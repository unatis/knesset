using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Заглушка AI-анализа: возвращает помеченные демо-данные с реалистичной структурой,
/// чтобы очередь, хранение и UI были полностью проверяемы без API-ключа.
/// Реальный провайдер (ClaudeBillAnalyzer) подключается сменой конфига Ai:Provider.
/// </summary>
public class StubBillAnalyzer : IBillAnalyzer
{
    public string ModelVersion => "stub-v1";

    public async Task<BillAnalysisResult> AnalyzeAsync(BillAnalysisRequest request, CancellationToken ct = default)
    {
        // Имитация времени генерации, чтобы UI-состояние «генерируется...» было видно.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        return new BillAnalysisResult
        {
            TranslatedName = $"[Перевод недоступен] {request.NameHebrew}",
            Summary = "Это демонстрационный анализ. AI-провайдер ещё не подключён — " +
                      "структура карточки, очередь генерации и хранение в базе работают, " +
                      "но содержательный анализ появится после подключения Claude API.",
            AffectedGroups = ["Демо: граждане", "Демо: бизнес"],
            PotentialBenefits =
            [
                new AnalysisPoint
                {
                    Text = "Демо-пример возможного преимущества.",
                    Kind = "inference",
                    SourceRef = "демо",
                    Confidence = 0.5
                }
            ],
            PotentialRisks =
            [
                new AnalysisPoint
                {
                    Text = "Демо-пример возможного риска.",
                    Kind = "risk",
                    SourceRef = "демо",
                    Confidence = 0.5
                }
            ],
            ArgumentsFor =
            [
                new AnalysisPoint { Text = "Демо-аргумент «за».", Kind = "position" }
            ],
            ArgumentsAgainst =
            [
                new AnalysisPoint { Text = "Демо-аргумент «против».", Kind = "position" }
            ],
            OpenQuestions = ["Демо: какие вопросы оставляет текст инициативы?"],
            FinancialImpact = new FinancialImpact
            {
                Citizens = "Демо: влияние на граждан не оценено.",
                Government = "Демо: влияние на бюджет не оценено."
            },
            ProviderNote = "⚠ Демо-данные — AI-провайдер не подключён."
        };
    }
}
