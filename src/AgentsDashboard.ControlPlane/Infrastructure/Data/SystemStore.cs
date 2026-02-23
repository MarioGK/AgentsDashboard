namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class SystemStore(
    IOrchestratorRepositorySessionFactory liteDbScopeFactory,
    LiteDbExecutor liteDbExecutor,
    LiteDbDatabase liteDbDatabase) : ISystemStore, IAsyncDisposable
{

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await MigrateTaskKindFieldsAsync(cancellationToken);
    }

    public async Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        return settings ?? new SystemSettingsDocument();
    }

    public async Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        settings.Id = "singleton";
        settings.UpdatedAtUtc = DateTime.UtcNow;

        var existing = await db.Settings.FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        if (existing is null)
        {
            db.Settings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
            return settings;
        }

        db.Entry(existing).CurrentValues.SetValues(settings);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<List<McpRegistryServerDocument>> ListMcpRegistryServersAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.McpRegistryServers.AsNoTracking()
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceMcpRegistryServersAsync(List<McpRegistryServerDocument> servers, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);

        var existing = await db.McpRegistryServers.ToListAsync(cancellationToken);
        foreach (var entry in existing)
        {
            db.McpRegistryServers.Remove(entry);
        }

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Id))
            {
                server.Id = Guid.NewGuid().ToString("N");
            }

            server.UpdatedAtUtc = DateTime.UtcNow;
            db.McpRegistryServers.Add(server);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<McpRegistryStateDocument> GetMcpRegistryStateAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var state = await db.McpRegistryState.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        return state ?? new McpRegistryStateDocument();
    }

    public async Task<McpRegistryStateDocument> UpsertMcpRegistryStateAsync(McpRegistryStateDocument state, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        state.Id = "singleton";
        state.UpdatedAtUtc = DateTime.UtcNow;

        var existing = await db.McpRegistryState.FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        if (existing is null)
        {
            db.McpRegistryState.Add(state);
            await db.SaveChangesAsync(cancellationToken);
            return state;
        }

        db.Entry(existing).CurrentValues.SetValues(state);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> TryAcquireLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(ttl);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(x => x.LeaseName == leaseName, cancellationToken);
        if (lease is null)
        {
            db.Leases.Add(new OrchestratorLeaseDocument
            {
                LeaseName = leaseName,
                OwnerId = ownerId,
                ExpiresAtUtc = expiresAtUtc,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (string.Equals(lease.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase) || lease.ExpiresAtUtc <= now)
        {
            lease.OwnerId = ownerId;
            lease.ExpiresAtUtc = expiresAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        await transaction.RollbackAsync(cancellationToken);
        return false;
    }

    public async Task<bool> RenewLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(
            x => x.LeaseName == leaseName && x.OwnerId == ownerId,
            cancellationToken);

        if (lease is null)
        {
            return false;
        }

        lease.ExpiresAtUtc = DateTime.UtcNow.Add(ttl);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReleaseLeaseAsync(string leaseName, string ownerId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(
            x => x.LeaseName == leaseName && x.OwnerId == ownerId,
            cancellationToken);

        if (lease is null)
        {
            return;
        }

        db.Leases.Remove(lease);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
    }

    public async Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
    }

    public async Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var existing = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (existing is null)
            return null;

        rule.Id = existing.Id;
        db.Entry(existing).CurrentValues.SetValues(rule);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var rule = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;

        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        db.AlertEvents.Add(alertEvent);
        await db.SaveChangesAsync(cancellationToken);
        return alertEvent;
    }

    public async Task<AlertEventDocument?> GetAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().OrderByDescending(x => x.FiredAtUtc).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().Where(x => x.RuleId == ruleId).OrderByDescending(x => x.FiredAtUtc).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var alertEvent = await db.AlertEvents.FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (alertEvent is null)
            return null;

        alertEvent.Resolved = true;
        await db.SaveChangesAsync(cancellationToken);
        return alertEvent;
    }

    public async Task<int> ResolveAlertEventsAsync(List<string> eventIds, CancellationToken cancellationToken)

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private async Task MigrateTaskKindFieldsAsync(CancellationToken cancellationToken)
    {
        await liteDbExecutor.ExecuteAsync(
            db =>
            {
                RemoveTaskKindFields(db.GetCollection("tasks"), "Kind", "kind");
                RemoveNestedTaskDefaultsKindFields(db.GetCollection("repositories"), "TaskDefaults", "Kind", "kind");
            },
            cancellationToken);
    }

    private static void RemoveTaskKindFields(LiteDB.ILiteCollection<LiteDB.BsonDocument> collection, params string[] fieldNames)
    {
        foreach (var document in collection.FindAll())
        {
            var changed = false;
            foreach (var fieldName in fieldNames)
            {
                if (document.Remove(fieldName))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                collection.Update(document);
            }
        }
    }

    private static void RemoveNestedTaskDefaultsKindFields(
        LiteDB.ILiteCollection<LiteDB.BsonDocument> collection,
        string nestedDocumentField,
        params string[] fieldNames)
    {
        foreach (var document in collection.FindAll())
        {
            if (!document.TryGetValue(nestedDocumentField, out var nestedValue) || !nestedValue.IsDocument)
            {
                continue;
            }

            var nestedDocument = nestedValue.AsDocument;
            var changed = false;

            foreach (var fieldName in fieldNames)
            {
                if (nestedDocument.Remove(fieldName))
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                continue;
            }

}
