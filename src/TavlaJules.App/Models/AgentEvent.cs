namespace TavlaJules.App.Models;

public sealed class AgentEvent
{
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string EventType { get; init; } = "";
    public string Severity { get; init; } = "info";
    public string Message { get; init; } = "";
    public string MetadataJson { get; init; } = "{}";
}
