using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Заглушка AI-структурирования инициатив: помеченные демо-данные с реалистичной
/// структурой. Реальный провайдер подключается сменой конфига Ai:Provider.
/// </summary>
public class StubInitiativeDrafter : IInitiativeDrafter
{
    public string ModelVersion => "stub-v1";

    public async Task<InitiativeDraftResult> DraftAsync(InitiativeDraftRequest request, CancellationToken ct = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        return new InitiativeDraftResult
        {
            Goal = $"[Демо] Юридически сформулированная цель инициативы «{request.Title}» появится после подключения AI-провайдера.",
            ProposedChanges =
            [
                "Демо: пункт предлагаемых изменений 1.",
                "Демо: пункт предлагаемых изменений 2."
            ],
            AffectedLaws = ["Демо: перечень затрагиваемых законов определит AI."],
            ExplanatoryNote = "Это демонстрационная структура. AI-провайдер ещё не подключён — " +
                              "механизм инициатив, подписи и хранение работают, а юридическое " +
                              "оформление текста появится после подключения Claude API.",
            FinancialImpactNote = "Демо: финансовые последствия не оценены.",
            SocialImpactNote = "Демо: социальные последствия не оценены.",
            Risks = ["Демо: возможные риски и возражения определит AI."],
            ProviderNote = "⚠ Демо-данные — AI-провайдер не подключён."
        };
    }
}
