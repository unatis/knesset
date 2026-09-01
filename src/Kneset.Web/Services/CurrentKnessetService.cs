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

    public async Task<int?> GetAsync(CancellationToken ct = default) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Bills.MaxAsync(b => (int?)b.KnessetNum, ct);
        });
}
