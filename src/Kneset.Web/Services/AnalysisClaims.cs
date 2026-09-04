using Kneset.Core.Entities;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Захват шага разбора через базу: не позволяет двум экземплярам приложения
/// сделать — и оплатить — одну работу дважды.
///
/// Почему не уникальный индекс на BillAnalyses: он запрещает вторую запись,
/// но к моменту вставки за оба обращения к модели уже заплачено. Захват
/// берётся до вызова модели, поэтому проигравший вообще не тратит денег.
///
/// Почему вставкой, а не чтением: чтение у двух процессов может пройти
/// одновременно и оба увидят «свободно». Вставка в таблицу с уникальным
/// индексом — единственная операция, где база сама выбирает победителя.
/// </summary>
public class AnalysisClaims(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AnalysisClaims> logger)
{
    /// <summary>
    /// Срок захвата. Процесс может умереть на середине — на бесплатном Render
    /// инстанс засыпает, — и без срока такая строка заблокировала бы
    /// законопроект навсегда. Взято с запасом: разбор Opus на крупном
    /// документе идёт минуты.
    /// </summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(20);

    /// <summary>Имя экземпляра в журнале: чей захват, видно сразу.</summary>
    private static readonly string Instance =
        $"{Environment.MachineName}/{Environment.ProcessId}";

    /// <summary>
    /// Пытается взять шаг себе. false означает «этим уже занят кто-то живой» —
    /// вызывающий обязан ничего не делать, а не «попробовать всё равно».
    /// </summary>
    public async Task<bool> TryClaimAsync(int billId, string step, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        db.AnalysisJobs.Add(new AnalysisJob
        {
            BillId = billId,
            Step = step,
            State = AnalysisJob.Running,
            ClaimedAt = now,
            ClaimedBy = Instance,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Строка уже есть — либо кто-то работает, либо остался след
            // прошлой попытки. Решаем по состоянию и сроку.
        }

        await using var read = await dbFactory.CreateDbContextAsync(ct);
        var existing = await read.AnalysisJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.BillId == billId && j.Step == step, ct);

        if (existing is null) return false;   // исчезла между двумя запросами

        if (existing.State == AnalysisJob.Running && existing.ClaimedAt > now - Lease)
        {
            logger.LogInformation(
                "Шаг {Step} по {BillId} уже считает {Who} с {When} — не дублирую",
                step, billId, existing.ClaimedBy, existing.ClaimedAt);
            return false;
        }

        // Перехват: либо прошлая попытка завершилась, либо её процесс умер.
        // Условие по ClaimedAt делает перехват атомарным — если кто-то
        // успел раньше, обновится ноль строк, и мы не станем считать.
        var taken = await read.AnalysisJobs
            .Where(j => j.Id == existing.Id && j.ClaimedAt == existing.ClaimedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, AnalysisJob.Running)
                .SetProperty(j => j.ClaimedAt, now)
                .SetProperty(j => j.ClaimedBy, Instance)
                .SetProperty(j => j.FinishedAt, (DateTime?)null)
                .SetProperty(j => j.Error, (string?)null), ct);

        if (taken == 0)
        {
            logger.LogInformation(
                "Шаг {Step} по {BillId} перехватил кто-то другой — не дублирую", step, billId);
            return false;
        }

        if (existing.State == AnalysisJob.Running)
        {
            logger.LogWarning(
                "Шаг {Step} по {BillId} висел в работе у {Who} с {When} дольше срока — забираю",
                step, billId, existing.ClaimedBy, existing.ClaimedAt);
        }

        return true;
    }

    /// <summary>Отпускает шаг. Причина неудачи сохраняется: по ней потом
    /// можно объяснить человеку, что именно не вышло.</summary>
    public async Task ReleaseAsync(
        int billId, string step, string? error, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.AnalysisJobs
            .Where(j => j.BillId == billId && j.Step == step)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, error is null ? AnalysisJob.Done : AnalysisJob.Failed)
                .SetProperty(j => j.FinishedAt, DateTime.UtcNow)
                .SetProperty(j => j.Error, error is null ? null : Trim(error)), ct);
    }

    private static string Trim(string error) =>
        error.Length > 2000 ? error[..2000] : error;
}
