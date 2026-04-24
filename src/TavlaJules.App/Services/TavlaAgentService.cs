using System.Text.Json;
using System.Text.RegularExpressions;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class TavlaAgentService
{
    private readonly JulesCliService julesCliService = new();
    private readonly OpenRouterClient openRouterClient = new();
    private readonly DatabaseHealthService databaseHealthService = new();
    private readonly AgentSqlReporter agentSqlReporter = new();
    private readonly AgentDashboardExporter dashboardExporter = new();
    private readonly AgentStateService agentStateService = new();

    public async Task<AgentRunResult> RunOnceAsync(
        ProjectSettings settings,
        string apiKey,
        string? databaseConnectionString,
        CancellationToken cancellationToken = default)
    {
        var runUuid = Guid.NewGuid();
        var startedAt = DateTimeOffset.Now;
        var events = new List<AgentEvent>
        {
            CreateEvent("run_started", "info", $"{settings.AgentName} ajani tek tur basladi.", new { settings.GitHubRepo, settings.TrackedJulesSessionId })
        };

        try
        {
            var sessions = await julesCliService.ListSessionsAsync(settings, cancellationToken);
            events.Add(CreateEvent("jules_sessions_read", "info", "Jules session listesi okundu.", new { exitCode = sessions.ExitCode }));
            var trackedSessionIdAtStart = settings.TrackedJulesSessionId;
            var relevantSessionsOutput = FilterRelevantSessions(sessions.Output, settings);

            var pullOutput = await TryPullTrackedSessionAsync(settings, relevantSessionsOutput, events, cancellationToken);
            var databaseHealth = await databaseHealthService.TestAsync(databaseConnectionString, cancellationToken);
            events.Add(CreateEvent(
                "database_health_checked",
                databaseHealth.IsSuccess ? "info" : "warning",
                databaseHealth.Message,
                new { databaseHealth.IsConfigured, databaseHealth.IsSuccess, databaseHealth.TableCount }));

            var projectContext = BuildProjectContext(settings, relevantSessionsOutput, pullOutput, databaseHealth);
            OpenRouterCompletionResult completion;

            try
            {
                completion = await openRouterClient.CompleteAsync(
                    settings,
                    apiKey,
                    BuildSystemPrompt(settings),
                    projectContext,
                    maxTokens: 1800,
                    cancellationToken);
                events.Add(CreateEvent("openrouter_analysis_completed", "info", "OpenRouter ajan analizi tamamlandi.", new { completion.Model }));
            }
            catch (Exception exception)
            {
                completion = CreateLocalFallbackCompletion(settings, relevantSessionsOutput, databaseHealth, exception);
                events.Add(CreateEvent("openrouter_degraded", "warning", "OpenRouter free modelleri gecici kullanilamadi; yerel rapor uretildi.", new { exception.GetType().Name }));
            }

            var nextPrompt = ExtractString(completion.Content, "nextPrompt");
            var shouldStart = ExtractBool(completion.Content, "shouldStartNewJulesSession");
            var trackedSessionCompleted = IsTrackedSessionCompleted(relevantSessionsOutput, trackedSessionIdAtStart);
            var shouldContinueCompletedSession = settings.AutoContinueCompletedSessions
                && trackedSessionCompleted
                && !agentStateService.HasHandledCompletedSession(settings, trackedSessionIdAtStart);
            CommandResult? autoResult = null;
            var newJulesSessionId = "";

            if (((settings.AllowAutoJulesSessions && shouldStart) || shouldContinueCompletedSession) && !string.IsNullOrWhiteSpace(nextPrompt))
            {
                autoResult = await julesCliService.CreateSessionAsync(settings, nextPrompt, cancellationToken);
                newJulesSessionId = AgentStateService.ParseSessionId(autoResult);

                if (autoResult.IsSuccess && shouldContinueCompletedSession)
                {
                    agentStateService.MarkCompletedSessionHandled(settings, trackedSessionIdAtStart, newJulesSessionId);

                    if (!string.IsNullOrWhiteSpace(newJulesSessionId))
                    {
                        settings.TrackedJulesSessionId = newJulesSessionId;
                    }
                }

                events.Add(CreateEvent(
                    "auto_jules_session_created",
                    autoResult.IsSuccess ? "info" : "error",
                    "Ajan otomatik Jules session denemesi yapti.",
                    new { autoResult.ExitCode, continuedFromSessionId = trackedSessionIdAtStart, newJulesSessionId, shouldContinueCompletedSession }));
            }
            else if (trackedSessionCompleted && !shouldContinueCompletedSession)
            {
                events.Add(CreateEvent(
                    "completed_session_already_handled",
                    "info",
                    "Izlenen completed Jules session daha once devam ettirildi veya otomatik devam kapali.",
                    new { trackedSessionIdAtStart, settings.AutoContinueCompletedSessions }));
            }
            else
            {
                events.Add(CreateEvent("next_prompt_prepared", "info", "Ajan sonraki Jules promptunu hazirladi; otomatik session acilmadi.", new { shouldStart, settings.AllowAutoJulesSessions }));
            }

            var reportPath = SaveReport(settings, completion, relevantSessionsOutput, pullOutput, databaseHealth, autoResult);
            var completedAt = DateTimeOffset.Now;
            var sqlReport = BuildSqlReport(settings, runUuid, startedAt, completedAt, "completed", completion, relevantSessionsOutput, reportPath, "");
            var sqlMessage = await TryWriteSqlReportAsync(databaseConnectionString, settings, sqlReport, events, cancellationToken);
            dashboardExporter.Export(settings, completion, reportPath, sqlMessage, relevantSessionsOutput, pullOutput, events);

            return new AgentRunResult
            {
                UsedModel = completion.Model,
                Analysis = completion.Content,
                NextPrompt = nextPrompt,
                ShouldStartNewJulesSession = shouldStart,
                ReportPath = reportPath,
                JulesSessionsRaw = relevantSessionsOutput,
                PullOutput = pullOutput,
                SqlReportMessage = sqlMessage,
                NewJulesSessionId = newJulesSessionId,
                AutoJulesSessionResult = autoResult
            };
        }
        catch (Exception exception)
        {
            events.Add(CreateEvent("run_failed", "error", exception.Message, new { exception.GetType().Name }));
            var completedAt = DateTimeOffset.Now;
            var failureCompletion = new OpenRouterCompletionResult
            {
                Model = settings.AgentModel,
                Content = JsonSerializer.Serialize(new
                {
                    statusSummary = "Ajan turu hata ile bitti.",
                    whatJulesDid = "",
                    nextPrompt = "",
                    shouldStartNewJulesSession = false,
                    databasePlan = "Hata duzeltilmeden DB/game fazina gecme.",
                    riskNotes = new[] { exception.Message }
                })
            };
            var failurePath = SaveReport(settings, failureCompletion, "", null, new DatabaseHealthResult { Message = "Ajan turu hata ile bitti." }, null);
            var sqlReport = BuildSqlReport(settings, runUuid, startedAt, completedAt, "failed", failureCompletion, "", failurePath, exception.Message);
            var sqlMessage = await TryWriteSqlReportAsync(databaseConnectionString, settings, sqlReport, events, cancellationToken);
            dashboardExporter.Export(settings, failureCompletion, failurePath, sqlMessage, "", null, events);
            throw;
        }
    }

    private async Task<string?> TryPullTrackedSessionAsync(ProjectSettings settings, string sessionsOutput, List<AgentEvent> events, CancellationToken cancellationToken)
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
        events.Add(CreateEvent("jules_session_pulled", pull.IsSuccess ? "info" : "error", "Izlenen Jules session pull sonucu alindi.", new { settings.TrackedJulesSessionId, pull.ExitCode }));
        return string.IsNullOrWhiteSpace(pull.Output) ? pull.Error : pull.Output;
    }

    private static bool IsTrackedSessionCompleted(string sessionsOutput, string trackedSessionId)
    {
        if (string.IsNullOrWhiteSpace(trackedSessionId))
        {
            return false;
        }

        return Regex.IsMatch(sessionsOutput, trackedSessionId + @".*(Completed|Needs\s+review)", RegexOptions.IgnoreCase);
    }

    private static string BuildSystemPrompt(ProjectSettings settings)
    {
        return """
        Sen 
        """ + settings.AgentName + """
         adli proje orkestrator ajanisin. Birincil modelin openai/gpt-oss-120b:free kabul edilir.
        Gorevin Jules'in son durumunu, yerel proje hafizasini, GitHub hedefini ve ajanlarim SQL rapor durumunu okuyup bir sonraki en dogru adimi tasarlamaktir.
        Proje, kullanicinin daha once yaptigi Batak projesine benzer sekilde fazli, loglu, prodetayi hafizali ve Jules destekli ilerlemelidir.
        Batak yalnizca surec disiplini ornegidir; cevapta Batak, FAZ 95 veya baska eski proje icerigi yazma.
        Konu sadece TavlaJules, tavla oyunu, Jules sessionlari ve ajanlarim SQL raporlamasidir.
        Gizli anahtar, connection string veya .env icerigini asla tekrar etme.
        Cevabini sadece gecerli JSON olarak ver:
        {
          "statusSummary": "kisa durum",
          "whatJulesDid": "Jules ne yapti veya hangi asamada",
          "nextPrompt": "Jules'e sonraki turda gonderilecek net prompt",
          "shouldStartNewJulesSession": false,
          "databasePlan": "ajanlarim raporlama ve tavla_online oyun DB icin sonraki DB adimi",
          "riskNotes": ["risk"]
        }
        """;
    }

    private static string FilterRelevantSessions(string sessionsOutput, ProjectSettings settings)
    {
        var lines = sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                line.Contains(settings.GitHubRepo, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(settings.TrackedJulesSessionId)
                    && line.Contains(settings.TrackedJulesSessionId, StringComparison.Ordinal)))
            .ToList();

        return lines.Count == 0
            ? "Bu repo icin Jules session satiri bulunamadi."
            : string.Join(Environment.NewLine, lines);
    }

    private static OpenRouterCompletionResult CreateLocalFallbackCompletion(
        ProjectSettings settings,
        string sessionsOutput,
        DatabaseHealthResult databaseHealth,
        Exception exception)
    {
        var content = JsonSerializer.Serialize(new
        {
            statusSummary = "OpenRouter free modelleri gecici olarak kullanilamadi; tavlajules yerel durum raporu yazdi.",
            whatJulesDid = sessionsOutput.Contains("In Progress", StringComparison.OrdinalIgnoreCase)
                ? "Izlenen TavlaJules Jules session'i hala devam ediyor."
                : "TavlaJules Jules session durumu okundu; detay icin agent_jules_sessions ve agent_events tablolarina bak.",
            nextPrompt = "Jules, TavlaJules reposunda mevcut durumu incele. Tavla kural motoru, mobil oyun akisi, ajanlarim SQL raporlari ve prodetayi/yapilanlar hafizasi disinda eski Batak fazlarina referans verme. Bir sonraki uygulanabilir tavla gelistirme adimini dosya bazli ve test odakli raporla.",
            shouldStartNewJulesSession = false,
            databasePlan = databaseHealth.IsSuccess
                ? "ajanlarim raporlari yaziliyor; sonraki adim agent_runs/agent_events ekran veya sorgu gorunumlerini iyilestirmek."
                : "ajanlarim DB baglantisini dogrula; SQL rapor yazimi olmadan otomatik Jules gorevi baslatma.",
            riskNotes = new[]
            {
                "OpenRouter free provider 503 veya 429 verebilir; bu durumda tavlajules SQL'e degraded rapor yazar.",
                "Fallback model listesinde gereksiz modeller tutulmamali."
            }
        });

        return new OpenRouterCompletionResult
        {
            Model = "local-degraded",
            Content = content
        };
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

        AJANLARIM SQL RAPOR DB DURUMU:
        Configured={databaseHealth.IsConfigured}; Success={databaseHealth.IsSuccess}; Tables={databaseHealth.TableCount}; Message={databaseHealth.Message}

        YEREL HAFIZA OZETLERI:
        {ReadTextFiles(Path.Combine(settings.ProjectFolder, "prodetayi"), 6000)}

        YAPILANLAR LOG OZETI:
        {ReadTextFiles(Path.Combine(settings.ProjectFolder, "yapilanlar"), 5000)}
        """;
    }

    private static AgentSqlRunReport BuildSqlReport(
        ProjectSettings settings,
        Guid runUuid,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        OpenRouterCompletionResult completion,
        string sessionsOutput,
        string reportPath,
        string errorText)
    {
        return new AgentSqlRunReport
        {
            RunUuid = runUuid,
            Status = status,
            TriggerSource = "timer_or_manual",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMs = (long)(completedAt - startedAt).TotalMilliseconds,
            Model = completion.Model,
            TrackedJulesSessionId = settings.TrackedJulesSessionId,
            GitHubRepo = settings.GitHubRepo,
            ReportPath = reportPath,
            StatusSummary = ExtractString(completion.Content, "statusSummary"),
            WhatJulesDid = ExtractString(completion.Content, "whatJulesDid"),
            NextPrompt = ExtractString(completion.Content, "nextPrompt"),
            ShouldStartNewJulesSession = ExtractBool(completion.Content, "shouldStartNewJulesSession"),
            DatabasePlan = ExtractString(completion.Content, "databasePlan"),
            RiskNotesJson = ExtractRawJson(completion.Content, "riskNotes", "[]"),
            AnalysisJson = NormalizeJsonForStorage(completion.Content),
            ErrorText = errorText,
            JulesSessionsRaw = sessionsOutput
        };
    }

    private async Task<string> TryWriteSqlReportAsync(
        string? connectionString,
        ProjectSettings settings,
        AgentSqlRunReport report,
        IReadOnlyList<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        try
        {
            return await agentSqlReporter.WriteRunAsync(connectionString, settings, report, events, cancellationToken);
        }
        catch (Exception exception)
        {
            return $"SQL raporu yazilamadi: {exception.Message}";
        }
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

    private static string ExtractRawJson(string content, string propertyName, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetRawText() : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string NormalizeJsonForStorage(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return JsonSerializer.Serialize(new { raw = content });
        }
    }

    private static AgentEvent CreateEvent(string eventType, string severity, string message, object metadata)
    {
        return new AgentEvent
        {
            EventType = eventType,
            Severity = severity,
            Message = message,
            MetadataJson = JsonSerializer.Serialize(metadata)
        };
    }

    private static string Trim(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars] + "...";
    }
}
