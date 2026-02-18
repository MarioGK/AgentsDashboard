namespace AgentsDashboard.ControlPlane.Services;

using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class AlertingService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<AlertingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IOrchestratorStore>();

                var rules = await store.ListEnabledAlertRulesAsync(stoppingToken);

                foreach (var rule in rules)
                {
                    await CheckRuleAsync(rule, store, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, "Error during alerting check cycle");
            }
        }
    }

    private async Task CheckRuleAsync(AlertRuleDocument rule, IOrchestratorStore store, CancellationToken cancellationToken)
    {
        try
        {
            if (rule.LastFiredAtUtc.HasValue && rule.CooldownMinutes > 0)
            {
                var cooldownEnd = rule.LastFiredAtUtc.Value.AddMinutes(rule.CooldownMinutes);
                if (DateTime.UtcNow < cooldownEnd)
                {
                    logger.ZLogDebug("Alert rule {RuleName} is in cooldown until {CooldownEnd}", rule.Name, cooldownEnd);
                    return;
                }
            }

            var (triggered, message) = rule.RuleType switch
            {
                AlertRuleType.MissingHeartbeat => await CheckMissingHeartbeatAsync(rule, store, cancellationToken),
                AlertRuleType.FailureRateSpike => await CheckFailureRateSpikeAsync(rule, store, cancellationToken),
                AlertRuleType.QueueBacklog => await CheckQueueBacklogAsync(rule, store, cancellationToken),
                AlertRuleType.RepeatedPrFailures => await CheckRepeatedPrFailuresAsync(rule, store, cancellationToken),
                AlertRuleType.RouteLeakDetection => await CheckRouteLeakDetectionAsync(rule, store, cancellationToken),
                _ => (false, string.Empty)
            };

            if (triggered)
            {
                logger.ZLogWarning("Alert rule {RuleName} triggered: {Message}", rule.Name, message);
                await FireAlertAsync(rule, message, store, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Error checking alert rule {RuleName} ({RuleType})", rule.Name, rule.RuleType);
        }
    }

    private async Task<(bool triggered, string message)> CheckMissingHeartbeatAsync(
        AlertRuleDocument rule,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var workers = await store.ListTaskRuntimesAsync(cancellationToken);
        var staleThreshold = DateTime.UtcNow.AddMinutes(-rule.Threshold);

        var staleWorkers = workers
            .Where(w => w.Online && w.LastHeartbeatUtc < staleThreshold)
            .ToList();

        if (staleWorkers.Count > 0)
        {
            var workerIds = string.Join(", ", staleWorkers.Select(w => w.TaskRuntimeId));
            return (true, $"{staleWorkers.Count} worker(s) missing heartbeat for {rule.Threshold} minutes: {workerIds}");
        }

        return (false, string.Empty);
    }

    private async Task<(bool triggered, string message)> CheckFailureRateSpikeAsync(
        AlertRuleDocument rule,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var failedRuns = await store.ListRunsByStateAsync(RunState.Failed, cancellationToken);
        var windowStart = DateTime.UtcNow.AddMinutes(-rule.WindowMinutes);
        var recentFailures = failedRuns.Where(r => r.EndedAtUtc >= windowStart).ToList();

        if (recentFailures.Count >= rule.Threshold)
        {
            return (true, $"{recentFailures.Count} runs failed in the last {rule.WindowMinutes} minutes (threshold: {rule.Threshold})");
        }

        return (false, string.Empty);
    }

    private async Task<(bool triggered, string message)> CheckQueueBacklogAsync(
        AlertRuleDocument rule,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var queuedCount = await store.CountActiveRunsAsync(cancellationToken);

        if (queuedCount >= rule.Threshold)
        {
            return (true, $"{queuedCount} active runs in queue (threshold: {rule.Threshold})");
        }

        return (false, string.Empty);
    }

    private async Task<(bool triggered, string message)> CheckRepeatedPrFailuresAsync(
        AlertRuleDocument rule,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var failedRuns = await store.ListRunsByStateAsync(RunState.Failed, cancellationToken);
        var windowStart = DateTime.UtcNow.AddMinutes(-rule.WindowMinutes);

        var recentFailuresWithPr = failedRuns
            .Where(r => r.EndedAtUtc >= windowStart && !string.IsNullOrWhiteSpace(r.PrUrl))
            .GroupBy(r => r.RepositoryId)
            .Select(g => new { RepositoryId = g.Key, Count = g.Count() })
            .Where(x => x.Count >= rule.Threshold)
            .ToList();

        if (recentFailuresWithPr.Count > 0)
        {
            var repoSummary = string.Join(", ", recentFailuresWithPr.Select(x => $"{x.RepositoryId}: {x.Count} failures"));
            return (true, $"Repeated PR failures detected in {recentFailuresWithPr.Count} repository(ies): {repoSummary}");
        }

        return (false, string.Empty);
    }

    private async Task<(bool triggered, string message)> CheckRouteLeakDetectionAsync(
        AlertRuleDocument rule,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-rule.WindowMinutes);
        var recentRuns = await store.ListRecentRunsAsync(cancellationToken);

        var suspiciousRuns = recentRuns
            .Where(r => r.CreatedAtUtc >= windowStart &&
                       !string.IsNullOrWhiteSpace(r.OutputJson) &&
                       (r.OutputJson.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                        r.OutputJson.Contains("https://", StringComparison.OrdinalIgnoreCase)))
            .Take(rule.Threshold)
            .ToList();

        if (suspiciousRuns.Count >= rule.Threshold)
        {
            return (true, $"{suspiciousRuns.Count} runs with potential route leaks detected (threshold: {rule.Threshold})");
        }

        return (false, string.Empty);
    }

    private async Task FireAlertAsync(
        AlertRuleDocument rule,
        string message,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var alertEvent = new AlertEventDocument
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            Message = message,
            FiredAtUtc = DateTime.UtcNow,
            Resolved = false
        };

        await store.RecordAlertEventAsync(alertEvent, cancellationToken);

        rule.LastFiredAtUtc = DateTime.UtcNow;
        await store.UpdateAlertRuleAsync(rule.Id, rule, cancellationToken);

        if (!string.IsNullOrWhiteSpace(rule.WebhookUrl))
        {
            await SendWebhookAsync(rule, alertEvent, cancellationToken);
        }
    }

    private async Task SendWebhookAsync(
        AlertRuleDocument rule,
        AlertEventDocument alertEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                alert_id = alertEvent.Id,
                rule_id = rule.Id,
                rule_name = rule.Name,
                rule_type = rule.RuleType.ToString(),
                message = alertEvent.Message,
                fired_at = alertEvent.FiredAtUtc.ToString("O"),
                threshold = rule.Threshold,
                window_minutes = rule.WindowMinutes
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var response = await httpClient.PostAsync(rule.WebhookUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.ZLogInformation(
                    "Alert webhook fired successfully for rule {RuleName} to {WebhookUrl}",
                    rule.Name,
                    rule.WebhookUrl);
            }
            else
            {
                logger.ZLogWarning(
                    "Alert webhook failed for rule {RuleName} to {WebhookUrl}: {StatusCode}",
                    rule.Name,
                    rule.WebhookUrl,
                    response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.ZLogError(
                ex,
                "Error sending alert webhook for rule {RuleName} to {WebhookUrl}",
                rule.Name,
                rule.WebhookUrl);
        }
    }
}
