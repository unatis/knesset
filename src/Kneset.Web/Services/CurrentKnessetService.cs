using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Kneset.Web.Services;

/// <summary>
/// Номер текущего созыва Кнессета.
///
/// Нужен всюду, где рисуется окно влияния: законопроект прошлого созыва
/// прекратился вместе с ним, каким бы ни был текст его статуса. Без этого
/// числа плашка сравнивать не с чем.
///
/// Берётся как максимум по законопроектам, а не из внешнего запроса:
/// синхронизация всё равно тянет только текущий созыв и предыдущий, так что
/// максимум в базе и есть текущий. Значение меняется раз в четыре года,
/// поэтому кэш на шесть часов — это про то, чтобы не ходить в базу из каждой
/// карточки, а не про свежесть.
/// </summary>
public sealed class CurrentKnessetService(
    IDbContextFactory<AppDbContext> factory,
    IMemoryCache cache)
{
    private const string CacheKey = "current-knesset-num";

    private const string EndedKey = "current-knesset-ended-on";

    public async Task<int?> GetAsync(CancellationToken ct = default) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Bills.MaxAsync(b => (int?)b.KnessetNum, ct);
        });

    /// <summary>
    /// Дата окончания текущего созыва, если он уже закончился, иначе null.
    ///
    /// Зачем это нужно отдельно от номера: с концом созыва законодательная
    /// работа останавливается — комиссии не заседают, чтений не проводится, —
    /// но статусы законопроектов при этом не меняются. Без этой даты сайт
    /// продолжает обещать открытое окно влияния у законопроектов созыва,
    /// который распущен. Так и вышло с 25-м: он закончился 17 июля 2026,
    /// а плашки на живом сайте по-прежнему говорили «окно влияния открыто».
    ///
    /// Берётся из `KnessetTerms`, которую наполняет синхронизация
    /// по `KNS_KnessetDates`, то есть из данных самого Кнессета.
    /// </summary>
    public async Task<DateOnly?> GetEndedOnAsync(CancellationToken ct = default)
    {
        var num = await GetAsync(ct);
        if (num is null) return null;

        return await cache.GetOrCreateAsync<DateOnly?>($"{EndedKey}:{num}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

            await using var db = await factory.CreateDbContextAsync(ct);
            var finish = await db.KnessetTerms
                .Where(t => t.Id == num)
                .Select(t => t.FinishDate)
                .FirstOrDefaultAsync(ct);

            // Только уже наступившая дата: у идущего созыва конец последней
            // сессии стоит в будущем, и это не роспуск.
            return finish is { } f && f < DateOnly.FromDateTime(DateTime.UtcNow) ? f : null;
        });
    }
}
