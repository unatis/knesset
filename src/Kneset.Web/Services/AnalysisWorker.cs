using System.Text.Json;
using Kneset.Core.Abstractions;
using Kneset.Core.Entities;
using Kneset.Core.Models;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Воркер AI-анализов. Конвейер для пары (законопроект, язык):
/// 1) нет свежего мастер-анализа (en) — генерирует его (IBillAnalyzer);
/// 2) запрошен не-английский язык и свежего перевода нет — переводит мастер
///    (IAnalysisTranslator). Все версии сохраняются: устаревшие помечаются IsStale,
///    но не удаляются — история анализов доступна на странице закона.
/// </summary>
public class AnalysisWorker(
    AnalysisQueue queue,
    AnalysisClaims claims,
    IBillAnalyzer analyzer,
    IAnalysisTranslator translator,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    private const string MasterLang = "en";

    /// <summary>
    /// Язык, на котором написаны документы Кнессета. При переводе разбора
    /// на этот язык переводчику подаётся сам документ: иначе термины идут
    /// кругом — иврит в документе, английский в мастере, обратный перевод
    /// на иврит — и возвращаются пересказом вместо формулировок закона.
    /// </summary>
    private const string DocumentLang = "he";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (billId, lang) in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(billId, lang, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка генерации анализа {BillId}/{Lang}", billId, lang);
            }
            finally
            {
                queue.MarkCompleted(billId, lang);
            }
        }
    }

    private async Task ProcessAsync(int billId, string lang, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, ct);
        if (bill is null) return;

        var master = await EnsureMasterAsync(db, bill, ct);
        if (master is null) return;

        if (lang == MasterLang) return;

        await EnsureTranslationAsync(db, bill, master, lang, ct);
    }

    /// <summary>
    /// Мастер-анализ (en): берёт свежий или генерирует. null означает
    /// «сейчас его делает другой экземпляр» — тогда переводить нечего,
    /// и задача просто заканчивается.
    ///
    /// Захват берётся под ключом <see cref="AnalysisJob.MasterStep"/>, а не
    /// под языком: разбор один на все языки, и запрос на русский и на
    /// арабский не должны заказать два разбора.
    /// </summary>
    private async Task<BillAnalysisResult?> EnsureMasterAsync(
        AppDbContext db, Bill bill, CancellationToken ct)
    {
        var existing = await LoadFreshAsync(db, bill.Id, MasterLang, ct);
        if (existing is not null) return existing;

        if (!await claims.TryClaimAsync(bill.Id, AnalysisJob.MasterStep, ct)) return null;

        // Ещё одна проверка уже под захватом: между чтением выше и захватом
        // мог успеть завершиться другой экземпляр, и тогда платить незачем.
        existing = await LoadFreshAsync(db, bill.Id, MasterLang, ct);
        if (existing is not null)
        {
            await claims.ReleaseAsync(bill.Id, AnalysisJob.MasterStep, null, ct);
            return existing;
        }

        try
        {
            var fullText = await LoadDocumentTextAsync(db, bill.Id, ct);

            var master = await analyzer.AnalyzeAsync(new BillAnalysisRequest
            {
                BillId = bill.Id,
                NameHebrew = bill.Name,
                SubTypeDesc = bill.SubTypeDesc,
                StatusDesc = bill.StatusDesc,
                KnessetNum = bill.KnessetNum,
                SummaryLaw = bill.SummaryLaw,
                FullText = fullText,
                LanguageCode = MasterLang
            }, ct);

            db.BillAnalyses.Add(new BillAnalysis
            {
                BillId = bill.Id,
                AnalysisJson = JsonSerializer.Serialize(master),
                ModelVersion = analyzer.ModelVersion,
                LanguageCode = MasterLang,
                GeneratedAt = DateTime.UtcNow,
                BillLastUpdatedAt = bill.LastUpdatedDate
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Мастер-анализ {BillId} сохранён ({Model})", bill.Id, analyzer.ModelVersion);

            await claims.ReleaseAsync(bill.Id, AnalysisJob.MasterStep, null, ct);
            return master;
        }
        catch (Exception ex)
        {
            await claims.ReleaseAsync(bill.Id, AnalysisJob.MasterStep, ex.Message, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Перевод мастер-анализа на запрошенный язык, если свежего нет.</summary>
    private async Task EnsureTranslationAsync(
        AppDbContext db, Bill bill, BillAnalysisResult master, string lang, CancellationToken ct)
    {
        var hasFresh = await db.BillAnalyses
            .AnyAsync(a => a.BillId == bill.Id && a.LanguageCode == lang && !a.IsStale, ct);
        if (hasFresh) return;

        if (!await claims.TryClaimAsync(bill.Id, lang, ct)) return;

        // Та же проверка под захватом — именно её отсутствие и стоило нам
        // двух оплаченных русских переводов законопроекта 42.
        hasFresh = await db.BillAnalyses
            .AnyAsync(a => a.BillId == bill.Id && a.LanguageCode == lang && !a.IsStale, ct);
        if (hasFresh)
        {
            await claims.ReleaseAsync(bill.Id, lang, null, ct);
            return;
        }

        try
        {
            // При переводе на язык документа подаём оригинал: переводчик берёт
            // термины из него, а не переводит английские обратно. В остальных
            // случаях документ не нужен — он обычно крупнее самого разбора,
            // и платить за его токены незачем.
            var sourceDocument = lang == DocumentLang
                ? await LoadDocumentTextAsync(db, bill.Id, ct)
                : null;

            // Версия берётся из результата, а не у переводчика: составной
            // переводчик выбирает провайдера по ходу дела, и его свойство
            // сообщало того, к кому он пойдёт следующим.
            var (translated, translatorVersion) =
                await translator.TranslateAsync(master, lang, sourceDocument, ct);

            db.BillAnalyses.Add(new BillAnalysis
            {
                BillId = bill.Id,
                AnalysisJson = JsonSerializer.Serialize(translated),
                ModelVersion = translatorVersion,
                LanguageCode = lang,
                GeneratedAt = DateTime.UtcNow,
                BillLastUpdatedAt = bill.LastUpdatedDate
            });

            // Переведённое название — в карточку закона (пока только русское поле).
            if (lang == "ru" && translated.TranslatedName.Length > 0
                && !translated.TranslatedName.StartsWith('['))
            {
                await db.Bills.Where(b => b.Id == bill.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.NameRu, translated.TranslatedName), ct);
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Перевод анализа {BillId} на {Lang} сохранён ({Model})",
                bill.Id, lang, translatorVersion);

            await claims.ReleaseAsync(bill.Id, lang, null, ct);
        }
        catch (Exception ex)
        {
            await claims.ReleaseAsync(bill.Id, lang, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private static async Task<BillAnalysisResult?> LoadFreshAsync(
        AppDbContext db, int billId, string lang, CancellationToken ct)
    {
        var json = await db.BillAnalyses.AsNoTracking()
            .Where(a => a.BillId == billId && a.LanguageCode == lang && !a.IsStale)
            .OrderByDescending(a => a.GeneratedAt)
            .Select(a => a.AnalysisJson)
            .FirstOrDefaultAsync(ct);

        return json is null ? null : JsonSerializer.Deserialize<BillAnalysisResult>(json);
    }

    /// <summary>
    /// Текст документа законопроекта. То, на чём держится разбор: политика
    /// требует ссылок на источник, а без текста ссылаться не на что,
    /// и анализ выходит пересказом названия.
    ///
    /// Предпочитаем docx: там текст лежит логическим порядком, тогда как
    /// в PDF он восстановлен по координатам глифов. При равенстве берём
    /// самый длинный — короткие извлечения обычно значат неполный разбор.
    /// </summary>
    private static async Task<string?> LoadDocumentTextAsync(
        AppDbContext db, int billId, CancellationToken ct) =>
        await db.BillDocumentTexts.AsNoTracking()
            .Where(t => t.BillDocument.BillId == billId && t.Status == "ok")
            .OrderBy(t => t.BillDocument.Format == "DOC" ? 0 : 1)
            .ThenByDescending(t => t.CharCount)
            .Select(t => t.Text)
            .FirstOrDefaultAsync(ct);
}
