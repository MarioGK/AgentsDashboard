using System.Text.Json;
using SystemTextJsonSerializer = System.Text.Json.JsonSerializer;

using AgentsDashboard.TaskRuntime.Infrastructure.Data;
using LiteDB;

namespace AgentsDashboard.TaskRuntime.Features.Events.Services;

public sealed class TaskRuntimeEventOutboxService(
    TaskRuntimeLiteDbStore liteDbStore,
    TaskRuntimeOptions options,
    ILogger<TaskRuntimeEventOutboxService> logger)
{
    private const string CollectionName = "runtime_event_outbox";

    public async Task<JobEventMessage> AppendAsync(JobEventMessage message, CancellationToken cancellationToken)
    {
        var payloadJson = SystemTextJsonSerializer.Serialize(message with { DeliveryId = 0 });
        var stored = new TaskRuntimeOutboxEventDocument
        {
            DeliveryId = 0,
            RunId = message.RunId,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTime.UtcNow
        };

        var deliveryId = await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeOutboxEventDocument>(CollectionName);
                collection.EnsureIndex(x => x.RunId);
                collection.EnsureIndex(x => x.CreatedAtUtc);
                var insertedId = collection.Insert(stored);
                TrimOutboxUnsafe(collection);
                return insertedId.AsInt64;
            },
            cancellationToken);

        return message with { DeliveryId = deliveryId };
    }

    public async Task<ReadEventBacklogResult> ReadBacklogAsync(ReadEventBacklogRequest request, CancellationToken cancellationToken)
    {
        if (request.MaxEvents <= 0)
        {
            return new ReadEventBacklogResult
            {
                Success = false,
                ErrorMessage = "max_events must be greater than zero",
                Events = [],
                LastDeliveryId = request.AfterDeliveryId,
                HasMore = false
            };
        }

        var normalizedMax = Math.Clamp(request.MaxEvents, 1, 5000);

        var readResult = await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeOutboxEventDocument>(CollectionName);
                collection.EnsureIndex(x => x.CreatedAtUtc);

                var items = collection.Query()
                    .Where(x => x.DeliveryId > request.AfterDeliveryId)
                    .OrderBy(x => x.DeliveryId)
                    .Limit(normalizedMax)
                    .ToList();

                var lastDeliveryId = items.Count > 0 ? items[^1].DeliveryId : request.AfterDeliveryId;
                var hasMore = collection.Exists(x => x.DeliveryId > lastDeliveryId);
                return (Items: items, LastDeliveryId: lastDeliveryId, HasMore: hasMore);
            },
            cancellationToken);

        var events = new List<JobEventMessage>(readResult.Items.Count);
        foreach (var item in readResult.Items)
        {
            try
            {
                var parsed = SystemTextJsonSerializer.Deserialize<JobEventMessage>(item.PayloadJson);
                if (parsed is null)
                {
                    continue;
                }

                events.Add(parsed with { DeliveryId = item.DeliveryId });
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to deserialize runtime outbox event {DeliveryId}", item.DeliveryId);
            }
        }

        return new ReadEventBacklogResult
        {
            Success = true,
            ErrorMessage = null,
            Events = events,
            LastDeliveryId = readResult.LastDeliveryId,
            HasMore = readResult.HasMore
        };
    }

    private void TrimOutboxUnsafe(ILiteCollection<TaskRuntimeOutboxEventDocument> collection)
    {
        var maxEntries = Math.Max(1000, options.EventOutboxMaxEntries);
        var total = collection.LongCount();
        if (total <= maxEntries)
        {
            return;
        }

        var removeCount = total - maxEntries;
        if (removeCount <= 0)
        {
            return;
        }

        var toRemove = collection.Query()
            .OrderBy(x => x.DeliveryId)
            .Limit((int)Math.Min(removeCount, int.MaxValue))
            .ToList();
        if (toRemove.Count == 0)
        {
            return;
        }

        foreach (var entry in toRemove)
        {
            collection.Delete(entry.DeliveryId);
        }
    }
}
