using System.Text.Json;
using Kneset.Core.Abstractions;
using Kneset.Core.Models;
using Kneset.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kneset.Web.Services;

/// <summary>
/// Фоновый воркер AI-структурирования инициатив: берёт из DraftQueue, вызывает
/// IInitiativeDrafter, сохраняет StructuredJson. Ошибка не останавливает очередь.
/// </summary>
public class DraftWorker(
    DraftQueue queue,
    IInitiativeDrafter drafter,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<DraftWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var initiativeId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DraftAsync(initiativeId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка структурирования инициативы {InitiativeId}", initiativeId);
            }
            finally
            {
                queue.MarkCompleted(initiativeId);
            }
        }
    }

    private async Task DraftAsync(int initiativeId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var initiative = await db.CitizenInitiatives.FirstOrDefaultAsync(i => i.Id == initiativeId, ct);
        if (initiative is null || initiative.StructuredJson is not null) return;

        var result = await drafter.DraftAsync(new InitiativeDraftRequest
        {
            InitiativeId = initiative.Id,
            Title = initiative.Title,
            ProblemDescription = initiative.ProblemDescription,
            ProposedSolution = initiative.ProposedSolution
        }, ct);

        initiative.StructuredJson = JsonSerializer.Serialize(result);
        initiative.ModelVersion = drafter.ModelVersion;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Структура инициативы {InitiativeId} сохранена ({Model})",
            initiativeId, drafter.ModelVersion);
    }
}
