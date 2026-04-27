using System.Reflection;
using TavlaJules.App.Models;
using TavlaJules.App.Services;
using Xunit;

namespace TavlaJules.Engine.Tests.Services;

public class TavlaAgentServiceTests
{
    [Fact]
    public void BuildPromptObjectiveKey_ClassifiesOnlineSchemaBeforePersistenceSmoke()
    {
        var prompt = """
        Create the TavlaJules online match schema phase for repo kaanx2311336/julestavla.

        Goal:
        Local play and persistence smoke checks are in place. Prepare the next online layer with small MySQL tables for match sessions and players.

        Requirements:
        - Add idempotent MySQL DDL for `online_matches` and `online_match_players`.
        """;

        var objectiveKey = InvokeBuildPromptObjectiveKey(prompt);

        Assert.Equal("online.match-schema", objectiveKey);
    }

    [Fact]
    public void BuildPromptObjectiveKey_StillClassifiesPersistenceSmokeAction()
    {
        var prompt = """
        Create the TavlaJules app persistence smoke-action phase.
        Add a visible Save Game / Load Last Snapshot action through GamePersistenceService.
        """;

        var objectiveKey = InvokeBuildPromptObjectiveKey(prompt);

        Assert.Equal("app.persistence-smoke-action", objectiveKey);
    }

    [Fact]
    public void BuildPromptObjectiveKey_ClassifiesVagueGameEngineSignatureAsTurnDice()
    {
        var prompt = "Please provide the exact GameEngine method signature you need added.";

        var objectiveKey = InvokeBuildPromptObjectiveKey(prompt);

        Assert.Equal("engine.turn-dice", objectiveKey);
    }

    [Fact]
    public void CanAutoSaveDirtyWorkspace_AllowsAgentOwnedFiles()
    {
        var status = new CommandResult
        {
            ExitCode = 0,
            Output = """
             M prodetayi/tavla_agent_service_cs.txt
             M src/TavlaJules.App/Services/TavlaAgentService.cs
            ?? src/TavlaJules.Engine.Tests/Services/TavlaAgentServiceTests.cs
            ?? yapilanlar/2026_04_27_tavlajules_otonom_roadmap_kilidi_acildi.txt
            """
        };

        var canAutoSave = InvokeCanAutoSaveDirtyWorkspace(status, out var dirtyFiles, out var unsafeFiles);

        Assert.True(canAutoSave);
        Assert.Contains("src/TavlaJules.App/Services/TavlaAgentService.cs", dirtyFiles);
        Assert.Empty(unsafeFiles);
    }

    [Fact]
    public void CanAutoSaveDirtyWorkspace_RejectsSecretOrRuntimeFiles()
    {
        var status = new CommandResult
        {
            ExitCode = 0,
            Output = """
             M .env
            ?? migrations/002_online_match_schema.sql
            ?? agent_reports/2026_04_27_12_01_00_agent_report.json
            """
        };

        var canAutoSave = InvokeCanAutoSaveDirtyWorkspace(status, out _, out var unsafeFiles);

        Assert.False(canAutoSave);
        Assert.Contains(".env", unsafeFiles);
        Assert.Contains("agent_reports/2026_04_27_12_01_00_agent_report.json", unsafeFiles);
    }

    [Fact]
    public void TryFindDuplicateCompletedSession_IgnoresSharedTitleWhenObjectiveDiffers()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"tavlajules-agent-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempFolder, "agent_state"));
            File.WriteAllText(
                Path.Combine(tempFolder, "agent_state", "tavlajules.json"),
                """
                {
                  "HandledCompletedSessionIds": ["2920587393206689478"],
                  "AppliedCompletedSessionIds": ["2920587393206689478"],
                  "SessionObjectiveKeys": {
                    "8132549434037104748": "online.match-repository",
                    "2920587393206689478": "online.match-schema"
                  }
                }
                """);

            var settings = new ProjectSettings
            {
                AgentName = "tavlajules",
                ProjectFolder = tempFolder,
                GitHubRepo = "kaanx2311336/julestavla"
            };
            var sessionsOutput = """
             8132549434037104748     Create the TavlaJules online match phase repository/service work  kaanx2311336/julestavla  Completed
             2920587393206689478     Create the TavlaJules online match phase schema work              kaanx2311336/julestavla  Completed
            """;

            var isDuplicate = InvokeTryFindDuplicateCompletedSession(
                settings,
                sessionsOutput,
                "8132549434037104748",
                out var duplicateOfSessionId);

