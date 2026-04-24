using System.Text.Json;
using System.Text.RegularExpressions;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class AgentStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool HasHandledCompletedSession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.HandledCompletedSessionIds.Contains(sessionId, StringComparer.Ordinal);
    }

    public bool HasAppliedCompletedSession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.AppliedCompletedSessionIds.Contains(sessionId, StringComparer.Ordinal);
    }

    public void MarkCompletedSessionHandled(ProjectSettings settings, string sessionId, string newSessionId)
    {
        var state = Load(settings);

        if (!state.HandledCompletedSessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            state.HandledCompletedSessionIds.Add(sessionId);
        }

        state.LastTrackedSessionId = newSessionId;
        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public void MarkCompletedSessionApplied(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);

        if (!state.AppliedCompletedSessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            state.AppliedCompletedSessionIds.Add(sessionId);
        }

        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public static string ParseSessionId(CommandResult result)
    {
        var text = $"{result.Output}{Environment.NewLine}{result.Error}";
        var match = Regex.Match(text, @"ID:\s*(?<id>\d{10,})");
        return match.Success ? match.Groups["id"].Value : "";
    }

    private AgentState Load(ProjectSettings settings)
    {
        var path = GetPath(settings);
        if (!File.Exists(path))
        {
            return new AgentState();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AgentState>(json, JsonOptions) ?? new AgentState();
        }
        catch
        {
            return new AgentState();
        }
    }

    private void Save(ProjectSettings settings, AgentState state)
    {
        var path = GetPath(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static string GetPath(ProjectSettings settings)
    {
        return Path.Combine(settings.ProjectFolder, "agent_state", $"{settings.AgentName}.json");
    }

    private sealed class AgentState
    {
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public string LastTrackedSessionId { get; set; } = "";
        public List<string> HandledCompletedSessionIds { get; set; } = [];
        public List<string> AppliedCompletedSessionIds { get; set; } = [];
    }
}
