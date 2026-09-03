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
    IBillAnalyzer analyzer,
    IAnalysisTranslator translator,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    private const string MasterLang = "en";

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

        // 1. Мастер-анализ (en): генерируем, если свежего нет.
        var masterEntity = await db.BillAnalyses
            .Where(a => a.BillId == billId && a.LanguageCode == MasterLang && !a.IsStale)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        BillAnalysisResult master;
        if (masterEntity is null)
        {
            // Текст документа — то, на чём вообще держится разбор: политика
            // требует ссылок на источник, а без текста ссылаться не на что,
            // и анализ выходит пересказом названия. Берём docx, если он есть:
            // там логический порядок, тогда как в PDF он восстановлен
            // по координатам глифов.
            var fullText = await db.BillDocumentTexts.AsNoTracking()
                .Where(t => t.BillDocument.BillId == bill.Id && t.Status == "ok")
                .OrderBy(t => t.BillDocument.Format == "DOC" ? 0 : 1)
                .ThenByDescending(t => t.CharCount)
                .Select(t => t.Text)
                .FirstOrDefaultAsync(ct);

            master = await analyzer.AnalyzeAsync(new BillAnalysisRequest
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
            logger.LogInformation("Мастер-анализ {BillId} сохранён ({Model})", billId, analyzer.ModelVersion);
        }
        else
        {
            master = JsonSerializer.Deserialize<BillAnalysisResult>(masterEntity.AnalysisJson)
                     ?? throw new InvalidOperationException($"Не разобран мастер-анализ {masterEntity.Id}");
        }

        if (lang == MasterLang) return;

        // 2. Перевод на запрошенный язык, если свежего нет.
        var hasFreshTranslation = await db.BillAnalyses
            .AnyAsync(a => a.BillId == billId && a.LanguageCode == lang && !a.IsStale, ct);
        if (hasFreshTranslation) return;

        // Версия берётся из результата, а не у переводчика: составной
        // переводчик выбирает провайдера по ходу дела, и его свойство
        // сообщало того, к кому он пойдёт следующим.
        var (translated, translatorVersion) = await translator.TranslateAsync(master, lang, ct);

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
        if (lang == "ru" && translated.TranslatedName.Length > 0 && !translated.TranslatedName.StartsWith('['))
        {
            await db.Bills.Where(b => b.Id == bill.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.NameRu, translated.TranslatedName), ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Перевод анализа {BillId} на {Lang} сохранён ({Model})",
            billId, lang, translatorVersion);
    }
}
