using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
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

    public IReadOnlyList<string> GetHandledCompletedSessionIds(ProjectSettings settings)
    {
        return Load(settings).HandledCompletedSessionIds;
    }

    public IReadOnlyList<string> GetAppliedCompletedSessionIds(ProjectSettings settings)
    {
        return Load(settings).AppliedCompletedSessionIds;
    }

    public bool HasHandledAwaitingInputSession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.HandledAwaitingInputSessionIds.Contains(sessionId, StringComparer.Ordinal);
    }

    public bool HasAwaitingInputRecoverySession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.AwaitingInputRecoverySessionIds.Contains(sessionId, StringComparer.Ordinal);
    }

    public bool HasRepliedAwaitingInputSession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.RepliedAwaitingInputSessionIds.Contains(sessionId, StringComparer.Ordinal);
    }

    public bool HasApprovedAwaitingPlanSession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.ApprovedAwaitingPlanSessionIds.Contains(sessionId, StringComparer.Ordinal);
    }

    public bool ShouldRetryAwaitingPlanApproval(ProjectSettings settings, string sessionId, TimeSpan retryAfter, int maxAttempts)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var state = Load(settings);
        var attempts = state.AwaitingPlanApprovalAttemptCounts.TryGetValue(sessionId, out var count) ? count : 0;
        if (attempts >= maxAttempts)
        {
            return false;
        }

        if (!state.AwaitingPlanApprovalSentAt.TryGetValue(sessionId, out var lastSentAt))
        {
            return true;
        }

        return DateTimeOffset.Now - lastSentAt >= retryAfter;
    }

    public int GetAwaitingPlanApprovalAttemptCount(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);
        return state.AwaitingPlanApprovalAttemptCounts.TryGetValue(sessionId, out var count) ? count : 0;
    }

    public string GetPendingAppliedSessionId(ProjectSettings settings)
    {
        return Load(settings).PendingAppliedSessionId;
    }

    public string GetLastPromptSessionId(ProjectSettings settings)
    {
        return Load(settings).LastPromptSessionId;
    }

    public bool HasSentPrompt(ProjectSettings settings, string prompt)
    {
        var state = Load(settings);
        return state.SentPromptHashes.Contains(HashPrompt(prompt), StringComparer.Ordinal);
    }

    public bool HasSentPromptObjective(ProjectSettings settings, string objectiveKey)
    {
        if (string.IsNullOrWhiteSpace(objectiveKey))
        {
            return false;
        }

        var state = Load(settings);
        return state.SentPromptObjectiveKeys.Contains(objectiveKey, StringComparer.Ordinal);
    }

    public void MarkPromptSent(ProjectSettings settings, string prompt, string sessionId, string objectiveKey = "")
    {
        var state = Load(settings);
        var hash = HashPrompt(prompt);

        if (!state.SentPromptHashes.Contains(hash, StringComparer.Ordinal))
        {
            state.SentPromptHashes.Add(hash);
        }

        if (!string.IsNullOrWhiteSpace(objectiveKey)
            && !state.SentPromptObjectiveKeys.Contains(objectiveKey, StringComparer.Ordinal))
        {
            state.SentPromptObjectiveKeys.Add(objectiveKey);
        }

        state.LastPromptSessionId = sessionId;
        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public void MarkAwaitingInputSessionHandled(ProjectSettings settings, string sessionId, string newSessionId)
    {
        var state = Load(settings);

        if (!state.HandledAwaitingInputSessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            state.HandledAwaitingInputSessionIds.Add(sessionId);
        }

        if (!string.IsNullOrWhiteSpace(newSessionId))
        {
            state.LastTrackedSessionId = newSessionId;
        }

        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public void MarkAwaitingInputRecoverySession(ProjectSettings settings, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var state = Load(settings);

        if (!state.AwaitingInputRecoverySessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            state.AwaitingInputRecoverySessionIds.Add(sessionId);
        }

        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public void MarkAwaitingInputSessionReplied(ProjectSettings settings, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var state = Load(settings);

        if (!state.RepliedAwaitingInputSessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            state.RepliedAwaitingInputSessionIds.Add(sessionId);
        }

        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public void MarkAwaitingPlanSessionApproved(ProjectSettings settings, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var state = Load(settings);

        if (!state.ApprovedAwaitingPlanSessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            state.ApprovedAwaitingPlanSessionIds.Add(sessionId);
        }

        state.AwaitingPlanApprovalSentAt[sessionId] = DateTimeOffset.Now;
        state.AwaitingPlanApprovalAttemptCounts[sessionId] =
            state.AwaitingPlanApprovalAttemptCounts.TryGetValue(sessionId, out var count) ? count + 1 : 1;
        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
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

        state.PendingAppliedSessionId = sessionId;
        state.UpdatedAt = DateTimeOffset.Now;
        Save(settings, state);
    }

    public void ClearPendingAppliedSession(ProjectSettings settings, string sessionId)
    {
        var state = Load(settings);

        if (state.PendingAppliedSessionId.Equals(sessionId, StringComparison.Ordinal))
        {
            state.PendingAppliedSessionId = "";
            state.UpdatedAt = DateTimeOffset.Now;
            Save(settings, state);
        }
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

    private static string HashPrompt(string prompt)
    {
        var normalized = Regex.Replace(prompt.Trim().ToLowerInvariant(), @"\s+", " ");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    private sealed class AgentState
    {
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public string LastTrackedSessionId { get; set; } = "";
        public string LastPromptSessionId { get; set; } = "";
        public string PendingAppliedSessionId { get; set; } = "";
        public List<string> HandledCompletedSessionIds { get; set; } = [];
        public List<string> AppliedCompletedSessionIds { get; set; } = [];
        public List<string> HandledAwaitingInputSessionIds { get; set; } = [];
        public List<string> AwaitingInputRecoverySessionIds { get; set; } = [];
        public List<string> RepliedAwaitingInputSessionIds { get; set; } = [];
        public List<string> ApprovedAwaitingPlanSessionIds { get; set; } = [];
        public Dictionary<string, DateTimeOffset> AwaitingPlanApprovalSentAt { get; set; } = [];
        public Dictionary<string, int> AwaitingPlanApprovalAttemptCounts { get; set; } = [];
        public List<string> SentPromptHashes { get; set; } = [];
        public List<string> SentPromptObjectiveKeys { get; set; } = [];
    }
}
