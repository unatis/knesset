using Kneset.Core.Abstractions;
using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Рассылка уведомлений о новых законопроектах и смене их стадий.
/// Вызывается последним шагом синхронизации: подписка на депутата требует
/// таблицы BillInitiators, которая заполняется уже после самих законопроектов.
/// </summary>
public class NotificationDispatchService(
    IDbContextFactory<AppDbContext> dbFactory,
    NotificationTextBuilder textBuilder,
    IEnumerable<INotificationChannel> channels,
    NotificationEvents events,
    IConfiguration configuration,
    ILogger<NotificationDispatchService> logger)
{
    /// <summary>
    /// Предохранитель: если кандидатов вдруг оказалось слишком много (например,
    /// база наполнилась заново), рассылаем только самое свежее, а не заваливаем
    /// каждого подписчика тысячами записей.
    /// </summary>
    private const int MaxBillsPerRun = 500;

    private const int RetentionDays = 30;

    public async Task<int> DispatchAsync(DateTime? since, CancellationToken ct)
    {
        // Первый прогон на пустых логах: точка отсчёта — сейчас, историю не рассылаем.
        var from = since ?? DateTime.UtcNow;

        var created = await DispatchNewBillsAsync(from, ct);
        created += await DispatchStatusChangesAsync(from, ct);

        await CleanupAsync(ct);

        if (created > 0)
        {
            logger.LogInformation("Уведомления: создано {Count}", created);
            events.RaiseCreated();
        }

        return created;
    }

    private async Task<int> DispatchNewBillsAsync(DateTime from, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var bills = await db.Bills.AsNoTracking()
            .Where(b => b.FirstSeenAt > from)
            .OrderByDescending(b => b.FirstSeenAt)
            .Take(MaxBillsPerRun + 1)
            .Select(b => new { b.Id, b.Name, b.NameRu, b.FirstSeenAt })
            .ToListAsync(ct);

        if (bills.Count > MaxBillsPerRun)
        {
            logger.LogWarning(
                "Уведомления: кандидатов больше {Max}, рассылаем только самые свежие. " +
                "Похоже на массовое наполнение базы, а не на реальные новые законопроекты",
                MaxBillsPerRun);
            bills = bills.Take(MaxBillsPerRun).ToList();
        }

        if (bills.Count == 0) return 0;

        var billIds = bills.Select(b => b.Id).ToList();

        // Подписки «все новые» — одинаковы для всех законопроектов прогона.
        var allNewUsers = await db.NotificationSubscriptions.AsNoTracking()
            .Where(s => s.Kind == SubscriptionKind.AllNewBills)
            .Select(s => s.UserId)
            .ToListAsync(ct);

        // Подписки на депутатов, которые оказались инициаторами этих законопроектов.
        var personMatches = await db.BillInitiators.AsNoTracking()
            .Where(bi => billIds.Contains(bi.BillId))
            .Join(db.NotificationSubscriptions.Where(s => s.Kind == SubscriptionKind.Person),
                bi => bi.PersonId, s => s.PersonId,
                (bi, s) => new { bi.BillId, s.UserId, PersonName = bi.Person.FirstName + " " + bi.Person.LastName })
            .ToListAsync(ct);

        var keywordSubs = await db.NotificationSubscriptions.AsNoTracking()
            .Where(s => s.Kind == SubscriptionKind.Keyword && s.Keyword != null)
            .Select(s => new { s.UserId, s.Keyword })
            .ToListAsync(ct);

        var candidates = new List<Notification>();

        foreach (var bill in bills)
        {
            // Повод от самого конкретного к самому общему: если человек подписан
            // и на депутата, и на слово — уведомление одно, с более точным поводом.
            var byUser = new Dictionary<string, (SubscriptionKind Kind, string? Detail)>();

            foreach (var match in personMatches.Where(m => m.BillId == bill.Id))
                byUser[match.UserId] = (SubscriptionKind.Person, match.PersonName.Trim());

            foreach (var sub in keywordSubs)
            {
                if (byUser.ContainsKey(sub.UserId)) continue;
                if (Matches(bill.Name, bill.NameRu, sub.Keyword!))
                    byUser[sub.UserId] = (SubscriptionKind.Keyword, sub.Keyword);
            }

            foreach (var userId in allNewUsers)
            {
                if (byUser.ContainsKey(userId)) continue;
                byUser[userId] = (SubscriptionKind.AllNewBills, null);
            }

            candidates.AddRange(byUser.Select(kv => new Notification
            {
                UserId = kv.Key,
                Kind = NotificationKind.NewBill,
                BillId = bill.Id,
                TriggeredBy = kv.Value.Kind,
                TriggerDetail = kv.Value.Detail,
                EventAt = bill.FirstSeenAt,
                CreatedAt = DateTime.UtcNow
            }));
        }

        return await SaveAsync(candidates, ct);
    }

    private async Task<int> DispatchStatusChangesAsync(DateTime from, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Смена стадии интересна только тем, кто следит за конкретным законом:
        // рассылать её всем подписчикам «все новые» — гарантированный спам.
        var changes = await db.Bills.AsNoTracking()
            .Where(b => b.StatusChangedAt != null && b.StatusChangedAt > from)
            .Join(db.NotificationSubscriptions.Where(s => s.Kind == SubscriptionKind.Bill),
                b => b.Id, s => s.BillId,
                (b, s) => new { b.Id, b.StatusChangedAt, s.UserId })
            .Take(MaxBillsPerRun)
            .ToListAsync(ct);

        var candidates = changes.Select(c => new Notification
        {
            UserId = c.UserId,
            Kind = NotificationKind.BillStatusChanged,
            BillId = c.Id,
            TriggeredBy = SubscriptionKind.Bill,
            EventAt = c.StatusChangedAt!.Value,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        return await SaveAsync(candidates, ct);
    }

    /// <summary>
    /// Сохраняет уведомления, отсеивая уже существующие, и отдаёт их каналам доставки.
    /// </summary>
    private async Task<int> SaveAsync(List<Notification> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return 0;

        var saved = 0;

        foreach (var chunk in candidates.Chunk(500))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var billIds = chunk.Select(c => c.BillId).Distinct().ToList();
            var userIds = chunk.Select(c => c.UserId).Distinct().ToList();

            var existing = await db.Notifications.AsNoTracking()
                .Where(n => userIds.Contains(n.UserId) && billIds.Contains(n.BillId))
                .Select(n => new { n.UserId, n.BillId, n.Kind, n.EventAt })
                .ToListAsync(ct);

            var known = existing
                .Select(e => (e.UserId, e.BillId, e.Kind, e.EventAt))
                .ToHashSet();

            var fresh = chunk
                .Where(c => known.Add((c.UserId, c.BillId, c.Kind, c.EventAt)))
                .ToList();

            if (fresh.Count == 0) continue;

            db.Notifications.AddRange(fresh);
            await db.SaveChangesAsync(ct);
            saved += fresh.Count;

            await DeliverAsync(db, fresh, ct);
        }

        return saved;
    }

    /// <summary>
    /// Отдаёт уведомления во включённые пользователем каналы. Сейчас по-настоящему
    /// работает только колокольчик, остальные каналы — заглушки, пишущие в лог.
    /// </summary>
    private async Task DeliverAsync(AppDbContext db, List<Notification> notifications, CancellationToken ct)
    {
        var userIds = notifications.Select(n => n.UserId).Distinct().ToList();

        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.PreferredLanguage, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        var userChannels = (await db.UserNotificationChannels.AsNoTracking()
                .Where(c => userIds.Contains(c.UserId) && c.IsEnabled)
                .ToListAsync(ct))
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var baseUri = configuration["PublicBaseUrl"] ?? "https://knesset-ksr9.onrender.com";

        // Названия грузим по языкам: у разных получателей разный язык уведомления.
        var billIds = notifications.Select(n => n.BillId).Distinct().ToList();
        var titlesByLang = new Dictionary<string, Dictionary<int, string>>();

        foreach (var lang in users.Values.Select(u => u.PreferredLanguage).Distinct())
            titlesByLang[lang] = await textBuilder.LoadTitlesAsync(billIds, lang, ct);

        var deliveries = new List<NotificationDelivery>();

        foreach (var notification in notifications)
        {
            if (!users.TryGetValue(notification.UserId, out var user)) continue;

            var title = titlesByLang[user.PreferredLanguage].GetValueOrDefault(notification.BillId, "");

            // Колокольчик доставлен самим фактом существования строки.
            deliveries.Add(new NotificationDelivery
            {
                NotificationId = notification.Id,
                Channel = NotificationChannelKind.InApp,
                Status = DeliveryStatus.Sent,
                AttemptedAt = DateTime.UtcNow
            });

            foreach (var setting in userChannels.GetValueOrDefault(notification.UserId, []))
            {
                if (setting.Channel == NotificationChannelKind.InApp) continue;

                var channel = channels.FirstOrDefault(c => c.Kind == setting.Channel);
                if (channel is null) continue;

                // Почта по умолчанию берётся из аккаунта, остальным каналам адрес нужен явно.
                var address = setting.Address
                    ?? (setting.Channel == NotificationChannelKind.Email ? user.Email : null);

                if (string.IsNullOrWhiteSpace(address))
                {
                    deliveries.Add(new NotificationDelivery
                    {
                        NotificationId = notification.Id,
                        Channel = setting.Channel,
                        Status = DeliveryStatus.Skipped,
                        Error = "Адрес доставки не задан",
                        AttemptedAt = DateTime.UtcNow
                    });
                    continue;
                }

                var message = textBuilder.BuildMessage(
                    notification, title, user.PreferredLanguage, address, baseUri);

                DeliveryResult result;
                try
                {
                    result = await channel.SendAsync(message, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Канал {Channel}: ошибка доставки", setting.Channel);
                    result = DeliveryResult.Failed(ex.Message);
                }

                deliveries.Add(new NotificationDelivery
                {
                    NotificationId = notification.Id,
                    Channel = setting.Channel,
                    Status = result.Status,
                    Error = result.Error?[..Math.Min(1000, result.Error.Length)],
                    AttemptedAt = DateTime.UtcNow
                });
            }
        }

        db.NotificationDeliveries.AddRange(deliveries);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Прочитанные уведомления старше месяца не нужны — таблица растёт быстро.</summary>
    private async Task CleanupAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var threshold = DateTime.UtcNow.AddDays(-RetentionDays);

        var removed = await db.Notifications
            .Where(n => n.ReadAt != null && n.CreatedAt < threshold)
            .ExecuteDeleteAsync(ct);

        if (removed > 0)
            logger.LogInformation("Уведомления: удалено прочитанных старше {Days} дней — {Count}", RetentionDays, removed);
    }

    private static bool Matches(string name, string? nameRu, string keyword)
    {
        var needle = keyword.Trim();
        if (needle.Length == 0) return false;

        return name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               (nameRu is not null && nameRu.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
