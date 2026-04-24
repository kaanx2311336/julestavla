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
    private readonly WorkspaceAutomationService workspaceAutomationService = new();

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
            var automation = await ProcessTrackedCompletedSessionAsync(settings, trackedSessionIdAtStart, relevantSessionsOutput, events, cancellationToken);
            var pullOutput = SelectPullOutput(automation);

            var databaseHealth = await databaseHealthService.TestAsync(databaseConnectionString, cancellationToken);
            events.Add(CreateEvent(
                "database_health_checked",
                databaseHealth.IsSuccess ? "info" : "warning",
                databaseHealth.Message,
                new { databaseHealth.IsConfigured, databaseHealth.IsSuccess, databaseHealth.TableCount }));

            var projectContext = BuildProjectContext(settings, relevantSessionsOutput, pullOutput, databaseHealth, automation);
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
                completion = CreateLocalFallbackCompletion(settings, relevantSessionsOutput, databaseHealth, automation, exception);
                events.Add(CreateEvent("openrouter_degraded", "warning", "OpenRouter free modelleri gecici kullanilamadi; yerel rapor uretildi.", new { exception.GetType().Name }));
            }

            var nextPrompt = ExtractString(completion.Content, "nextPrompt");
            var shouldStart = ExtractBool(completion.Content, "shouldStartNewJulesSession");
            var shouldContinueCompletedSession = settings.AutoContinueCompletedSessions
                && automation.TrackedSessionCompleted
                && !automation.AutomationBlocked
                && !agentStateService.HasHandledCompletedSession(settings, trackedSessionIdAtStart);
            CommandResult? autoResult = null;
            var newJulesSessionId = "";

            if (((settings.AllowAutoJulesSessions && shouldStart) || shouldContinueCompletedSession) && !string.IsNullOrWhiteSpace(nextPrompt))
            {
                if (agentStateService.HasSentPrompt(settings, nextPrompt))
                {
                    events.Add(CreateEvent(
                        "duplicate_next_prompt_skipped",
                        "warning",
                        "Ayni Jules promptu daha once gonderildigi icin yeni session acilmadi.",
                        new { trackedSessionIdAtStart }));
                }
                else
                {
                    autoResult = await julesCliService.CreateSessionAsync(settings, nextPrompt, cancellationToken);
                    newJulesSessionId = AgentStateService.ParseSessionId(autoResult);

                    if (autoResult.IsSuccess)
                    {
                        agentStateService.MarkPromptSent(settings, nextPrompt, newJulesSessionId);
                    }

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
            }
            else if (automation.TrackedSessionCompleted && !shouldContinueCompletedSession)
            {
                events.Add(CreateEvent(
                    "completed_session_already_handled",
                    "info",
                    "Izlenen completed Jules session daha once devam ettirildi, otomatik devam kapali veya otomasyon bloklandi.",
                    new { trackedSessionIdAtStart, settings.AutoContinueCompletedSessions, automation.AutomationBlocked }));
            }
            else
            {
                events.Add(CreateEvent("next_prompt_prepared", "info", "Ajan sonraki Jules promptunu hazirladi; otomatik session acilmadi.", new { shouldStart, settings.AllowAutoJulesSessions }));
            }

            var reportPath = SaveReport(settings, completion, relevantSessionsOutput, pullOutput, databaseHealth, autoResult, automation);
            var completedAt = DateTimeOffset.Now;
            var runStatus = automation.AutomationBlocked ? "blocked" : "completed";
            var sqlReport = BuildSqlReport(settings, runUuid, startedAt, completedAt, runStatus, completion, relevantSessionsOutput, reportPath, "");
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
                Automation = automation,
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
            var failurePath = SaveReport(settings, failureCompletion, "", null, new DatabaseHealthResult { Message = "Ajan turu hata ile bitti." }, null, new AgentAutomationArtifacts { Summary = exception.Message, AutomationBlocked = true });
            var sqlReport = BuildSqlReport(settings, runUuid, startedAt, completedAt, "failed", failureCompletion, "", failurePath, exception.Message);
            var sqlMessage = await TryWriteSqlReportAsync(databaseConnectionString, settings, sqlReport, events, cancellationToken);
            dashboardExporter.Export(settings, failureCompletion, failurePath, sqlMessage, "", null, events);
            throw;
        }
    }

    private async Task<AgentAutomationArtifacts> ProcessTrackedCompletedSessionAsync(
        ProjectSettings settings,
        string trackedSessionId,
        string sessionsOutput,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        var automation = new AgentAutomationArtifacts
        {
            TrackedSessionCompleted = IsTrackedSessionCompleted(sessionsOutput, trackedSessionId)
        };

        if (string.IsNullOrWhiteSpace(trackedSessionId))
        {
            automation.Summary = "Izlenen Jules session id bos; otomasyon beklemede.";
            return automation;
        }

        if (!sessionsOutput.Contains(trackedSessionId, StringComparison.Ordinal))
        {
            automation.Summary = "Izlenen Jules session listede bulunamadi; otomasyon beklemede.";
            return automation;
        }

        if (!automation.TrackedSessionCompleted)
        {
            automation.Summary = "Izlenen Jules session henuz tamamlanmadi; ajan durum raporu uretir ve bekler.";
            return automation;
        }

        if (TryFindDuplicateCompletedSession(settings, sessionsOutput, trackedSessionId, out var duplicateOfSessionId))
        {
            automation.AlreadyApplied = true;
            automation.DuplicateCompletedSession = true;
            automation.DuplicateOfSessionId = duplicateOfSessionId;
            automation.Summary = $"Completed Jules session daha once islenen {duplicateOfSessionId} ile ayni gorunuyor; tekrar apply edilmeyecek.";
            events.Add(CreateEvent("duplicate_completed_session_skipped", "warning", "Ayni prompttan gelen completed Jules session tekrar apply edilmeyecek.", new { trackedSessionId, duplicateOfSessionId }));
            return automation;
        }

        if (!settings.AutoApplyCompletedSessionPatch)
        {
            automation.JulesPullResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: false, cancellationToken);
            automation.Summary = "Completed Jules session pull edildi, fakat otomatik apply kapali.";
            events.Add(CreateEvent("jules_session_pulled", automation.JulesPullResult.IsSuccess ? "info" : "error", "Izlenen Jules session pull sonucu alindi.", new { trackedSessionId, automation.JulesPullResult.ExitCode, apply = false }));
            return automation;
        }

        if (agentStateService.HasAppliedCompletedSession(settings, trackedSessionId))
        {
            automation.AlreadyApplied = true;
            automation.JulesPullResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: false, cancellationToken);
            automation.Summary = "Completed Jules session daha once apply edildi; tekrar uygulanmadi.";
            events.Add(CreateEvent("jules_session_already_applied", "info", "Izlenen completed Jules session daha once apply edilmis.", new { trackedSessionId }));
            return automation;
        }

        automation.GitStatusBeforeApply = await workspaceAutomationService.GetGitStatusAsync(settings, cancellationToken);
        events.Add(CreateEvent("git_status_before_apply", automation.GitStatusBeforeApply.IsSuccess ? "info" : "error", "Apply oncesi git durumu okundu.", new { automation.GitStatusBeforeApply.ExitCode, isClean = WorkspaceAutomationService.IsCleanStatus(automation.GitStatusBeforeApply) }));

        if (!WorkspaceAutomationService.IsCleanStatus(automation.GitStatusBeforeApply))
        {
            var pendingAppliedSessionId = agentStateService.GetPendingAppliedSessionId(settings);
            if (!string.IsNullOrWhiteSpace(pendingAppliedSessionId) && settings.AutoCommitAndPushAppliedChanges)
            {
                automation.ResumedDirtyWorkspace = true;
                events.Add(CreateEvent("dirty_workspace_recovery_started", "warning", "Onceki apply sonrasi kirli kalan workspace dogrulanip commit/push edilecek.", new { pendingAppliedSessionId, trackedSessionId }));
                var verified = await VerifyAppliedChangesAsync(settings, automation, events, cancellationToken);

                if (verified)
                {
                    await CommitAndPushAppliedChangesAsync(settings, pendingAppliedSessionId, automation, events, cancellationToken);
                }

                if (!automation.AutomationBlocked)
                {
                    agentStateService.ClearPendingAppliedSession(settings, pendingAppliedSessionId);
                    automation.Summary = $"Kirli workspace onceki apply icin toparlandi ve {pendingAppliedSessionId} commit/push sureci tamamlandi. Bu tur yeni apply baslatilmadi.";
                }
                else
                {
                    automation.Summary = "Kirli workspace toparlanamadi; build/test/commit/push detaylari agent_events icinde.";
                }
            }
            else
            {
                automation.AutomationBlocked = true;
                automation.JulesPullResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: false, cancellationToken);
                automation.Summary = "Calisma alani temiz olmadigi icin completed Jules patch'i otomatik uygulanmadi.";
                events.Add(CreateEvent("auto_apply_blocked_dirty_workspace", "warning", "Calisma alani temiz degil; otomatik apply durduruldu.", new { trackedSessionId, status = Trim(automation.GitStatusBeforeApply.Output + automation.GitStatusBeforeApply.Error, 1200) }));
            }

            automation.AutomationBlocked = true;
            return automation;
        }

        automation.JulesApplyResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: true, cancellationToken);
        automation.AppliedThisTurn = automation.JulesApplyResult.IsSuccess;
        automation.AutomationBlocked = !automation.JulesApplyResult.IsSuccess;
        events.Add(CreateEvent("jules_session_applied", automation.JulesApplyResult.IsSuccess ? "info" : "error", "Completed Jules patch apply denemesi yapildi.", new { trackedSessionId, automation.JulesApplyResult.ExitCode }));

        if (!automation.JulesApplyResult.IsSuccess)
        {
            automation.Summary = "Completed Jules patch apply edilemedi; sonraki Jules session acilmadi.";
            return automation;
        }

        agentStateService.MarkCompletedSessionApplied(settings, trackedSessionId);

        if (settings.AutoRunVerification && !await VerifyAppliedChangesAsync(settings, automation, events, cancellationToken))
        {
            automation.Summary = "Jules patch apply edildi ama dogrulama basarisiz; commit/push ve sonraki session durduruldu.";
            automation.GitStatusAfterAutomation = await workspaceAutomationService.GetGitStatusAsync(settings, cancellationToken);
            return automation;
        }

        if (settings.AutoCommitAndPushAppliedChanges)
        {
            await CommitAndPushAppliedChangesAsync(settings, trackedSessionId, automation, events, cancellationToken);
            if (!automation.AutomationBlocked)
            {
                agentStateService.ClearPendingAppliedSession(settings, trackedSessionId);
            }
        }

        automation.GitStatusAfterAutomation = await workspaceAutomationService.GetGitStatusAsync(settings, cancellationToken);
        automation.Summary = BuildAutomationSummary(automation);
        return automation;
    }

    private async Task<bool> VerifyAppliedChangesAsync(
        ProjectSettings settings,
        AgentAutomationArtifacts automation,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        automation.VerificationBuildResult = await workspaceAutomationService.BuildAsync(settings, cancellationToken);
        events.Add(CreateEvent("verification_build_completed", automation.VerificationBuildResult.IsSuccess ? "info" : "error", "Otomatik dotnet build tamamlandi.", new { automation.VerificationBuildResult.ExitCode }));

        if (automation.VerificationBuildResult.IsSuccess)
        {
            automation.VerificationTestResult = await workspaceAutomationService.TestAsync(settings, cancellationToken);
            events.Add(CreateEvent("verification_test_completed", automation.VerificationTestResult.IsSuccess ? "info" : "error", "Otomatik dotnet test tamamlandi.", new { automation.VerificationTestResult.ExitCode }));
        }

        if (!automation.VerificationBuildResult.IsSuccess || automation.VerificationTestResult is { IsSuccess: false })
        {
            automation.AutomationBlocked = true;
            return false;
        }

        return true;
    }

    private async Task CommitAndPushAppliedChangesAsync(
        ProjectSettings settings,
        string trackedSessionId,
        AgentAutomationArtifacts automation,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        automation.GitStageResult = await workspaceAutomationService.StageAllAsync(settings, cancellationToken);
        events.Add(CreateEvent("git_stage_completed", automation.GitStageResult.IsSuccess ? "info" : "error", "Degisiklikler git index'e alindi.", new { automation.GitStageResult.ExitCode }));
        if (!automation.GitStageResult.IsSuccess)
        {
            automation.AutomationBlocked = true;
            return;
        }

        automation.SecretScanResult = await workspaceAutomationService.SecretScanAsync(settings, cancellationToken);
        var secretScanPassed = WorkspaceAutomationService.SecretScanPassed(automation.SecretScanResult);
        events.Add(CreateEvent("git_secret_scan_completed", secretScanPassed ? "info" : "error", "Staged secret taramasi tamamlandi.", new { automation.SecretScanResult.ExitCode, passed = secretScanPassed }));
        if (!secretScanPassed)
        {
            automation.AutomationBlocked = true;
            return;
        }

        automation.GitCommitResult = await workspaceAutomationService.CommitAsync(settings, trackedSessionId, cancellationToken);
        var nothingToCommit = WorkspaceAutomationService.HasNothingToCommit(automation.GitCommitResult);
        events.Add(CreateEvent("git_commit_completed", automation.GitCommitResult.IsSuccess || nothingToCommit ? "info" : "error", "Otomatik commit denemesi tamamlandi.", new { automation.GitCommitResult.ExitCode, nothingToCommit }));
        if (!automation.GitCommitResult.IsSuccess)
        {
            automation.AutomationBlocked = !nothingToCommit;
            return;
        }

        automation.GitPushResult = await workspaceAutomationService.PushAsync(settings, cancellationToken);
        events.Add(CreateEvent("git_push_completed", automation.GitPushResult.IsSuccess ? "info" : "error", "Otomatik push denemesi tamamlandi.", new { automation.GitPushResult.ExitCode }));
        if (!automation.GitPushResult.IsSuccess)
        {
            automation.AutomationBlocked = true;
        }
    }

    private bool TryFindDuplicateCompletedSession(ProjectSettings settings, string sessionsOutput, string trackedSessionId, out string duplicateOfSessionId)
    {
        duplicateOfSessionId = "";
        var trackedDescription = NormalizeSessionDescription(TryGetSessionDescription(sessionsOutput, trackedSessionId));
        if (trackedDescription.Length < 28)
        {
            return false;
        }

        var knownSessionIds = agentStateService
            .GetAppliedCompletedSessionIds(settings)
            .Concat(agentStateService.GetHandledCompletedSessionIds(settings))
            .Where(id => !id.Equals(trackedSessionId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

        foreach (var sessionId in knownSessionIds)
        {
            var knownDescription = NormalizeSessionDescription(TryGetSessionDescription(sessionsOutput, sessionId));
            if (knownDescription.Length < 28)
            {
                continue;
            }

            if (trackedDescription.StartsWith(knownDescription, StringComparison.Ordinal)
                || knownDescription.StartsWith(trackedDescription, StringComparison.Ordinal)
                || CommonPrefixLength(trackedDescription, knownDescription) >= 40)
            {
                duplicateOfSessionId = sessionId;
                return true;
            }
        }

        return false;
    }

    private static string TryGetSessionDescription(string sessionsOutput, string sessionId)
    {
        var line = sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.Contains(sessionId, StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(line))
        {
            return "";
        }

        var match = Regex.Match(line, @"^\s*\d{10,}\s+(?<description>.*?)\s+[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["description"].Value : line;
    }

    private static string NormalizeSessionDescription(string value)
    {
        value = value.Replace("…", "", StringComparison.Ordinal).Trim().ToLowerInvariant();
        return Regex.Replace(value, @"\s+", " ");
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var count = Math.Min(left.Length, right.Length);
        for (var i = 0; i < count; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return count;
    }

    private static string? SelectPullOutput(AgentAutomationArtifacts automation)
    {
        if (automation.DuplicateCompletedSession)
        {
            return automation.Summary;
        }

        var result = automation.JulesApplyResult ?? automation.JulesPullResult;
        if (result is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output;
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
        Ajan dongusu su sekildedir: Jules durumunu oku, tamamlanan isi apply/dogrula/raporla, mevcut repo durumuna gore tek ve uygulanabilir sonraki promptu tasarla, sonra bir sonraki Jules session'a devam et.
        Jules henuz Planning veya In Progress ise yeni session isteme; sadece mevcut anlik durumu raporla.
        Completed is apply/dogrulama/commit/push ile guvenli hale geldiyse nextPrompt bir sonraki kucuk faz olsun.
        nextPrompt mutlaka dosya/modul bazli, test/dogrulama beklentili, 100-500 satir bandinda ve mevcut prodetayi/yapilanlar disiplinine uygun olsun.
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
        var repoPrefix = settings.GitHubRepo.Length > 18
            ? settings.GitHubRepo[..18]
            : settings.GitHubRepo;

        var lines = sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
                line.Contains(settings.GitHubRepo, StringComparison.OrdinalIgnoreCase)
                || line.Contains(repoPrefix, StringComparison.OrdinalIgnoreCase)
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
        AgentAutomationArtifacts automation,
        Exception exception)
    {
        var content = JsonSerializer.Serialize(new
        {
            statusSummary = "OpenRouter free modelleri gecici olarak kullanilamadi; tavlajules yerel durum raporu yazdi ve yeni Jules promptu gondermedi.",
            whatJulesDid = sessionsOutput.Contains("In Progress", StringComparison.OrdinalIgnoreCase)
                ? "Izlenen TavlaJules Jules session'i hala devam ediyor."
                : string.IsNullOrWhiteSpace(automation.Summary)
                    ? "TavlaJules Jules session durumu okundu; detay icin agent_jules_sessions ve agent_events tablolarina bak."
                    : automation.Summary,
            nextPrompt = "",
            shouldStartNewJulesSession = false,
            databasePlan = databaseHealth.IsSuccess
                ? "ajanlarim raporlari yaziliyor; sonraki adim agent_runs/agent_events ekran veya sorgu gorunumlerini iyilestirmek."
                : "ajanlarim DB baglantisini dogrula; SQL rapor yazimi olmadan otomatik Jules gorevi baslatma.",
            riskNotes = new[]
            {
                "OpenRouter free provider 503 veya 429 verebilir; bu durumda tavlajules sadece SQL'e degraded rapor yazar.",
                "OpenRouter cevap vermeden ayni prompt tekrar Jules'e gonderilmez.",
                exception.Message
            }
        });

        return new OpenRouterCompletionResult
        {
            Model = "local-degraded",
            Content = content
        };
    }

    private static string BuildProjectContext(
        ProjectSettings settings,
        string sessionsOutput,
        string? pullOutput,
        DatabaseHealthResult databaseHealth,
        AgentAutomationArtifacts automation)
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

        OTONOM AJAN TUR OZETI:
        {automation.Summary}

        OTONOM KOMUT OZETLERI:
        {BuildCommandSummary(automation)}

        AJANLARIM SQL RAPOR DB DURUMU:
        Configured={databaseHealth.IsConfigured}; Success={databaseHealth.IsSuccess}; Tables={databaseHealth.TableCount}; Message={databaseHealth.Message}

        SON OPENROUTER/AJAN RAPORLARI:
        {ReadLatestAgentReports(Path.Combine(settings.ProjectFolder, "agent_reports"), 5000)}

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
        CommandResult? autoResult,
        AgentAutomationArtifacts automation)
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
            automation,
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

    private static string ReadLatestAgentReports(string folder, int maxChars)
    {
        if (!Directory.Exists(folder))
        {
            return "Rapor klasoru yok.";
        }

        var parts = Directory
            .GetFiles(folder, "*_agent_report.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(5)
            .Select(ReadAgentReportSummary);

        return Trim(string.Join(Environment.NewLine, parts), maxChars);
    }

    private static string ReadAgentReportSummary(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var model = root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() : "";
            var sessionId = root.TryGetProperty("trackedJulesSessionId", out var sessionValue) ? sessionValue.GetString() : "";
            var analysis = root.TryGetProperty("analysis", out var analysisValue) ? analysisValue.GetString() ?? "" : "";
            return $"--- {Path.GetFileName(path)} ---{Environment.NewLine}model={model}; session={sessionId}; analysis={Trim(analysis, 900)}";
        }
        catch
        {
            return $"--- {Path.GetFileName(path)} ---{Environment.NewLine}{Trim(File.ReadAllText(path), 900)}";
        }
    }

    private static string BuildCommandSummary(AgentAutomationArtifacts automation)
    {
        var lines = new List<string>
        {
            $"trackedCompleted={automation.TrackedSessionCompleted}",
            $"appliedThisTurn={automation.AppliedThisTurn}",
            $"alreadyApplied={automation.AlreadyApplied}",
            $"duplicateCompletedSession={automation.DuplicateCompletedSession}",
            $"resumedDirtyWorkspace={automation.ResumedDirtyWorkspace}",
            $"blocked={automation.AutomationBlocked}"
        };

        AddCommandLine(lines, "gitStatusBefore", automation.GitStatusBeforeApply);
        AddCommandLine(lines, "julesPull", automation.JulesPullResult);
        AddCommandLine(lines, "julesApply", automation.JulesApplyResult);
        AddCommandLine(lines, "dotnetBuild", automation.VerificationBuildResult);
        AddCommandLine(lines, "dotnetTest", automation.VerificationTestResult);
        AddCommandLine(lines, "gitStage", automation.GitStageResult);
        AddCommandLine(lines, "secretScan", automation.SecretScanResult);
        AddCommandLine(lines, "gitCommit", automation.GitCommitResult);
        AddCommandLine(lines, "gitPush", automation.GitPushResult);
        AddCommandLine(lines, "gitStatusAfter", automation.GitStatusAfterAutomation);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildAutomationSummary(AgentAutomationArtifacts automation)
    {
        if (!automation.TrackedSessionCompleted)
        {
            return automation.Summary;
        }

        if (automation.AutomationBlocked)
        {
            return string.IsNullOrWhiteSpace(automation.Summary)
                ? "Otonom akis bloklandi; agent_events detaylarini kontrol et."
                : automation.Summary;
        }

        if (automation.AppliedThisTurn)
        {
            var commitText = automation.GitCommitResult?.IsSuccess == true ? " commit edildi" : "";
            var pushText = automation.GitPushResult?.IsSuccess == true ? " ve pushlandi" : "";
            return $"Completed Jules patch'i apply edildi, dogrulandi{commitText}{pushText}.";
        }

        if (automation.AlreadyApplied)
        {
            return "Completed Jules patch'i daha once uygulanmis; sonraki prompt karari icin mevcut repo durumu inceleniyor.";
        }

        return automation.Summary;
    }

    private static void AddCommandLine(List<string> lines, string name, CommandResult? result)
    {
        if (result is null)
        {
            return;
        }

        var text = Trim((result.Output + " " + result.Error).ReplaceLineEndings(" "), 500);
        lines.Add($"{name}: exit={result.ExitCode}; {text}");
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
