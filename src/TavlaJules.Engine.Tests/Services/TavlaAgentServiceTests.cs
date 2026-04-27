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
}
