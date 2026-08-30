using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Подпись под гражданской инициативой. Вынесена из страницы инициативы,
/// потому что подписывать теперь можно и прямо из ленты: две копии этой
/// логики разошлись бы на переходе через порог, а он меняет статус
/// инициативы — цена расхождения выше, чем у обычного дубля.
/// </summary>
public class InitiativeSigningService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Ставит подпись и, если порог достигнут, переводит инициативу
    /// в ThresholdReached. Повторная подпись безопасна.
    /// </summary>
    public async Task SignAsync(int initiativeId, string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        try
        {
            db.InitiativeSignatures.Add(new InitiativeSignature
            {
                InitiativeId = initiativeId,
                UserId = userId,
                SignedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Уникальный индекс: уже подписал (гонка двойного клика) — игнорируем.
        }

        var threshold = await db.CitizenInitiatives
            .Where(i => i.Id == initiativeId)
            .Select(i => (int?)i.SignatureThreshold)
            .FirstOrDefaultAsync(ct);
        if (threshold is null) return;

        var count = await db.InitiativeSignatures.CountAsync(s => s.InitiativeId == initiativeId, ct);
        if (count >= threshold)
        {
            // Условие по статусу в самом запросе: два одновременных подписанта
            // не должны дважды переводить инициативу через порог.
            await db.CitizenInitiatives
                .Where(i => i.Id == initiativeId && i.Status == InitiativeStatus.Published)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.Status, InitiativeStatus.ThresholdReached), ct);
        }
    }

    /// <summary>Какие из перечисленных инициатив человек уже подписал.</summary>
    public async Task<HashSet<int>> GetSignedAsync(
        IReadOnlyCollection<int> initiativeIds, string userId, CancellationToken ct = default)
    {
        if (initiativeIds.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = initiativeIds.ToList();

        return (await db.InitiativeSignatures.AsNoTracking()
                .Where(s => s.UserId == userId && ids.Contains(s.InitiativeId))
                .Select(s => s.InitiativeId)
                .ToListAsync(ct))
            .ToHashSet();
    }
}
