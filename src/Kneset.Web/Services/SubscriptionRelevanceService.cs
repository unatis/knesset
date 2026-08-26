using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Почему законопроект попал в личную ленту. Detail — то, что показывается
/// человеку: имя депутата или само слово; для подписки на конкретный закон
/// уточнять нечего.
/// </summary>
public sealed record SubscriptionReason(SubscriptionKind Kind, string? Detail);

/// <summary>
/// Сводка подписок для строки под табами. AllNewBills сюда не входит намеренно:
/// эта подписка включается всем при регистрации и означает ровно то же, что таб
/// «Всё», — считать её личным интересом нельзя, иначе «Моё» совпадёт со «Всё».
/// </summary>
public sealed record SubscriptionSummary(int Themes, int Persons, int Bills)
{
    public bool IsEmpty => Themes == 0 && Persons == 0 && Bills == 0;
}

/// <summary>
/// Связь между подписками пользователя и законопроектами: отдаёт карту
/// «Id закона → повод», по которой лента на главной фильтрует свои выборки.
/// Правило совпадения по слову берётся из NotificationSubscription.MatchesKeyword —
/// то же самое, по которому рассылаются уведомления.
/// </summary>
public class SubscriptionRelevanceService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Сколько свежих законопроектов просматриваем при сопоставлении со словами.
    /// Сравнение регистронезависимое и делается в памяти (как в рассылке), поэтому
    /// выборку надо ограничить: тянуть все 11 тысяч названий на каждый показ
    /// главной незачем, а лента всё равно показывает только свежие события.
    /// </summary>
    private const int KeywordCandidatePool = 500;

    public async Task<SubscriptionSummary> GetSummaryAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var byKind = await db.NotificationSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(SubscriptionKind kind) => byKind.FirstOrDefault(x => x.Kind == kind)?.Count ?? 0;

        return new SubscriptionSummary(
            Themes: Count(SubscriptionKind.Keyword),
            Persons: Count(SubscriptionKind.Person),
            Bills: Count(SubscriptionKind.Bill));
    }

    /// <summary>
    /// Карта «законопроект → повод». Пустая карта означает, что показывать
    /// в «Моём» нечего — у человека нет ни одной подписки на тему, депутата
    /// или закон.
    /// </summary>
    public async Task<Dictionary<int, SubscriptionReason>> GetRelevantBillsAsync(
        string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var subs = await db.NotificationSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Kind, s.PersonId, s.BillId, s.Keyword })
            .ToListAsync(ct);

        var billIds = subs.Where(s => s.Kind == SubscriptionKind.Bill && s.BillId != null)
            .Select(s => s.BillId!.Value).Distinct().ToList();
        var personIds = subs.Where(s => s.Kind == SubscriptionKind.Person && s.PersonId != null)
            .Select(s => s.PersonId!.Value).Distinct().ToList();
        var keywords = subs.Where(s => s.Kind == SubscriptionKind.Keyword && s.Keyword != null)
            .Select(s => s.Keyword!).Distinct().ToList();

        var result = new Dictionary<int, SubscriptionReason>();

        // Порядок заполнения — от самого конкретного повода к самому общему, как
        // в рассылке уведомлений: подписка на закон точнее подписки на депутата,
        // та — точнее подписки на слово. Первый записавшийся повод не переписывается.
        foreach (var id in billIds)
            result[id] = new SubscriptionReason(SubscriptionKind.Bill, null);

        if (personIds.Count > 0)
        {
            var byPerson = await db.BillInitiators.AsNoTracking()
                .Where(bi => personIds.Contains(bi.PersonId))
                .OrderByDescending(bi => bi.Bill.LastUpdatedDate)
                .Take(KeywordCandidatePool)
                .Select(bi => new
                {
                    bi.BillId,
                    PersonName = bi.Person.FirstName + " " + bi.Person.LastName
                })
                .ToListAsync(ct);

            foreach (var match in byPerson)
                if (!result.ContainsKey(match.BillId))
                    result[match.BillId] = new SubscriptionReason(
                        SubscriptionKind.Person, match.PersonName.Trim());
        }

        if (keywords.Count > 0)
        {
            var candidates = await db.Bills.AsNoTracking()
                .OrderByDescending(b => b.LastUpdatedDate)
                .Take(KeywordCandidatePool)
                .Select(b => new { b.Id, b.Name, b.NameRu })
                .ToListAsync(ct);

            foreach (var bill in candidates)
            {
                if (result.ContainsKey(bill.Id)) continue;

                var hit = keywords.FirstOrDefault(
                    k => NotificationSubscription.MatchesKeyword(bill.Name, bill.NameRu, k));
                if (hit is not null)
                    result[bill.Id] = new SubscriptionReason(SubscriptionKind.Keyword, hit);
            }
        }

        return result;
    }
}
