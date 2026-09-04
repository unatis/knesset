using Kneset.Core.Abstractions;
using Kneset.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Переводит бесплатным провайдером, пока тот не упрётся в суточную квоту,
/// затем платным.
///
/// Дважды проверенный расчёт: перевод — не то место, где модели расходятся
/// по существу (изобретать позиции здесь нечего), а Gemini на бесплатном
/// тарифе уже перевёл 2 316 названий законопроектов за ноль шекелей.
/// Ограничивает его не качество, а 20 запросов в сутки на модель.
///
/// Исчерпание запоминается до конца суток UTC: иначе каждый перевод будет
/// тратить запрос только ради того, чтобы снова узнать, что запросов нет.
///
/// Структура перевода сверяется с оригиналом (<see cref="AnalysisShape"/>):
/// когда переводят разные модели разных вендоров, требование политики
/// об идентичном содержании на всех языках перестаёт быть само собой
/// разумеющимся. Расхождение — повод отдать перевод платному провайдеру,
/// а не опубликовать.
/// </summary>
public class FallbackAnalysisTranslator(
    GeminiAnalysisTranslator free,
    IAnalysisTranslator paid,
    ILogger<FallbackAnalysisTranslator> logger) : IAnalysisTranslator
{
    private DateOnly? _exhaustedOn;

    private bool QuotaAvailable => _exhaustedOn != DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<AnalysisTranslation> TranslateAsync(
        BillAnalysisResult source, string targetLanguage,
        string? sourceDocument = null, CancellationToken ct = default)
    {
        if (QuotaAvailable)
        {
            try
            {
                var free_ = await free.TranslateAsync(source, targetLanguage, sourceDocument, ct);

                var diff = AnalysisShape.Diff(source, free_.Analysis);
                if (diff.Length == 0) return free_;

                logger.LogWarning(
                    "Перевод на {Lang} от {Model} расходится с оригиналом по структуре " +
                    "({Diff}) — отдаю платному провайдеру",
                    targetLanguage, free_.ModelVersion, diff);
            }
            catch (GeminiAnalysisTranslator.QuotaExhaustedException ex)
            {
                _exhaustedOn = DateOnly.FromDateTime(DateTime.UtcNow);
                logger.LogInformation(
                    "{Message}. До конца суток перевожу платным провайдером", ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       || !ct.IsCancellationRequested)
            {
                // Сетевая неполадка у бесплатного провайдера не повод терять
                // перевод: платный доступен.
                //
                // Про условие: истёкший таймаут HttpClient приходит как
                // TaskCanceledException, то есть как наследник
                // OperationCanceledException. Прежний фильтр принимал его
                // за отмену со стороны вызывающего и пропускал наружу —
                // и перевод терялся целиком, хотя платный провайдер был
                // доступен. Отличаем по самому токену: если его никто
                // не отменял, это не отмена, а неполадка.
                logger.LogWarning(ex,
                    "Бесплатный переводчик не справился, отдаю платному");
            }
        }

        var paidResult = await paid.TranslateAsync(source, targetLanguage, sourceDocument, ct);

        var paidDiff = AnalysisShape.Diff(source, paidResult.Analysis);
        if (paidDiff.Length > 0)
        {
            // Здесь уже не к кому уходить — записываем в лог и отдаём как есть:
            // расхождение по структуре хуже отсутствия перевода не настолько,
            // чтобы оставить пользователя без разбора вообще.
            logger.LogWarning(
                "Перевод на {Lang} от {Model} расходится с оригиналом по структуре: {Diff}",
                targetLanguage, paidResult.ModelVersion, paidDiff);
        }

        return paidResult;
    }
}
