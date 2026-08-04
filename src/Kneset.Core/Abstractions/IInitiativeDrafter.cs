using Kneset.Core.Models;

namespace Kneset.Core.Abstractions;

/// <summary>
/// AI-провайдер юридического структурирования гражданских инициатив.
/// Реализации: StubInitiativeDrafter (демо), позже — Claude API. Выбор — конфиг Ai:Provider.
/// </summary>
public interface IInitiativeDrafter
{
    string ModelVersion { get; }

    Task<InitiativeDraftResult> DraftAsync(InitiativeDraftRequest request, CancellationToken ct = default);
}
