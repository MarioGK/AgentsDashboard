using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class AutomationSchedulerService(
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    ILogger<AutomationSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Automation scheduler tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var repositories = await store.ListRepositoriesAsync(cancellationToken);

        foreach (var repository in repositories)
        {
            var automations = await store.ListAutomationDefinitionsAsync(repository.Id, cancellationToken);
            if (automations.Count == 0)
            {
                continue;
            }

            foreach (var automation in automations)
            {
                if (!automation.Enabled ||
                    !string.Equals(automation.TriggerKind, "cron", StringComparison.OrdinalIgnoreCase) ||
                    automation.NextRunAtUtc is null ||
                    automation.NextRunAtUtc > now)
                {
                    continue;
                }

                var task = await store.GetTaskAsync(automation.TaskId, cancellationToken);
                if (task is null || !task.Enabled)
                {
                    await RescheduleAsync(automation, cancellationToken);
                    continue;
                }

                var run = await store.CreateRunAsync(
                    task,
                    cancellationToken,
                    executionModeOverride: task.ExecutionModeDefault,
                    sessionProfileId: task.SessionProfileId,
                    automationRunId: automation.Id);

                var dispatchAccepted = await dispatcher.DispatchAsync(
                    repository,
                    task,
                    run,
                    cancellationToken,
                    sessionProfileId: run.SessionProfileId,
                    automationRunId: automation.Id);

                await store.CreateAutomationExecutionAsync(new AutomationExecutionDocument
                {
                    AutomationDefinitionId = automation.Id,
                    RepositoryId = automation.RepositoryId,
                    TaskId = automation.TaskId,
                    RunId = run.Id,
                    Status = dispatchAccepted ? "queued" : "rejected",
                    Message = dispatchAccepted ? "Run dispatched by scheduler." : "Run queued/rejected by dispatcher.",
                    TriggeredBy = "scheduler",
                    StartedAtUtc = now,
                    CompletedAtUtc = DateTime.UtcNow,
                }, cancellationToken);

                await RescheduleAsync(automation, cancellationToken);
            }
        }
    }

    private Task RescheduleAsync(AutomationDefinitionDocument automation, CancellationToken cancellationToken)
    {
        var request = new UpsertAutomationDefinitionRequest(
            automation.RepositoryId,
            automation.TaskId,
            automation.Name,
            automation.CronExpression,
            automation.TriggerKind,
            automation.ReplayPolicy,
            automation.Enabled);
        return store.UpsertAutomationDefinitionAsync(automation.Id, request, cancellationToken);
    }
}
