using System.Security.Cryptography;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Kneset.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Превращает документы законопроектов в текст: скачивает файл с сайта
/// Кнессета, разбирает и складывает результат в BillDocumentTexts.
///
/// Берёт только формат DOC — по факту это .docx, и текст из него выходит
/// логическим порядком. PDF пока не трогаем: PdfPig отдаёт иврит наизнанку
/// и без пробелов между словами, для него нужен отдельный разбор по словам
/// с восстановлением bidi-порядка.
///
/// Обход идёт партиями и возобновляется: «что разобрать дальше» — это запрос
/// к базе, а не позиция в памяти, поэтому прогон можно прерывать сколько
/// угодно раз. Сначала окно влияния (там текст нужен для анализа), потом
/// остальное от свежих созывов к старым.
///
/// Выключен по умолчанию: включается Documents:Extract:Enabled. Разовый
/// проход по корпусу — это несколько часов и 11.5 тысяч запросов к Кнессету,
/// такое не должно запускаться само при каждом старте приложения.
/// </summary>
public class DocumentTextService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<DocumentTextService> logger) : BackgroundService
{
    private const string EntityName = "BillDocumentTexts";

    /// <summary>Разбираем только docx — см. комментарий к классу.</summary>
    private const string TargetFormat = "DOC";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Documents:Extract:Enabled", false))
        {
            logger.LogInformation(
                "Извлечение текста документов выключено (Documents:Extract:Enabled).");
            return;
        }

        // Даём приложению подняться и отдать первые страницы, прежде чем
        // занимать сеть и базу фоновой работой.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        var batchSize = config.GetValue("Documents:Extract:BatchSize", 50);
        var delayMs = config.GetValue("Documents:Extract:DelayMs", 250);
        var maxPerRun = config.GetValue("Documents:Extract:MaxPerRun", int.MaxValue);

        await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        var log = new SyncLog { EntityName = EntityName, StartedUtc = DateTime.UtcNow };
        db.SyncLogs.Add(log);
        await db.SaveChangesAsync(stoppingToken);

        var processed = 0;
        try
        {
            while (processed < maxPerRun && !stoppingToken.IsCancellationRequested)
            {
                var batch = await NextBatchAsync(
                    Math.Min(batchSize, maxPerRun - processed), stoppingToken);
                if (batch.Count == 0) break;

                processed += await ProcessBatchAsync(batch, delayMs, stoppingToken);
                logger.LogInformation("Текст документов: разобрано {Processed}", processed);
            }

            log.RecordsUpserted = processed;
            log.FinishedUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Остановка приложения — не ошибка. Незакрытый лог покажет,
            // что проход был прерван, и следующий старт продолжит с того же места.
            log.RecordsUpserted = processed;
        }
        catch (Exception ex)
        {
            log.Error = ex.Message;
            log.FinishedUtc = DateTime.UtcNow;
            logger.LogError(ex, "Извлечение текста документов прервано");
        }

        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Текст документов: проход завершён, разобрано {Processed}", processed);
    }

    /// <summary>
    /// Следующая партия документов без актуального текста. Приоритет — окно
    /// влияния, затем свежие созывы. Запрос к базе, а не курсор в памяти,
    /// поэтому прогон возобновляем.
    /// </summary>
    private async Task<List<BillDocument>> NextBatchAsync(int size, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var version = DocumentTextExtractor.Version;

        var pending = db.BillDocuments
            .Where(d => d.Format == TargetFormat)
            // Уже разобранное этой же версией парсера пропускаем — включая
            // неудачи: файл, который не разбирается, не станет разбираться
            // от повторной попытки тем же кодом.
            .Where(d => d.ExtractedText == null || d.ExtractedText.ExtractorVersion != version);

        var window = await db.Bills
            .Where(b => b.StatusId != null && InfluenceStages.Contains(b.StatusId.Value))
            .Select(b => b.StatusDesc)
            .Distinct()
            .ToListAsync(ct);

        var first = await pending
            .Where(d => window.Contains(d.Bill.StatusDesc))
            .OrderByDescending(d => d.Bill.KnessetNum)
            .ThenBy(d => d.Id)
            .Take(size)
            .ToListAsync(ct);

        if (first.Count >= size) return first;

        var more = await pending
            .Where(d => !window.Contains(d.Bill.StatusDesc))
            .OrderByDescending(d => d.Bill.KnessetNum)
            .ThenBy(d => d.Id)
            .Take(size - first.Count)
            .ToListAsync(ct);

        first.AddRange(more);
        return first;
    }

    private async Task<int> ProcessBatchAsync(
        List<BillDocument> batch, int delayMs, CancellationToken ct)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(90);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = batch.Select(d => d.Id).ToList();
        var existing = await db.BillDocumentTexts
            .Where(t => ids.Contains(t.BillDocumentId))
            .ToDictionaryAsync(t => t.BillDocumentId, ct);

        foreach (var doc in batch)
        {
            var row = Extract(await FetchAsync(http, doc.Url, ct), doc.Id);

            if (existing.TryGetValue(doc.Id, out var prev))
            {
                prev.Text = row.Text;
                prev.CharCount = row.CharCount;
                prev.ExtractorVersion = row.ExtractorVersion;
                prev.SourceHash = row.SourceHash;
                prev.SourceBytes = row.SourceBytes;
                prev.ExtractedAt = row.ExtractedAt;
                prev.Status = row.Status;
                prev.Error = row.Error;
            }
            else
            {
                db.BillDocumentTexts.Add(row);
            }

            // Вежливость к серверу Кнессета: 11.5 тысяч файлов — это не повод
            // выгружать их залпом.
            if (delayMs > 0) await Task.Delay(delayMs, ct);
        }

        await db.SaveChangesAsync(ct);
        return batch.Count;
    }

    private static async Task<(byte[]? Bytes, string? Error)> FetchAsync(
        HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, ct);
            return resp.IsSuccessStatusCode
                ? (await resp.Content.ReadAsByteArrayAsync(ct), null)
                : (null, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.GetType().Name);
        }
    }

    private static BillDocumentText Extract((byte[]? Bytes, string? Error) fetched, int documentId)
    {
        var row = new BillDocumentText
        {
            BillDocumentId = documentId,
            ExtractorVersion = DocumentTextExtractor.Version,
            ExtractedAt = DateTime.UtcNow,
        };

        if (fetched.Bytes is not { } bytes)
        {
            row.Status = "error";
            row.Error = fetched.Error;
            return row;
        }

        row.SourceBytes = bytes.Length;
        row.SourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var result = DocumentTextExtractor.Extract(bytes);
        row.Text = result.Text;
        row.CharCount = result.CharCount;
        row.Status = result.Error is not null ? "unsupported"
            : result.CharCount == 0 ? "empty"
            : "ok";
        row.Error = result.Error;
        return row;
    }

    /// <summary>
    /// Этапы окна влияния — те же, что в фильтре по умолчанию на /bills.
    /// </summary>
    private static readonly int[] InfluenceStages = [108, 113, 150, 111, 114, 130, 141];
}
