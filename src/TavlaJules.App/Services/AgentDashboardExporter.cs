using System.Text.Json;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class AgentDashboardExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Export(
        ProjectSettings settings,
        OpenRouterCompletionResult completion,
        string reportPath,
        string sqlReportMessage,
        string julesSessionsRaw,
        string? pullOutput,
        IReadOnlyList<AgentEvent> events)
    {
        var dashboardFolder = Path.Combine(settings.ProjectFolder, "ajanizleme", "data");
        Directory.CreateDirectory(dashboardFolder);
        var dashboardPath = Path.Combine(dashboardFolder, "dashboard.json");
        var dashboardScriptPath = Path.Combine(dashboardFolder, "dashboard-data.js");

        var snapshot = LoadSnapshot(dashboardPath, settings.AgentName);
        var entry = CreateEntry(settings, completion, reportPath, sqlReportMessage, julesSessionsRaw, pullOutput, events);
        snapshot.Runs.Insert(0, entry);

        if (snapshot.Runs.Count > 50)
        {
            snapshot.Runs.RemoveRange(50, snapshot.Runs.Count - 50);
        }

        snapshot.UpdatedAt = DateTimeOffset.Now;
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(dashboardPath, json);
        File.WriteAllText(dashboardScriptPath, "window.AGENT_DASHBOARD = " + json + ";" + Environment.NewLine);
        return dashboardPath;
    }

    private static DashboardSnapshot LoadSnapshot(string path, string agentName)
    {
        if (!File.Exists(path))
        {
            return new DashboardSnapshot { AgentName = agentName };
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DashboardSnapshot>(json, JsonOptions) ?? new DashboardSnapshot { AgentName = agentName };
        }
        catch
        {
            return new DashboardSnapshot { AgentName = agentName };
        }
    }

    private static DashboardRunEntry CreateEntry(
        ProjectSettings settings,
        OpenRouterCompletionResult completion,
        string reportPath,
        string sqlReportMessage,
        string julesSessionsRaw,
        string? pullOutput,
        IReadOnlyList<AgentEvent> events)
    {
        var analysis = ParseAnalysis(completion.Content);
        return new DashboardRunEntry
        {
            CreatedAt = DateTimeOffset.Now,
            AgentName = settings.AgentName,
            GitHubRepo = settings.GitHubRepo,
            TrackedJulesSessionId = settings.TrackedJulesSessionId,
            Model = completion.Model,
            Status = completion.Model.Equals("local-degraded", StringComparison.OrdinalIgnoreCase) ? "degraded" : "completed",
            StatusSummary = analysis.StatusSummary,
            WhatJulesDid = analysis.WhatJulesDid,
            NextPrompt = analysis.NextPrompt,
            ShouldStartNewJulesSession = analysis.ShouldStartNewJulesSession,
            DatabasePlan = analysis.DatabasePlan,
            RiskNotes = analysis.RiskNotes,
            ReportPath = reportPath,
            SqlReportMessage = sqlReportMessage,
            JulesSessionsRaw = julesSessionsRaw,
            PullOutput = pullOutput,
            Events = events
                .Select(agentEvent => new DashboardEvent
                {
                    CreatedAt = agentEvent.CreatedAt,
                    EventType = agentEvent.EventType,
                    Severity = agentEvent.Severity,
                    Message = agentEvent.Message,
                    MetadataJson = agentEvent.MetadataJson
                })
                .ToList()
        };
    }

    private static DashboardAnalysis ParseAnalysis(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return new DashboardAnalysis
            {
                StatusSummary = GetString(root, "statusSummary"),
                WhatJulesDid = GetString(root, "whatJulesDid"),
                NextPrompt = GetString(root, "nextPrompt"),
                ShouldStartNewJulesSession = root.TryGetProperty("shouldStartNewJulesSession", out var start) && start.ValueKind == JsonValueKind.True,
                DatabasePlan = GetString(root, "databasePlan"),
                RiskNotes = root.TryGetProperty("riskNotes", out var risks) && risks.ValueKind == JsonValueKind.Array
                    ? risks.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList()
                    : []
            };
        }
        catch
        {
            return new DashboardAnalysis
            {
                StatusSummary = content,
                RiskNotes = ["Model cevabi JSON olarak parse edilemedi."]
            };
        }
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
    }

    private sealed class DashboardSnapshot
    {
        public string AgentName { get; set; } = "tavlajules";
        public DateTimeOffset? UpdatedAt { get; set; }
        public List<DashboardRunEntry> Runs { get; set; } = [];
    }

    private sealed class DashboardRunEntry
    {
        public DateTimeOffset CreatedAt { get; set; }
        public string AgentName { get; set; } = "";
        public string GitHubRepo { get; set; } = "";
        public string TrackedJulesSessionId { get; set; } = "";
        public string Model { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusSummary { get; set; } = "";
        public string WhatJulesDid { get; set; } = "";
        public string NextPrompt { get; set; } = "";
        public bool ShouldStartNewJulesSession { get; set; }
        public string DatabasePlan { get; set; } = "";
        public List<string> RiskNotes { get; set; } = [];
        public string ReportPath { get; set; } = "";
        public string SqlReportMessage { get; set; } = "";
        public string JulesSessionsRaw { get; set; } = "";
        public string? PullOutput { get; set; }
        public List<DashboardEvent> Events { get; set; } = [];
    }

    private sealed class DashboardEvent
    {
        public DateTimeOffset CreatedAt { get; set; }
        public string EventType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string MetadataJson { get; set; } = "{}";
    }

    private sealed class DashboardAnalysis
    {
        public string StatusSummary { get; set; } = "";
        public string WhatJulesDid { get; set; } = "";
        public string NextPrompt { get; set; } = "";
        public bool ShouldStartNewJulesSession { get; set; }
        public string DatabasePlan { get; set; } = "";
        public List<string> RiskNotes { get; set; } = [];
    }
}