            Assert.False(isDuplicate);
            Assert.Equal("", duplicateOfSessionId);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessTrackedCompletedSession_TreatsAwaitingInputImplementedObjectiveAsNoOpCompleted()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"tavlajules-agent-test-{Guid.NewGuid():N}");
        var sessionId = "13307009469547887061";
        try
        {
            Directory.CreateDirectory(Path.Combine(tempFolder, "src", "TavlaJules.Engine", "Engine"));
            Directory.CreateDirectory(Path.Combine(tempFolder, "src", "TavlaJules.Engine", "Models"));
            await File.WriteAllTextAsync(
                Path.Combine(tempFolder, "src", "TavlaJules.Engine", "Engine", "GameEngine.cs"),
                """
                namespace TavlaJules.Engine.Engine;
                public sealed class GameEngine
                {
                    public object RemainingDice { get; } = new();
                    public void StartTurn() { }
                    public void AdvanceTurn() { }
                    public void RollDice() { }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(tempFolder, "src", "TavlaJules.Engine", "Models", "GameStateSnapshot.cs"),
                "namespace TavlaJules.Engine.Models; public sealed record GameStateSnapshot();");

            var settings = new ProjectSettings
            {
                AgentName = "tavlajules",
                ProjectFolder = tempFolder,
                GitHubRepo = "kaanx2311336/julestavla",
                TrackedJulesSessionId = sessionId
            };
            var sessionsOutput = $"""
             {sessionId}    Please provide the exact GameEngine method signature you need added  kaanx2311336/julestavla  2h ago
            """;
            var events = new List<AgentEvent>();

            var automation = await InvokeProcessTrackedCompletedSessionAsync(
                settings,
                sessionId,
                sessionsOutput,
                events);

            Assert.True(automation.TrackedSessionCompleted);
            Assert.True(automation.AlreadyApplied);
            Assert.False(automation.TrackedSessionAwaitingInput);
            Assert.Contains(events, item => item.EventType == "awaiting_session_objective_already_implemented");
            Assert.True(new AgentStateService().HasHandledCompletedSession(settings, sessionId));
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    [Fact]
    public void IsDuplicatePromptTarget_ForgetsStaleObjectiveWithoutSessionId()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"tavlajules-agent-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempFolder, "agent_state"));
            var settings = new ProjectSettings
            {
                AgentName = "tavlajules",
                ProjectFolder = tempFolder,
                GitHubRepo = "kaanx2311336/julestavla"
            };
            var prompt = "Create the TavlaJules board polish phase for repo kaanx2311336/julestavla.";
            var stateService = new AgentStateService();
            stateService.MarkPromptSent(settings, prompt, "", "app.board-polish");

            var duplicate = InvokeIsDuplicatePromptTarget(
                settings,
                sessionsOutput: "",
                prompt,
                objectiveKey: "app.board-polish",
                out var events);

            Assert.False(duplicate);
            Assert.False(stateService.HasSentPromptObjective(settings, "app.board-polish"));
            Assert.Contains(events, item => item.EventType == "stale_prompt_objective_unlocked");
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    [Fact]
    public void IsDuplicatePromptTarget_KeepsActiveSessionObjective()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"tavlajules-agent-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempFolder, "agent_state"));
            var settings = new ProjectSettings
            {
                AgentName = "tavlajules",
                ProjectFolder = tempFolder,
                GitHubRepo = "kaanx2311336/julestavla"
            };
            var prompt = "Create the TavlaJules board polish phase for repo kaanx2311336/julestavla.";
            var stateService = new AgentStateService();
            stateService.MarkPromptSent(settings, prompt, "12345678901", "app.board-polish");
            var sessionsOutput = """
             12345678901    Create the TavlaJules board polish phase for repo kaanx2311336/julestavla  kaanx2311336/julestavla  Planning
            """;

            var duplicate = InvokeIsDuplicatePromptTarget(
                settings,
                sessionsOutput,
                prompt,
                objectiveKey: "app.board-polish",
                out _);

            Assert.True(duplicate);
            Assert.True(stateService.HasSentPromptObjective(settings, "app.board-polish"));
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    private static string InvokeBuildPromptObjectiveKey(string prompt)
    {
        var method = typeof(TavlaAgentService).GetMethod(
            "BuildPromptObjectiveKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [prompt]));
    }

    private static bool InvokeCanAutoSaveDirtyWorkspace(
        CommandResult status,
        out IReadOnlyList<string> dirtyFiles,
        out IReadOnlyList<string> unsafeFiles)
    {
        var method = typeof(TavlaAgentService).GetMethod(
            "CanAutoSaveDirtyWorkspace",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        object?[] parameters = [status, null, null];
        var result = Assert.IsType<bool>(method.Invoke(null, parameters));
        dirtyFiles = Assert.IsAssignableFrom<IReadOnlyList<string>>(parameters[1]);
        unsafeFiles = Assert.IsAssignableFrom<IReadOnlyList<string>>(parameters[2]);
        return result;
    }

    private static bool InvokeTryFindDuplicateCompletedSession(
        ProjectSettings settings,
        string sessionsOutput,
        string trackedSessionId,
        out string duplicateOfSessionId)
    {
        var method = typeof(TavlaAgentService).GetMethod(
            "TryFindDuplicateCompletedSession",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        object?[] parameters = [settings, sessionsOutput, trackedSessionId, null];
        var result = Assert.IsType<bool>(method.Invoke(new TavlaAgentService(), parameters));
        duplicateOfSessionId = Assert.IsType<string>(parameters[3]);
        return result;
    }

    private static async Task<AgentAutomationArtifacts> InvokeProcessTrackedCompletedSessionAsync(
        ProjectSettings settings,
        string trackedSessionId,
        string sessionsOutput,
        List<AgentEvent> events)
    {
        var method = typeof(TavlaAgentService).GetMethod(
            "ProcessTrackedCompletedSessionAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<AgentAutomationArtifacts>>(method.Invoke(
            new TavlaAgentService(),
            [settings, trackedSessionId, sessionsOutput, events, CancellationToken.None]));
        return await task;
    }

    private static bool InvokeIsDuplicatePromptTarget(
        ProjectSettings settings,
        string sessionsOutput,
        string prompt,
        string objectiveKey,
        out List<AgentEvent> events)
    {
        var method = typeof(TavlaAgentService).GetMethod(
            "IsDuplicatePromptTarget",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        events = [];
        object?[] parameters = [settings, sessionsOutput, prompt, objectiveKey, events];
        return Assert.IsType<bool>(method.Invoke(new TavlaAgentService(), parameters));
    }
}
