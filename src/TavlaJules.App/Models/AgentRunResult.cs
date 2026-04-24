namespace TavlaJules.App.Models;

public sealed class AgentRunResult
{
    public string UsedModel { get; init; } = "";
    public string Analysis { get; init; } = "";
    public string NextPrompt { get; init; } = "";
    public bool ShouldStartNewJulesSession { get; init; }
    public string ReportPath { get; init; } = "";
    public string JulesSessionsRaw { get; init; } = "";
    public string? PullOutput { get; init; }
    public string SqlReportMessage { get; init; } = "";
    public string NewJulesSessionId { get; init; } = "";
    public CommandResult? AutoJulesSessionResult { get; init; }
}
