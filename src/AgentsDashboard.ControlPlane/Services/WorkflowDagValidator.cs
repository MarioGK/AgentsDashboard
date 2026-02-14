using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed record DagValidationResult(bool IsValid, List<string> Errors);

public static class WorkflowDagValidator
{
    public static async Task<DagValidationResult> ValidateAsync(
        WorkflowV2Document workflow,
        IOrchestratorStore store,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var nodes = workflow.Nodes;
        var edges = workflow.Edges;

        var startNodes = nodes.Where(n => n.Type == WorkflowNodeType.Start).ToList();
        if (startNodes.Count == 0)
            errors.Add("Workflow must have exactly one Start node.");
        else if (startNodes.Count > 1)
            errors.Add($"Workflow has {startNodes.Count} Start nodes; exactly one is required.");

        var endNodes = nodes.Where(n => n.Type == WorkflowNodeType.End).ToList();
        if (endNodes.Count == 0)
            errors.Add("Workflow must have at least one End node.");

        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id));
        if (nodeIds.Count != nodes.Count)
            errors.Add("Node IDs must be unique.");

        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.SourceNodeId))
                errors.Add($"Edge '{edge.Id}' references unknown source node '{edge.SourceNodeId}'.");
            if (!nodeIds.Contains(edge.TargetNodeId))
                errors.Add($"Edge '{edge.Id}' references unknown target node '{edge.TargetNodeId}'.");
        }

        foreach (var node in startNodes)
        {
            if (edges.Any(e => e.TargetNodeId == node.Id))
                errors.Add($"Start node '{node.Name}' must not have incoming edges.");
        }

        foreach (var node in endNodes)
        {
            if (edges.Any(e => e.SourceNodeId == node.Id))
                errors.Add($"End node '{node.Name}' must not have outgoing edges.");
        }

        var priorityGroups = edges.GroupBy(e => e.SourceNodeId);
        foreach (var group in priorityGroups)
        {
            var priorities = group.Select(e => e.Priority).ToList();
            if (priorities.Count != priorities.Distinct().Count())
                errors.Add($"Edges from node '{group.Key}' have duplicate priorities.");
        }

        if (HasCycle(nodes, edges))
            errors.Add("Workflow graph contains a cycle.");

        if (startNodes.Count == 1)
        {
            var reachable = GetReachableNodes(startNodes[0].Id, edges);
            var unreachable = nodeIds.Except(reachable).ToList();
            if (unreachable.Count > 0)
            {
                var names = nodes.Where(n => unreachable.Contains(n.Id)).Select(n => n.Name);
                errors.Add($"Nodes not reachable from Start: {string.Join(", ", names)}.");
            }
        }

        foreach (var node in nodes.Where(n => n.Type == WorkflowNodeType.Agent))
        {
            if (string.IsNullOrEmpty(node.AgentId))
            {
                errors.Add($"Agent node '{node.Name}' must reference an AgentId.");
                continue;
            }

            var agent = await store.GetAgentAsync(node.AgentId, cancellationToken);
            if (agent is null)
                errors.Add($"Agent node '{node.Name}' references non-existent agent '{node.AgentId}'.");
        }

        return new DagValidationResult(errors.Count == 0, errors);
    }

    private static bool HasCycle(List<WorkflowNodeConfig> nodes, List<WorkflowEdgeConfig> edges)
    {
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var node in nodes)
        {
            inDegree[node.Id] = 0;
            adjacency[node.Id] = [];
        }

        foreach (var edge in edges)
        {
            if (adjacency.ContainsKey(edge.SourceNodeId) && inDegree.ContainsKey(edge.TargetNodeId))
            {
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
                inDegree[edge.TargetNodeId]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        return visited != nodes.Count;
    }

    private static HashSet<string> GetReachableNodes(string startId, List<WorkflowEdgeConfig> edges)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var edge in edges)
        {
            if (!adjacency.ContainsKey(edge.SourceNodeId))
                adjacency[edge.SourceNodeId] = [];
            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
        }

        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(startId);
        visited.Add(startId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (adjacency.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors.Where(n => visited.Add(n)))
                    queue.Enqueue(neighbor);
            }
        }

        return visited;
    }
}
