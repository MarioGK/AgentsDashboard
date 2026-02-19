using System.Text.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;


internal sealed record DecodedRunStructuredEvent(
    string EventType,
    string Category,
    string PayloadJson,
    string Schema,
    string Summary,
    string Error,
    DateTime TimestampUtc,
    long Sequence);
