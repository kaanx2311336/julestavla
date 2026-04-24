using System.Text.Json;
using System.Text.RegularExpressions;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class TavlaAgentService
{
    private readonly JulesCliService julesCliService = new();
    private readonly OpenRouterClient openRouterClient = new();
    private readonly DatabaseHealthService databaseHealthService = new();

    public async Task<AgentRunResult> RunOnceAsync(
        ProjectSettings settings,
        string apiKey,
        string? databaseConnectionString,
        CancellationToken cancellationToken = default)
    {
        var sessions = await julesCliService.ListSessionsAsync(settings, cancellationToken);
        var pullOutput = await TryPullTrackedSessionAsync(settings, sessions.Output, cancellationToken);
        var databaseHealth = await databaseHealthService.TestAsync(databaseConnectionString, cancellationToken);
        var projectContext = BuildProjectContext(settings, sessions.Output, pullOutput, databaseHealth);

        var completion = await openRouterClient.CompleteAsync(
            settings,
            apiKey,
            BuildSystemPrompt(),
            projectContext,
            maxTokens: 1800,
            cancellationToken);

        var nextPrompt = ExtractString(completion.Content, "nextPrompt");
        var shouldStart = ExtractBool(completion.Content, "shouldStartNewJulesSession");
        CommandResult? autoResult = null;

        if (settings.AllowAutoJulesSessions && shouldStart && !string.IsNullOrWhiteSpace(nextPrompt))
        {
            autoResult = await julesCliService.CreateSessionAsync(settings, nextPrompt, cancellationToken);
        }

        var reportPath = SaveReport(settings, completion, sessions.Output, pullOutput, databaseHealth, autoResult);
        return new AgentRunResult
        {
            UsedModel = completion.Model,
            Analysis = completion.Content,
            NextPrompt = nextPrompt,
            ShouldStartNewJulesSession = shouldStart,
            ReportPath = reportPath,
            JulesSessionsRaw = sessions.Output,
            PullOutput = pullOutput,
            AutoJulesSessionResult = autoResult
        };
    }

    private async Task<string?> TryPullTrackedSessionAsync(ProjectSettings settings, string sessionsOutput, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TrackedJulesSessionId))
        {
            return null;
        }

        if (!sessionsOutput.Contains(settings.TrackedJulesSessionId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!Regex.IsMatch(sessionsOutput, settings.TrackedJulesSessionId + @".*(Completed|Needs\s+review)", RegexOptions.IgnoreCase))
        {
            return null;
        }

        var pull = await julesCliService.PullSessionAsync(settings, settings.TrackedJulesSessionId, apply: false, cancellationToken);
        return string.IsNullOrWhiteSpace(pull.Output) ? pull.Error : pull.Output;
    }

    private static string BuildSystemPrompt()
    {
        return """
        Sen TavlaJules proje orkestrator ajanisin. Birincil modelin openai/gpt-oss-120b:free kabul edilir.
        Gorevin Jules'in son durumunu, yerel proje hafizasini, GitHub hedefini ve tavla_online veritabani durumunu okuyup bir sonraki en dogru adimi tasarlamaktir.
        Proje, kullanicinin daha once yaptigi Batak projesine benzer sekilde fazli, loglu, prodetayi hafizali ve Jules destekli ilerlemelidir.
        Gizli anahtar, connection string veya .env icerigini asla tekrar etme.
        Cevabini sadece gecerli JSON olarak ver:
        {
          "statusSummary": "kisa durum",
          "whatJulesDid": "Jules ne yapti veya hangi asamada",
          "nextPrompt": "Jules'e sonraki turda gonderilecek net prompt",
          "shouldStartNewJulesSession": false,
          "databasePlan": "tavla_online icin sonraki DB adimi",
          "riskNotes": ["risk"]
        }
        """;
    }

    private static string BuildProjectContext(ProjectSettings settings, string sessionsOutput, string? pullOutput, DatabaseHealthResult databaseHealth)
    {
        return $"""
        GENEL HEDEF:
        {settings.Goal}

        GITHUB REPO:
        {settings.GitHubRepo}

        IZLENEN JULES SESSION:
        {settings.TrackedJulesSessionId}

        JULES SESSION LISTESI:
        {Trim(sessionsOutput, 5000)}

        JULES PULL OZETI:
        {Trim(pullOutput ?? "Pull yapilmadi; session henuz tamamlanmis gorunmuyor.", 4000)}

        TAVLA_ONLINE DB DURUMU:
        Configured={databaseHealth.IsConfigured}; Success={databaseHealth.IsSuccess}; Tables={databaseHealth.TableCount}; Message={databaseHealth.Message}

        YEREL HAFIZA OZETLERI:
        {ReadTextFiles(Path.Combine(settings.ProjectFolder, "prodetayi"), 6000)}

        YAPILANLAR LOG OZETI:
        {ReadTextFiles(Path.Combine(settings.ProjectFolder, "yapilanlar"), 5000)}
        """;
    }

    private static string SaveReport(
        ProjectSettings settings,
        OpenRouterCompletionResult completion,
        string sessionsOutput,
        string? pullOutput,
        DatabaseHealthResult databaseHealth,
        CommandResult? autoResult)
    {
        var reportsFolder = Path.Combine(settings.ProjectFolder, "agent_reports");
        Directory.CreateDirectory(reportsFolder);
        var path = Path.Combine(reportsFolder, $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}_agent_report.json");

        var payload = new
        {
            createdAt = DateTimeOffset.Now,
            model = completion.Model,
            trackedJulesSessionId = settings.TrackedJulesSessionId,
            gitHubRepo = settings.GitHubRepo,
            database = new
            {
                databaseHealth.IsConfigured,
                databaseHealth.IsSuccess,
                databaseHealth.TableCount,
                databaseHealth.Message
            },
            julesSessions = sessionsOutput,
            julesPull = pullOutput,
            analysis = completion.Content,
            autoJulesSession = autoResult
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string ReadTextFiles(string folder, int maxChars)
    {
        if (!Directory.Exists(folder))
        {
            return "Klasor yok.";
        }

        var parts = Directory
            .GetFiles(folder, "*.txt")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(12)
            .Select(path => $"--- {Path.GetFileName(path)} ---{Environment.NewLine}{File.ReadAllText(path)}");

        return Trim(string.Join(Environment.NewLine, parts), maxChars);
    }

    private static string ExtractString(string content, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
        }
        catch
        {
            var match = Regex.Match(content, $@"""{propertyName}""\s*:\s*""(?<value>.*?)""", RegexOptions.Singleline);
            return match.Success ? Regex.Unescape(match.Groups["value"].Value) : "";
        }
    }

    private static bool ExtractBool(string content, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return Regex.IsMatch(content, $@"""{propertyName}""\s*:\s*true", RegexOptions.IgnoreCase);
        }
    }

    private static string Trim(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars] + "...";
    }
}
