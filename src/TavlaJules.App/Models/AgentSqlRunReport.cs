namespace TavlaJules.App.Models;

public sealed class AgentSqlRunReport
{
    public Guid RunUuid { get; init; } = Guid.NewGuid();
    public string Status { get; init; } = "completed";
    public string TriggerSource { get; init; } = "timer";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public long DurationMs { get; init; }
    public string Model { get; init; } = "";
    public string TrackedJulesSessionId { get; init; } = "";
    public string GitHubRepo { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string StatusSummary { get; init; } = "";
    public string WhatJulesDid { get; init; } = "";
    public string NextPrompt { get; init; } = "";
    public bool ShouldStartNewJulesSession { get; init; }
    public string DatabasePlan { get; init; } = "";
    public string RiskNotesJson { get; init; } = "[]";
    public string AnalysisJson { get; init; } = "{}";
    public string ErrorText { get; init; } = "";
    public string JulesSessionsRaw { get; init; } = "";
}
