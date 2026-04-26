using System.Text.Json;
using System.Text.RegularExpressions;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class TavlaAgentService
{
    private static readonly TimeSpan AwaitingPlanApprovalRetryAfter = TimeSpan.FromMinutes(10);
    private const int AwaitingPlanApprovalMaxAttempts = 3;

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
            await ReplyToAwaitingInputSessionsAsync(settings, relevantSessionsOutput, automation, events, cancellationToken);
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

            var proposedNextPrompt = ExtractString(completion.Content, "nextPrompt");
            var nextPrompt = SelectNextPrompt(settings, proposedNextPrompt, relevantSessionsOutput, events);
            var shouldStart = ExtractBool(completion.Content, "shouldStartNewJulesSession");
            var shouldStartNextImplementedPhase = settings.AutoContinueCompletedSessions
                && automation.TrackedSessionCompleted
                && automation.AlreadyApplied
                && !automation.AutomationBlocked;
            if (string.IsNullOrWhiteSpace(nextPrompt) && shouldStartNextImplementedPhase)
            {
                var completedObjectiveKey = ResolveSessionObjectiveKey(settings, relevantSessionsOutput, trackedSessionIdAtStart);
                nextPrompt = BuildNextGameImprovementPrompt(settings, completedObjectiveKey);
                if (!string.IsNullOrWhiteSpace(nextPrompt))
                {
                    events.Add(CreateEvent(
                        "next_game_phase_selected",
                        "info",
                        "Completed hedef zaten uygulanmis oldugu icin lokal oyun yol haritasindan sonraki faz secildi.",
                        new { completedObjectiveKey, nextObjectiveKey = BuildPromptObjectiveKey(nextPrompt) }));
                }
            }

            var shouldContinueCompletedSession = settings.AutoContinueCompletedSessions
                && automation.TrackedSessionCompleted
                && !automation.AutomationBlocked
                && !agentStateService.HasHandledCompletedSession(settings, trackedSessionIdAtStart);
            var shouldRecoverAwaitingInputSession = settings.AutoRecoverAwaitingInputSessions
                && automation.TrackedSessionAwaitingInput
                && !automation.AwaitingInputAlreadyHandled
                && !automation.AwaitingInputReplySent
                && !automation.AutomationBlocked
                && !string.IsNullOrWhiteSpace(automation.AwaitingInputRecoveryPrompt);
            var completedObjectiveKeyAtStart = automation.TrackedSessionCompleted
                ? ResolveSessionObjectiveKey(settings, relevantSessionsOutput, trackedSessionIdAtStart)
                : "";
            var promptToSend = shouldRecoverAwaitingInputSession ? automation.AwaitingInputRecoveryPrompt : nextPrompt;
            var promptObjectiveKey = BuildPromptObjectiveKey(promptToSend);
            var trackedSessionBusy = IsTrackedSessionBusy(relevantSessionsOutput, trackedSessionIdAtStart, automation);
            var shouldContinueCompletedInPlace =
                !shouldRecoverAwaitingInputSession
                && (shouldContinueCompletedSession || shouldStartNextImplementedPhase)
                && ShouldContinueCompletedSessionInPlace(completedObjectiveKeyAtStart, promptObjectiveKey, promptToSend);
            CommandResult? autoResult = null;
            var newJulesSessionId = "";

            if (!trackedSessionBusy
                && (shouldRecoverAwaitingInputSession || (settings.AllowAutoJulesSessions && shouldStart) || shouldContinueCompletedSession || shouldStartNextImplementedPhase)
                && !string.IsNullOrWhiteSpace(promptToSend))
            {
                if (IsPromptObjectiveImplemented(settings.ProjectFolder, promptObjectiveKey))
                {
                    events.Add(CreateEvent(
                        "implemented_next_prompt_skipped",
                        "warning",
                        "Onerilen Jules prompt hedefi mevcut kodda uygulanmis gorundugu icin yeni session acilmadi.",
                        new { promptObjectiveKey, prompt = Trim(promptToSend, 700) }));
                }
                else
                {
                    var canRetryNoDiffCompletedObjective = automation.NoDiffObjectiveReopened
                        && shouldContinueCompletedSession
                        && ObjectiveKeysMatch(completedObjectiveKeyAtStart, promptObjectiveKey);
                    var duplicatePromptTarget = agentStateService.HasSentPrompt(settings, promptToSend)
                        || agentStateService.HasSentPromptObjective(settings, promptObjectiveKey)
                        || SessionsContainObjective(relevantSessionsOutput, promptObjectiveKey);

                    if (duplicatePromptTarget && !canRetryNoDiffCompletedObjective)
                    {
                        if (shouldRecoverAwaitingInputSession)
                        {
                            newJulesSessionId = agentStateService.GetLastPromptSessionId(settings);
                            agentStateService.MarkAwaitingInputSessionHandled(settings, trackedSessionIdAtStart, newJulesSessionId);

                            if (!string.IsNullOrWhiteSpace(newJulesSessionId))
                            {
                                settings.TrackedJulesSessionId = newJulesSessionId;
                            }
                        }

                        events.Add(CreateEvent(
                            "duplicate_next_prompt_skipped",
                            "warning",
                            "Ayni Jules prompt hedefi daha once gonderildigi veya Jules listesinde bulundugu icin yeni session acilmadi.",
                            new { trackedSessionIdAtStart, shouldRecoverAwaitingInputSession, promptObjectiveKey }));
                    }
                    else
                    {
                        if (duplicatePromptTarget && canRetryNoDiffCompletedObjective)
                        {
                            events.Add(CreateEvent(
                                "no_diff_duplicate_prompt_retry_allowed",
                                "warning",
                                "No-diff completed session hedefi eksik kaldigi icin eski prompt hash'i retry'i engellemeyecek.",
                                new { trackedSessionIdAtStart, completedObjectiveKeyAtStart, promptObjectiveKey }));
                        }

                        if (shouldContinueCompletedInPlace)
                        {
                            autoResult = await julesCliService.ReplyToSessionAsync(settings, trackedSessionIdAtStart, BuildCompletedSessionContinuationPrompt(promptToSend), cancellationToken);
                            automation.CompletedContinuationResult = autoResult;
                            newJulesSessionId = autoResult.IsSuccess ? trackedSessionIdAtStart : "";
                        }
                        else
                        {
                            autoResult = await julesCliService.CreateSessionAsync(settings, promptToSend, cancellationToken);
                            newJulesSessionId = AgentStateService.ParseSessionId(autoResult);
                        }

                        if (autoResult.IsSuccess)
                        {
                            agentStateService.MarkPromptSent(settings, promptToSend, newJulesSessionId, promptObjectiveKey);
                        }

                        if (autoResult.IsSuccess && shouldRecoverAwaitingInputSession)
                        {
                            automation.AwaitingInputRecoveryStarted = true;
                            agentStateService.MarkAwaitingInputSessionHandled(settings, trackedSessionIdAtStart, newJulesSessionId);
                            agentStateService.MarkAwaitingInputRecoverySession(settings, newJulesSessionId);

                            if (!string.IsNullOrWhiteSpace(newJulesSessionId))
                            {
                                settings.TrackedJulesSessionId = newJulesSessionId;
                            }
                        }

                        if (autoResult.IsSuccess && shouldContinueCompletedSession)
                        {
                            if (shouldContinueCompletedInPlace)
                            {
                                automation.CompletedSessionContinuedInPlace = true;
                                automation.CompletedContinuationRelated = true;
                                agentStateService.MarkCompletedSessionContinuedInPlace(settings, trackedSessionIdAtStart);
                            }
                            else
                            {
                                automation.CompletedSessionOpenedNewSession = true;
                                agentStateService.MarkCompletedSessionHandled(settings, trackedSessionIdAtStart, newJulesSessionId);
                            }

                            if (!string.IsNullOrWhiteSpace(newJulesSessionId))
                            {
                                settings.TrackedJulesSessionId = newJulesSessionId;
                            }
                        }

                        if (autoResult.IsSuccess && shouldStartNextImplementedPhase)
                        {
                            if (shouldContinueCompletedInPlace)
                            {
                                automation.CompletedSessionContinuedInPlace = true;
                                automation.CompletedContinuationRelated = true;
                                agentStateService.MarkCompletedSessionContinuedInPlace(settings, trackedSessionIdAtStart);
                            }
                            else
                            {
                                automation.CompletedSessionOpenedNewSession = true;
                                agentStateService.MarkCompletedSessionHandled(settings, trackedSessionIdAtStart, newJulesSessionId);
                            }

                            if (!string.IsNullOrWhiteSpace(newJulesSessionId))
                            {
                                settings.TrackedJulesSessionId = newJulesSessionId;
                            }
                        }

                        events.Add(CreateEvent(
                            shouldContinueCompletedInPlace ? "completed_jules_session_continued_in_place" : "auto_jules_session_created",
                            autoResult.IsSuccess ? "info" : "error",
                            shouldContinueCompletedInPlace
                                ? "Ajan baglantili promptu completed Jules session'in altina yazdi."
                                : "Ajan otomatik yeni Jules session denemesi yapti.",
                            new
                            {
                                autoResult.ExitCode,
                                continuedFromSessionId = trackedSessionIdAtStart,
                                newJulesSessionId,
                                completedObjectiveKeyAtStart,
                                promptObjectiveKey,
                                shouldContinueCompletedSession,
                                shouldRecoverAwaitingInputSession,
                                shouldStartNextImplementedPhase,
                                shouldContinueCompletedInPlace
                            }));
                    }
                }
            }
            else if (automation.TrackedSessionAwaitingInput)
            {
                events.Add(CreateEvent(
                    "jules_awaiting_input_observed",
                    automation.AwaitingInputAlreadyHandled ? "info" : "warning",
                    automation.AwaitingInputAlreadyHandled
                        ? "Izlenen Jules session daha once input bekliyor diye ele alindi."
                        : "Izlenen Jules session kullanici girdisi bekliyor; otomatik kurtarma kapali veya uygun degil.",
                    new { trackedSessionIdAtStart, settings.AutoRecoverAwaitingInputSessions, automation.AwaitingInputAlreadyHandled }));
            }
            else if (trackedSessionBusy)
            {
                events.Add(CreateEvent(
                    "tracked_jules_session_busy",
                    "info",
                    "Izlenen Jules session Planning/In Progress oldugu icin yeni Jules session acilmadi.",
                    new { trackedSessionIdAtStart }));
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
            TrackedSessionCompleted = IsTrackedSessionCompleted(sessionsOutput, trackedSessionId),
            TrackedSessionAwaitingInput = IsTrackedSessionAwaitingInput(sessionsOutput, trackedSessionId),
            TrackedSessionAwaitingPlanApproval = IsTrackedSessionAwaitingPlanApproval(sessionsOutput, trackedSessionId)
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

        if (automation.TrackedSessionAwaitingInput)
        {
            automation.AwaitingInputRecoverySession =
                agentStateService.HasAwaitingInputRecoverySession(settings, trackedSessionId)
                || IsLikelyAwaitingInputRecoverySession(sessionsOutput, trackedSessionId);
            if (automation.AwaitingInputRecoverySession)
            {
                agentStateService.MarkAwaitingInputRecoverySession(settings, trackedSessionId);
            }

            automation.AwaitingInputAlreadyHandled =
                automation.AwaitingInputRecoverySession
                || agentStateService.HasHandledAwaitingInputSession(settings, trackedSessionId);
            automation.AwaitingInputRecoveryPrompt = automation.AwaitingInputRecoverySession
                ? ""
                : BuildAwaitingInputRecoveryPrompt(settings, trackedSessionId, sessionsOutput);
            automation.Summary = automation.AwaitingInputRecoverySession
                ? "Izlenen Jules kurtarma session'i de kullanici girdisi bekliyor; ayni session icinde otomatik cevap denenecek."
                : automation.AwaitingInputAlreadyHandled
                    ? "Izlenen Jules session kullanici girdisi bekliyor; bu session icin kurtarma gorevi daha once acildi."
                    : "Izlenen Jules session kullanici girdisi bekliyor; CLI reply destegi olmadigi icin netlestirilmis yeni Jules gorevi hazirlandi.";
            events.Add(CreateEvent(
                "jules_awaiting_input_detected",
                automation.AwaitingInputAlreadyHandled ? "info" : "warning",
                automation.Summary,
                new { trackedSessionId, settings.AutoRecoverAwaitingInputSessions, automation.AwaitingInputAlreadyHandled, automation.AwaitingInputRecoverySession }));
            return automation;
        }

        var trackedObjectiveKey = ResolveSessionObjectiveKey(settings, sessionsOutput, trackedSessionId);
        if (automation.TrackedSessionAwaitingPlanApproval
            && IsPromptObjectiveImplemented(settings.ProjectFolder, trackedObjectiveKey))
        {
            automation.TrackedSessionCompleted = true;
            automation.AlreadyApplied = true;
            automation.DuplicateCompletedSession = true;
            automation.Summary = $"Awaiting Plan Approval session hedefi ({trackedObjectiveKey}) mevcut kodda zaten uygulanmis; no-op Jules session tamam sayildi.";
            agentStateService.MarkCompletedSessionHandled(settings, trackedSessionId, trackedSessionId);
            events.Add(CreateEvent(
                "awaiting_plan_objective_already_implemented",
                "warning",
                "Jules plan onayi bekliyor gorunuyor ama hedef lokal kodda zaten var; session no-op tamam kabul edildi.",
                new { trackedSessionId, trackedObjectiveKey }));
            return automation;
        }

        if (!automation.TrackedSessionCompleted)
        {
            automation.Summary = "Izlenen Jules session henuz tamamlanmadi; ajan durum raporu uretir ve bekler.";
            return automation;
        }

        var completedObjectiveKey = trackedObjectiveKey;

        if (!settings.AutoApplyCompletedSessionPatch)
        {
            automation.JulesPullResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: false, cancellationToken);
            automation.Summary = "Completed Jules session pull edildi, fakat otomatik apply kapali.";
            events.Add(CreateEvent("jules_session_pulled", automation.JulesPullResult.IsSuccess ? "info" : "error", "Izlenen Jules session pull sonucu alindi.", new { trackedSessionId, automation.JulesPullResult.ExitCode, apply = false }));
            return automation;
        }

        if (agentStateService.HasAppliedCompletedSession(settings, trackedSessionId))
        {
            automation.JulesPullResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: false, cancellationToken);
            var alreadyAppliedHasDiff = HasJulesPatchDiff(automation.JulesPullResult);
            if (!alreadyAppliedHasDiff
                && !IsPromptObjectiveImplemented(settings.ProjectFolder, completedObjectiveKey))
            {
                agentStateService.ReopenCompletedSessionObjective(settings, trackedSessionId, completedObjectiveKey);
                automation.AlreadyApplied = false;
                automation.NoDiffObjectiveReopened = true;
                automation.Summary = $"Completed Jules session {trackedSessionId} daha once apply edildi saniliyordu ama remote diff yok ve hedef ({completedObjectiveKey}) hala eksik; hedef yeniden gorevlendirilecek.";
                events.Add(CreateEvent(
                    "applied_session_without_diff_reopened",
                    "warning",
                    "Session applied listesinde ama remote diff yok ve hedef eksik; applied/duplicate kilidi kaldirildi.",
                    new { trackedSessionId, completedObjectiveKey }));
                return automation;
            }

            automation.AlreadyApplied = true;
            automation.Summary = "Completed Jules session daha once apply edildi; tekrar uygulanmadi.";
            events.Add(CreateEvent("jules_session_already_applied", "info", "Izlenen completed Jules session daha once apply edilmis.", new { trackedSessionId }));
            return automation;
        }

        automation.JulesPullResult = await julesCliService.PullSessionAsync(settings, trackedSessionId, apply: false, cancellationToken);
        var hasPreviewDiff = HasJulesPatchDiff(automation.JulesPullResult);
        if (!hasPreviewDiff)
        {
            if (IsPromptObjectiveImplemented(settings.ProjectFolder, completedObjectiveKey))
            {
                automation.AlreadyApplied = true;
                automation.DuplicateCompletedSession = true;
                automation.Summary = $"Completed Jules session hedefi ({completedObjectiveKey}) mevcut kodda zaten uygulanmis ve remote diff yok; no-op tamam kabul edildi.";
                agentStateService.MarkCompletedSessionHandled(settings, trackedSessionId, trackedSessionId);
                events.Add(CreateEvent(
                    "completed_no_diff_objective_already_implemented",
                    "info",
                    "Completed Jules session diff uretmedi ama hedef lokal kodda zaten mevcut.",
                    new { trackedSessionId, completedObjectiveKey }));
                return automation;
            }

            agentStateService.ReopenCompletedSessionObjective(settings, trackedSessionId, completedObjectiveKey);
            automation.NoDiffObjectiveReopened = true;
            automation.Summary = $"Completed Jules session {trackedSessionId} diff uretmedi ve hedef ({completedObjectiveKey}) hala eksik; ajan ayni hedefi yeniden netlestirip devam ettirecek.";
            events.Add(CreateEvent(
                "completed_no_diff_objective_missing",
                "warning",
                "Completed Jules session remote diff uretmedi ve hedef eksik; apply basarili sayilmayacak, prompt kilidi kaldirilacak.",
                new { trackedSessionId, completedObjectiveKey }));
            return automation;
        }

        if (TryFindDuplicateCompletedSession(settings, sessionsOutput, trackedSessionId, out var duplicateOfSessionId))
        {
            var duplicateWasApplied = agentStateService.HasAppliedCompletedSession(settings, duplicateOfSessionId);
            automation.DuplicateOfSessionId = duplicateOfSessionId;
            if (!hasPreviewDiff || duplicateWasApplied)
            {
                automation.AlreadyApplied = true;
                automation.DuplicateCompletedSession = true;
                automation.Summary = $"Completed Jules session daha once islenen {duplicateOfSessionId} ile ayni gorunuyor; tekrar apply edilmeyecek.";
                events.Add(CreateEvent(
                    "duplicate_completed_session_skipped",
                    "warning",
                    "Ayni prompttan gelen completed Jules session tekrar apply edilmeyecek.",
                    new { trackedSessionId, duplicateOfSessionId, duplicateWasApplied, hasPreviewDiff }));
                return automation;
            }

            events.Add(CreateEvent(
                "duplicate_completed_session_has_patch",
                "warning",
                "Completed session duplicate gorunuyor ama onceki eslesen session apply edilmemis ve yeni patch diff'i var; apply akisi devam edecek.",
                new { trackedSessionId, duplicateOfSessionId, hasPreviewDiff }));
        }

        if (automation.JulesPullResult.IsSuccess
            && IsCompletedPatchWrongTarget(completedObjectiveKey, automation.JulesPullResult.Output))
        {
            automation.AlreadyApplied = true;
            automation.DuplicateCompletedSession = true;
            automation.Summary = "Completed Jules patch'i hedef dosyalara dokunmadigi icin uygulanmadi; ajan ayni hedefi dogru dosya sinirlariyla yeniden gorevlendirecek.";
            agentStateService.MarkCompletedSessionHandled(settings, trackedSessionId, trackedSessionId);
            agentStateService.ForgetPromptSent(settings, BuildNextGameImprovementPrompt(settings, completedObjectiveKey), completedObjectiveKey, trackedSessionId);
            events.Add(CreateEvent(
                "completed_patch_wrong_target_skipped",
                "warning",
                "Jules tamamlanan isi meta/orkestrator dosyalarina yazmis; data/migration/test hedeflerine dokunmadigi icin patch apply edilmeyecek.",
                new
                {
                    trackedSessionId,
                    completedObjectiveKey,
                    changedFiles = ExtractChangedFilesFromDiff(automation.JulesPullResult.Output)
                }));
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
                automation.Summary = "Calisma alani temiz olmadigi icin completed Jules patch'i otomatik uygulanmadi.";
                events.Add(CreateEvent("auto_apply_blocked_dirty_workspace", "warning", "Calisma alani temiz degil; otomatik apply durduruldu.", new { trackedSessionId, status = Trim(automation.GitStatusBeforeApply.Output + automation.GitStatusBeforeApply.Error, 1200) }));
            }

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

    private async Task ReplyToAwaitingInputSessionsAsync(
        ProjectSettings settings,
        string sessionsOutput,
        AgentAutomationArtifacts automation,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        automation.AwaitingInputSessionIds = FindAwaitingInputSessionIds(sessionsOutput).ToList();
        automation.AwaitingPlanSessionIds = FindAwaitingPlanSessionIds(sessionsOutput).ToList();
        if (automation.TrackedSessionCompleted && !string.IsNullOrWhiteSpace(settings.TrackedJulesSessionId))
        {
            automation.AwaitingInputSessionIds.RemoveAll(sessionId => sessionId.Equals(settings.TrackedJulesSessionId, StringComparison.Ordinal));
            automation.AwaitingPlanSessionIds.RemoveAll(sessionId => sessionId.Equals(settings.TrackedJulesSessionId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(settings.TrackedJulesSessionId)
            && automation.AwaitingInputSessionIds.Contains(settings.TrackedJulesSessionId, StringComparer.Ordinal))
        {
            automation.TrackedSessionAwaitingInput = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.TrackedJulesSessionId)
            && automation.AwaitingPlanSessionIds.Contains(settings.TrackedJulesSessionId, StringComparer.Ordinal))
        {
            automation.TrackedSessionAwaitingPlanApproval = true;
        }

        if (automation.AwaitingInputSessionIds.Count == 0 && automation.AwaitingPlanSessionIds.Count == 0)
        {
            return;
        }

        automation.AwaitingPlanApprovalPrompt = BuildAwaitingPlanApprovalPrompt(settings);

        if (!settings.AutoReplyAwaitingInputSessions)
        {
            events.Add(CreateEvent(
                "jules_awaiting_input_reply_skipped",
                "warning",
                "Awaiting User Feedback session bulundu ama otomatik cevap kapali.",
                new { automation.AwaitingInputSessionIds }));
            return;
        }

        foreach (var sessionId in automation.AwaitingPlanSessionIds.Take(3))
        {
            var alreadyApproved = agentStateService.HasApprovedAwaitingPlanSession(settings, sessionId);
            if (alreadyApproved
                && !agentStateService.ShouldRetryAwaitingPlanApproval(
                    settings,
                    sessionId,
                    AwaitingPlanApprovalRetryAfter,
                    AwaitingPlanApprovalMaxAttempts))
            {
                automation.AwaitingPlanApprovalAlreadySent = true;
                events.Add(CreateEvent(
                    "jules_awaiting_plan_approval_already_sent",
                    "info",
                    "Bu Awaiting Plan Approval session icin daha once onay gonderildi; retry araligi veya maksimum deneme siniri nedeniyle tekrar gonderilmedi.",
                    new
                    {
                        sessionId,
                        attempts = agentStateService.GetAwaitingPlanApprovalAttemptCount(settings, sessionId),
                        retryAfterMinutes = AwaitingPlanApprovalRetryAfter.TotalMinutes,
                        maxAttempts = AwaitingPlanApprovalMaxAttempts
                    }));
                continue;
            }

            if (alreadyApproved)
            {
                automation.AwaitingPlanApprovalRetryDue = true;
                events.Add(CreateEvent(
                    "jules_awaiting_plan_approval_retrying",
                    "warning",
                    "Jules hala Awaiting Plan Approval gorundugu icin onay tekrar gonderilecek.",
                    new
                    {
                        sessionId,
                        attempts = agentStateService.GetAwaitingPlanApprovalAttemptCount(settings, sessionId),
                        retryAfterMinutes = AwaitingPlanApprovalRetryAfter.TotalMinutes
                    }));
            }

            var result = await julesCliService.ReplyToSessionAsync(settings, sessionId, automation.AwaitingPlanApprovalPrompt, cancellationToken);
            automation.AwaitingPlanApprovalResults.Add(result);

            if (result.IsSuccess)
            {
                automation.AwaitingPlanApprovalSent = true;
                agentStateService.MarkAwaitingPlanSessionApproved(settings, sessionId);
            }

            events.Add(CreateEvent(
                "jules_awaiting_plan_approval_sent",
                result.IsSuccess ? "info" : "error",
                result.IsSuccess ? "Jules Awaiting Plan Approval session'ina onay gonderildi." : "Jules Awaiting Plan Approval session'ina onay gonderilemedi.",
                new { sessionId, result.ExitCode, output = Trim(result.Output, 700), error = Trim(result.Error, 900) }));
        }

        foreach (var sessionId in automation.AwaitingInputSessionIds.Take(3))
        {
            if (agentStateService.HasRepliedAwaitingInputSession(settings, sessionId))
            {
                events.Add(CreateEvent(
                    "jules_awaiting_input_reply_already_sent",
                    "info",
                    "Bu Awaiting User Feedback session icin daha once cevap gonderildi.",
                    new { sessionId }));
                continue;
            }

            var replyPrompt = BuildAwaitingInputReplyPrompt(settings, sessionId, sessionsOutput);
            if (string.IsNullOrWhiteSpace(automation.AwaitingInputReplyPrompt))
            {
                automation.AwaitingInputReplyPrompt = replyPrompt;
            }

            var result = await julesCliService.ReplyToSessionAsync(settings, sessionId, replyPrompt, cancellationToken);
            automation.AwaitingInputReplyResults.Add(result);

            if (result.IsSuccess)
            {
                automation.AwaitingInputReplySent = true;
                agentStateService.MarkAwaitingInputSessionReplied(settings, sessionId);
            }

            events.Add(CreateEvent(
                "jules_awaiting_input_reply_sent",
                result.IsSuccess ? "info" : "error",
                result.IsSuccess ? "Jules Awaiting User Feedback session'ina cevap gonderildi." : "Jules Awaiting User Feedback session'ina cevap gonderilemedi.",
                new { sessionId, result.ExitCode, output = Trim(result.Output, 700), error = Trim(result.Error, 900) }));
        }

        if (automation.AwaitingInputReplySent)
        {
            automation.Summary = "Awaiting User Feedback durumundaki Jules session'a net GameEngine metot cevabi gonderildi; ajan tamamlanmasini bekleyecek.";
        }

        if (automation.AwaitingPlanApprovalSent)
        {
            automation.Summary = automation.AwaitingPlanApprovalRetryDue
                ? "Awaiting Plan Approval durumundaki Jules session hala bekledigi icin plan onayi tekrar gonderildi."
                : "Awaiting Plan Approval durumundaki Jules session'a plan onayi gonderildi; ajan uygulamaya gecmeli.";
        }
        else if (automation.AwaitingPlanApprovalAlreadySent)
        {
            automation.Summary = "Awaiting Plan Approval session'a onay daha once gonderildi; retry araligi veya maksimum deneme siniri bekleniyor.";
        }
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

        return Regex.IsMatch(sessionsOutput, Regex.Escape(trackedSessionId) + @".*(Completed|Needs\s+review)", RegexOptions.IgnoreCase);
    }

    private static bool IsTrackedSessionAwaitingInput(string sessionsOutput, string trackedSessionId)
    {
        if (string.IsNullOrWhiteSpace(trackedSessionId))
        {
            return false;
        }

        var line = FindSessionLine(sessionsOutput, trackedSessionId);
        return !string.IsNullOrWhiteSpace(line) && IsAwaitingInputSessionLine(line);
    }

    private static bool IsTrackedSessionAwaitingPlanApproval(string sessionsOutput, string trackedSessionId)
    {
        if (string.IsNullOrWhiteSpace(trackedSessionId))
        {
            return false;
        }

        var line = FindSessionLine(sessionsOutput, trackedSessionId);
        return !string.IsNullOrWhiteSpace(line) && IsPlanApprovalSessionLine(line);
    }

    private static bool IsLikelyAwaitingInputRecoverySession(string sessionsOutput, string trackedSessionId)
    {
        var description = NormalizeSessionDescription(TryGetSessionDescription(sessionsOutput, trackedSessionId));
        return description.Contains("previous session", StringComparison.Ordinal)
            && description.Contains("waiting for", StringComparison.Ordinal);
    }

    private static IEnumerable<string> FindAwaitingInputSessionIds(string sessionsOutput)
    {
        return sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new
            {
                Line = line,
                Match = Regex.Match(line, @"^\s*(?<id>\d{10,})", RegexOptions.IgnoreCase)
            })
            .Where(item => item.Match.Success
                && !IsPlanApprovalSessionLine(item.Line)
                && IsAwaitingInputSessionLine(item.Line))
            .Select(item => item.Match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal);
    }

    private static string FindSessionLine(string sessionsOutput, string sessionId)
    {
        return sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.Contains(sessionId, StringComparison.Ordinal))
            ?? "";
    }

    private static bool IsAwaitingInputSessionLine(string line)
    {
        if (IsPlanApprovalSessionLine(line))
        {
            return false;
        }

        if (Regex.IsMatch(
                line,
                @"(Awaiting\s+User|Awaiting\s+.*Feedback|User\s+Feedback|Needs\s+input|Waiting\s+for\s+.*input|Awaiting\s+Plan|Plan\s+Approval|Awaiting\s+.*Approval)",
                RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(line, @"\b(Completed|Needs\s+review|In\s+Progress|Planning)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var description = NormalizeSessionDescription(ExtractSessionDescriptionFromLine(line));
        if (LooksLikeClarificationRequest(description))
        {
            return true;
        }

        return IsStaleInactiveSessionLine(line);
    }

    private static string ExtractSessionDescriptionFromLine(string line)
    {
        var match = Regex.Match(line, @"^\s*\d{10,}\s+(?<description>.*?)\s+[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["description"].Value : line;
    }

    private static bool LooksLikeClarificationRequest(string description)
    {
        return description.Contains("please provide", StringComparison.Ordinal)
            || description.Contains("exact method", StringComparison.Ordinal)
            || description.Contains("method specification", StringComparison.Ordinal)
            || description.Contains("what should i add", StringComparison.Ordinal)
            || description.Contains("could you please provide", StringComparison.Ordinal)
            || description.Contains("needs detailed requirements", StringComparison.Ordinal)
            || description.Contains("user feedback", StringComparison.Ordinal)
            || description.Contains("clarification", StringComparison.Ordinal);
    }

    private static bool IsStaleInactiveSessionLine(string line)
    {
        if (!TryParseLastActiveAge(line, out var age))
        {
            return false;
        }

        return age >= TimeSpan.FromMinutes(5);
    }

    private static bool TryParseLastActiveAge(string line, out TimeSpan age)
    {
        age = TimeSpan.Zero;
        var match = Regex.Match(line, @"(?<age>(?:(?<days>\d+)d)?(?:(?<hours>\d+)h)?(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)?)\s+ago", RegexOptions.IgnoreCase);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups["age"].Value))
        {
            return false;
        }

        var days = ParseInt(match.Groups["days"].Value);
        var hours = ParseInt(match.Groups["hours"].Value);
        var minutes = ParseInt(match.Groups["minutes"].Value);
        var seconds = ParseInt(match.Groups["seconds"].Value);
        age = new TimeSpan(days, hours, minutes, seconds);
        return age > TimeSpan.Zero;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }

    private static IEnumerable<string> FindAwaitingPlanSessionIds(string sessionsOutput)
    {
        return sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new
            {
                Line = line,
                Match = Regex.Match(line, @"^\s*(?<id>\d{10,})", RegexOptions.IgnoreCase)
            })
            .Where(item => item.Match.Success && IsPlanApprovalSessionLine(item.Line))
            .Select(item => item.Match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal);
    }

    private static bool IsPlanApprovalSessionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (Regex.IsMatch(
                line,
                @"(Awaiting\s+Plan|Plan\s+Approval|Awaiting\s+.*Approval)",
                RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(line, @"\b(Completed|Needs\s+review|In\s+Progress)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var description = NormalizeSessionDescription(ExtractSessionDescriptionFromLine(line));
        return description.Contains("please approve", StringComparison.Ordinal)
            || description.Contains("approve the following plan", StringComparison.Ordinal)
            || description.Contains("plan so jules can proceed", StringComparison.Ordinal)
            || description.Contains("finalize this commit", StringComparison.Ordinal)
            || description.Contains("no changes to code", StringComparison.Ordinal)
            || description.Contains("consider this step already completed", StringComparison.Ordinal)
            || description.Contains("already implemented", StringComparison.Ordinal);
    }

    private static string BuildAwaitingInputReplyPrompt(ProjectSettings settings, string sessionId, string sessionsOutput)
    {
        var description = NormalizeSessionDescription(TryGetSessionDescription(sessionsOutput, sessionId));
        if (description.Contains("capturegamestatesnapshot", StringComparison.Ordinal)
            || description.Contains("game state snapshot", StringComparison.Ordinal)
            || description.Contains("snapshot method", StringComparison.Ordinal))
        {
            return BuildSnapshotClarificationPrompt(settings);
        }

        if (description.Contains("generatelegalmoves", StringComparison.Ordinal)
            || description.Contains("roll dice", StringComparison.Ordinal)
            || description.Contains("rolldice", StringComparison.Ordinal)
            || description.Contains("turn management", StringComparison.Ordinal)
            || description.Contains("gameengine method", StringComparison.Ordinal))
        {
            return BuildTurnDiceClarificationPrompt(settings);
        }

        return $"""
        Continue this same Jules session for repo {settings.GitHubRepo}.

        Do not ask another clarification question. Use the existing codebase, `prodetayi/` summaries, and `yapilanlar/` notes to choose the smallest safe implementation detail. If the requested capability is already implemented, finalize the session with no code changes and mark it complete.

        Guardrails:
        - Keep each patch focused around 100-500 lines.
        - Update relevant `prodetayi/` summaries and add one dated `yapilanlar/` note if you touch code.
        - Use the existing project stack and Aiven MySQL assumptions; do not introduce PostgreSQL, JSONB, or EF Core unless they already exist in this repo.
        - Do not print or commit secrets, `.env`, API keys, or database passwords.
        """;
    }

    private static string BuildTurnDiceClarificationPrompt(ProjectSettings settings)
    {
        return $"""
        Here are the exact GameEngine requirements. Please continue this same Jules session and implement them now; do not ask another clarification question.

        Repo: {settings.GitHubRepo}

        Target files:
        - `src/TavlaJules.Engine/Engine/GameEngine.cs`
        - `src/TavlaJules.Engine.Tests/Engine/GameEngineTests.cs`
        - `prodetayi/GameEngine.md`
        - one new dated note under `yapilanlar/`

        Add these GameEngine capabilities:
        1. `RollDice(Random? random = null): (int die1, int die2)`
           Return two dice values between 1 and 6. Use the supplied random when present for deterministic tests.

        2. `StartTurn(PlayerColor player, int die1, int die2): void`
           Validate dice values are 1..6, set `CurrentTurn`, store the current dice, and create remaining move dice. Doubles must create four remaining dice.

        3. `IReadOnlyList<int> RemainingDice`
           Expose the remaining dice for the active turn without letting callers mutate internal state.

        4. `bool IsTurnComplete`
           True when the active turn has no remaining dice.

        5. `bool ApplyMove(Move move)`
           Use `CurrentTurn` and `RemainingDice`; infer the move distance using the existing direction rules, validate with `MoveValidator`, apply the existing hit/bar/bearing-off behavior, consume one matching die, and switch turn only when the turn is complete.

        6. Keep the existing `ApplyMove(Move move, PlayerColor player)` overload if needed for compatibility, but route it through the new turn/dice flow or keep behavior compatible with existing tests.

        7. `void AdvanceTurn()`
           Clear remaining dice and switch `CurrentTurn` to the other player. This should be safe to call at turn completion.

        8. `IEnumerable<Move> GenerateLegalMoves(PlayerColor player)`
           Generate simple legal single-checker moves for each remaining die by scanning board points and using `MoveValidator`. Keep it conservative; do not rewrite `MoveValidator`.

        Constraints:
        - Read relevant `prodetayi/` summaries before editing.
        - Do not rewrite `Board`, `Move`, `PlayerColor`, or `MoveValidator`.
        - Keep the patch focused around 100-500 lines.
        - Add tests for dice rolling with deterministic random, doubles producing four remaining dice, invalid dice values, consuming dice after valid moves, rejecting moves that do not match remaining dice, and switching turn only after dice are consumed.
        - Run or keep compatible with `dotnet build` and `dotnet test`.
        - Do not commit or print secrets, `.env`, API keys, or database passwords.
        """;
    }

    private static string BuildSnapshotClarificationPrompt(ProjectSettings settings)
    {
        return $"""
        Continue this same Jules session for repo {settings.GitHubRepo}.

        If the snapshot capability is already implemented, finalize this session with no additional code changes and mark it complete.

        If a `CaptureGameStateSnapshot` signature is still required, use exactly this API:
        `public static GameStateSnapshot CaptureGameStateSnapshot(GameEngine engine)`

        Snapshot contents:
        - points 1..24 as immutable `PointSnapshot(index, color, checkerCount)` values
        - `WhiteCheckersOnBar`, `BlackCheckersOnBar`
        - `WhiteCheckersBorneOff`, `BlackCheckersBorneOff`
        - `CurrentTurn`
        - immutable copy of `RemainingDice`
        - `TurnNumber`

        Keep this as an engine/model task only. Do not add DB persistence in this session. Database persistence must be a later separate phase and must use the existing project stack/Aiven MySQL assumptions, not PostgreSQL or JSONB unless the repo already has that stack.

        Update relevant `prodetayi/` summaries and add one dated `yapilanlar/` note if you touch code. Do not print or commit secrets.
        """;
    }

    private static string BuildAwaitingPlanApprovalPrompt(ProjectSettings settings)
    {
        return $"""
        Plan approved. Please continue this same Jules session for repo {settings.GitHubRepo}.

        If your review says the requested engine capability already exists, finalize this session with no additional code changes and mark the task complete.

        If you still need the snapshot method signature, use exactly:
        `public static GameStateSnapshot CaptureGameStateSnapshot(GameEngine engine)`

        Keep persistence/database work out of `TavlaJules.Engine` for this approval. Any later DB phase must be a separate task and must use the existing project stack/Aiven MySQL assumptions, not PostgreSQL, JSONB, or EF Core unless those are already present in this repo.

        Update related tests, relevant `prodetayi/` summaries, and one dated `yapilanlar/` note if you touch code. Do not ask another clarification question unless a build-blocking ambiguity remains. Do not include secrets or `.env` values.
        """;
    }

    private static string BuildAwaitingInputRecoveryPrompt(ProjectSettings settings, string trackedSessionId, string sessionsOutput)
    {
        var originalDescription = TryGetSessionDescription(sessionsOutput, trackedSessionId);
        return $"""
        Jules, implement this exact TavlaJules engine task for repo {settings.GitHubRepo}. Do not ask another clarification question; if a small detail is missing, use the conservative existing engine behavior.

        Context:
        Previous session {trackedSessionId} waited for user feedback because the GameEngine task was too vague. This prompt is the clarification.

        Original vague task:
        {originalDescription}

        Goal:
        Extend `src/TavlaJules.Engine/Engine/GameEngine.cs` with a small turn orchestration layer for the existing tavla/backgammon engine.

        Scope:
        - Read the relevant `prodetayi/` summaries before editing.
        - Keep the existing `Board`, `Move`, `PlayerColor` and `MoveValidator` classes; do not rewrite them.
        - Add explicit dice state to `GameEngine`: current dice values, remaining dice/moves, and a `StartTurn(PlayerColor player, int die1, int die2)` style API.
        - Make `ApplyMove` consume a matching remaining die instead of always inferring and immediately switching turn after one move.
        - Add a `IsTurnComplete`/similar property or method and switch turn only when the current turn has no remaining dice.
        - Preserve current hit, bar and bearing-off behavior already present in `GameEngine`.
        - Keep the change around 100-500 lines and focused on engine/tests only.
        - Add or update tests in `src/TavlaJules.Engine.Tests/Engine/GameEngineTests.cs` for dice consumption, invalid dice distance, and turn switch after dice are consumed.
        - Update `prodetayi/GameEngine.md` and add one dated `yapilanlar/` note describing the completed work.
        - Run or at least keep the repo compatible with `dotnet build` and `dotnet test`.
        - Do not include secrets, .env values, API keys, or database passwords.
        """;
    }

    private string ResolveSessionObjectiveKey(ProjectSettings settings, string sessionsOutput, string sessionId)
    {
        var stateObjectiveKey = agentStateService.GetSessionObjectiveKey(settings, sessionId);
        return !string.IsNullOrWhiteSpace(stateObjectiveKey)
            ? stateObjectiveKey
            : BuildPromptObjectiveKey(TryGetSessionDescription(sessionsOutput, sessionId));
    }

    private static string SelectNextPrompt(
        ProjectSettings settings,
        string proposedPrompt,
        string sessionsOutput,
        List<AgentEvent> events)
    {
        if (string.IsNullOrWhiteSpace(proposedPrompt))
        {
            return "";
        }

        var objectiveKey = BuildPromptObjectiveKey(proposedPrompt);
        if (string.IsNullOrWhiteSpace(objectiveKey))
        {
            return proposedPrompt;
        }

        proposedPrompt = CanonicalizeNextPrompt(settings, objectiveKey, proposedPrompt, events);

        var implemented = IsPromptObjectiveImplemented(settings.ProjectFolder, objectiveKey);
        var alreadyInJules = SessionsContainObjective(sessionsOutput, objectiveKey);
        if (!implemented && !alreadyInJules)
        {
            return proposedPrompt;
        }

        var replacementPrompt = BuildNextGameImprovementPrompt(settings, objectiveKey);
        var replacementKey = BuildPromptObjectiveKey(replacementPrompt);
        if (string.IsNullOrWhiteSpace(replacementPrompt)
            || string.IsNullOrWhiteSpace(replacementKey)
            || objectiveKey.Equals(replacementKey, StringComparison.Ordinal))
        {
            events.Add(CreateEvent(
                "next_prompt_already_done_skipped",
                "warning",
                "OpenRouter uygulanmis veya Jules listesinde bulunan hedefi onerdi; uygun sonraki roadmap fazi bulunamadigi icin prompt gonderilmeyecek.",
                new { objectiveKey, replacementKey, implemented, alreadyInJules }));

            return "";
        }

        events.Add(CreateEvent(
            "next_prompt_replanned",
            "warning",
            "OpenRouter ayni veya uygulanmis oyun hedefini onerdi; ajan sonraki oyun fazina gecti.",
            new { objectiveKey, replacementKey, implemented, alreadyInJules }));

        return replacementPrompt;
    }

    private static string CanonicalizeNextPrompt(
        ProjectSettings settings,
        string objectiveKey,
        string proposedPrompt,
        List<AgentEvent> events)
    {
        if (objectiveKey.Equals("data.mysql-load-snapshot", StringComparison.Ordinal))
        {
            var loadSnapshotPrompt = BuildLoadSnapshotPrompt(settings);
            events.Add(CreateEvent(
                "next_prompt_canonicalized",
                "warning",
                "OpenRouter load-snapshot hedefini repo uyumlu TavlaJules.Data promptuna cevirdi.",
                new
                {
                    objectiveKey,
                    original = Trim(proposedPrompt, 700),
                    canonical = Trim(loadSnapshotPrompt, 700)
                }));

            return loadSnapshotPrompt;
        }

        if (!objectiveKey.Equals("data.mysql-game-persistence", StringComparison.Ordinal)
            && !objectiveKey.Equals("data.mysql-schema", StringComparison.Ordinal)
            && !objectiveKey.Equals("data.mysql-repository", StringComparison.Ordinal))
        {
            return proposedPrompt;
        }

        var canonicalPrompt = BuildNextGameImprovementPrompt(settings, "engine.game-state-snapshot");
        if (string.IsNullOrWhiteSpace(canonicalPrompt)
            || canonicalPrompt.Equals(proposedPrompt, StringComparison.Ordinal))
        {
            return proposedPrompt;
        }

        events.Add(CreateEvent(
            "next_prompt_canonicalized",
            "warning",
            "OpenRouter DB persistence hedefini genis veya repo disi onerdi; ajan mevcut TavlaJules.Data/MySQL roadmap promptuna cevirdi.",
            new
            {
                objectiveKey,
                original = Trim(proposedPrompt, 700),
                canonical = Trim(canonicalPrompt, 700)
            }));

        return canonicalPrompt;
    }

    private static string BuildNextGameImprovementPrompt(ProjectSettings settings, string skippedObjectiveKey)
    {
        if (!IsPromptObjectiveImplemented(settings.ProjectFolder, "engine.generate-legal-moves-explicit-dice"))
        {
            return $"""
            Implement the explicit dice legal move generator for repo {settings.GitHubRepo}.

            Target files:
            - `src/TavlaJules.Engine/Engine/GameEngine.cs`
            - `src/TavlaJules.Engine/Models/Move.cs`
            - `src/TavlaJules.Engine.Tests/Engine/GameEngineTests.cs`
            - `src/TavlaJules.Engine.Tests/Models/MoveTests.cs`
            - relevant `prodetayi/` summaries and one dated `yapilanlar/` note

            Requirements:
            - Add `GenerateLegalMoves(PlayerColor player, (int die1, int die2) dice): IEnumerable<Move>`.
            - Keep the existing `GenerateLegalMoves(PlayerColor player)` API.
            - Add `DiceUsed` metadata to `Move` so UI/online replay can show which die produced the legal move.
            - Respect bar priority, blocked points, hits, bearing off with larger die, and doubles.
            - Add focused tests for explicit dice, blocked points, bar entry, hit metadata, and bearing off.
            - Do not touch secrets or `.env` values.
            """;
        }

        if (!IsPromptObjectiveImplemented(settings.ProjectFolder, "engine.move-sequences"))
        {
            return $"""
            Create the next TavlaJules engine phase: full-turn legal move sequence generation for repo {settings.GitHubRepo}.

            Target files:
            - new `src/TavlaJules.Engine/Engine/MoveSequenceGenerator.cs`
            - new or updated tests under `src/TavlaJules.Engine.Tests/Engine/`
            - relevant `prodetayi/` summaries and one dated `yapilanlar/` note

            Goal:
            The current engine can list single legal moves for dice. Add a focused generator that returns ordered legal move sequences for a complete turn, so the UI/AI can choose a whole turn instead of one isolated move.

            Requirements:
            - Use existing `Board`, `Move`, `MoveValidator`, and `GameEngine.GenerateLegalMoves`; do not rewrite rule classes.
            - Support normal dice order permutations, doubles as four moves, bar-entry priority, hits, blocked points, and bearing-off.
            - Do not mutate the original board while exploring sequences; add a small internal board copy helper if needed.
            - Return a compact model such as `IReadOnlyList<IReadOnlyList<Move>>` or a small `MoveSequence` class if that is cleaner.
            - Keep the patch around 100-500 lines and add deterministic unit tests.
            - Run or keep compatible with `dotnet build` and `dotnet test`.
            - Do not include secrets, API keys, or database passwords.
            """;
        }

        if (!IsPromptObjectiveImplemented(settings.ProjectFolder, "engine.game-state-snapshot"))
        {
            return $"""
            Add a serializable game state snapshot layer for TavlaJules repo {settings.GitHubRepo}.

            Target files:
            - new model file under `src/TavlaJules.Engine/Models/`
            - focused tests under `src/TavlaJules.Engine.Tests/Models/`
            - relevant `prodetayi/` summaries and one dated `yapilanlar/` note

            Goal:
            Prepare the engine for mobile UI and online persistence by exposing a safe snapshot of board points, bars, borne-off counts, current turn, remaining dice, and winner/progress flags.

            Requirements:
            - Do not expose mutable engine internals directly.
            - Keep DTO names clear and JSON-friendly.
            - Add tests that snapshots match a fresh board and after one move.
            - Do not include secrets or `.env` values.
            """;
        }

        if (!IsPromptObjectiveImplemented(settings.ProjectFolder, "data.mysql-schema"))
        {
            return $"""
            Create the TavlaJules MySQL schema phase for repo {settings.GitHubRepo}.

            Target files:
            - new MySQL migration script under `migrations/`
            - `src/TavlaJules.Data/TavlaJules.Data.csproj`
            - relevant `prodetayi/` summaries and one dated `yapilanlar/` note

            Goal:
            Engine phases are already implemented. Prepare the Aiven MySQL `tavla_online` schema so completed turns can later persist games, snapshots, dice rolls, and generated move sequences.

            Requirements:
            - Use MySQL-compatible SQL only; do not use PostgreSQL, JSONB, or EF Core unless those are already present in this repo.
            - Add idempotent `CREATE TABLE IF NOT EXISTS` statements for `games`, `game_state_snapshots`, `move_sequences`, and `dice_rolls`.
            - Keep snapshot and sequence payloads as JSON text/LONGTEXT columns suitable for MySQL.
            - Ensure `src/TavlaJules.Data/TavlaJules.Data.csproj` uses `MySqlConnector` and does not add PostgreSQL packages.
            - Keep this patch schema-only, around 100-300 lines.
            - Do not include secrets, API keys, connection strings, or `.env` values.
            """;
        }

        if (!IsPromptObjectiveImplemented(settings.ProjectFolder, "data.mysql-repository"))
        {
            return $"""
            Create the TavlaJules MySQL repository phase for repo {settings.GitHubRepo}.

            Target files:
            - new or updated `src/TavlaJules.Data/Repositories/MySqlGameRepository.cs`
            - `src/TavlaJules.Data/Repositories/GameStateRepository.cs` only if needed for compatibility
            - focused tests under the existing test project
            - relevant `prodetayi/` summaries and one dated `yapilanlar/` note

            Goal:
            The MySQL schema/migration phase is already present. Add the repository code that persists and reads Tavla online game data without requiring EF Core, Docker, PostgreSQL, JSONB, or secrets in source.

            Requirements:
            - Use `MySqlConnector`, parameterized SQL, async methods, and the existing `IDbConnectionFactory` pattern where possible.
            - Implement methods equivalent to:
              - `Task SaveSnapshotAsync(GameStateSnapshot snapshot, string? gameId = null, CancellationToken cancellationToken = default)`
              - `Task<GameStateSnapshot?> LoadSnapshotAsync(string gameId, CancellationToken cancellationToken = default)`
              - `Task SaveMoveSequenceAsync(string gameId, MoveSequence sequence, CancellationToken cancellationToken = default)`
              - `Task SaveDiceRollAsync(string gameId, int die1, int die2, PlayerColor player, CancellationToken cancellationToken = default)`
            - Use the existing MySQL table names from `migrations/001_initial_schema.sql`: `games`, `game_state_snapshots`, `move_sequences`, and `dice_rolls`.
            - Add tests that verify SQL command text, parameters, JSON serialization, and missing-row behavior without requiring a live database.
            - Keep the patch around 100-500 lines.
            - Do not include secrets, API keys, connection strings, or `.env` values.
            """;
        }

        return "";
    }

    private static string BuildLoadSnapshotPrompt(ProjectSettings settings)
    {
        return $"""
        Create the TavlaJules MySQL load-snapshot capability for repo {settings.GitHubRepo}.

        Target files:
        - `src/TavlaJules.Data/Repositories/GameStateRepository.cs`
        - focused tests under `src/TavlaJules.Engine.Tests/`
        - relevant `prodetayi/` summaries and one dated `yapilanlar/` note

        Goal:
        The repository can already save a `GameStateSnapshot`. Add a focused load method so an online game can resume a saved snapshot.

        Requirements:
        - Add `Task<GameStateSnapshot?> LoadSnapshotAsync(string gameId, CancellationToken cancellationToken = default)` or a closely matching method to `GameStateRepository`.
        - Use parameterized MySQL-compatible SQL only.
        - Map the existing `tavla_game_snapshots` columns back into `GameStateSnapshot`, including points, bar counts, borne-off counts, current turn, remaining dice, and turn number.
        - Return `null` when no snapshot exists for the requested game id.
        - Add unit tests that verify command text/parameters and snapshot mapping without requiring a live database or Docker container.
        - Do not create a new project, do not add EF Core, do not add Docker dependencies, and do not modify `TavlaAgentService`.
        - Do not include secrets, API keys, connection strings, or `.env` values.
        """;
    }

    private static string BuildPromptObjectiveKey(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "";
        }

        var normalized = NormalizeSessionDescription(prompt);
        if (MentionsMySqlRepositoryLayer(normalized))
        {
            return "data.mysql-repository";
        }

        if (MentionsMySqlSchema(normalized))
        {
            return "data.mysql-schema";
        }

        if (MentionsLoadSnapshot(normalized))
        {
            return "data.mysql-load-snapshot";
        }

        if (normalized.Contains("tavla_online", StringComparison.Ordinal)
            || normalized.Contains("aiven mysql", StringComparison.Ordinal)
            || normalized.Contains("mysql persistence", StringComparison.Ordinal)
            || normalized.Contains("mysqlgamerepository", StringComparison.Ordinal)
            || normalized.Contains("igamerepository", StringComparison.Ordinal)
            || normalized.Contains("online persistence", StringComparison.Ordinal)
            || normalized.Contains("game_state_snapshots", StringComparison.Ordinal)
            || normalized.Contains("move_sequences", StringComparison.Ordinal)
            || normalized.Contains("dice_rolls", StringComparison.Ordinal)
            || normalized.Contains("gamestaterepository", StringComparison.Ordinal))
        {
            return "data.mysql-game-persistence";
        }

        if (normalized.Contains("game state snapshot", StringComparison.Ordinal)
            || normalized.Contains("serializable game state", StringComparison.Ordinal)
            || normalized.Contains("snapshot layer", StringComparison.Ordinal)
            || normalized.Contains("capturegamestatesnapshot", StringComparison.Ordinal)
            || normalized.Contains("exact method signature", StringComparison.Ordinal))
        {
            return "engine.game-state-snapshot";
        }

        if (normalized.Contains("rolldice", StringComparison.Ordinal)
            || normalized.Contains("startturn", StringComparison.Ordinal)
            || normalized.Contains("remainingdice", StringComparison.Ordinal)
            || normalized.Contains("advanceturn", StringComparison.Ordinal))
        {
            return "engine.turn-dice";
        }

        if (normalized.Contains("movesequence", StringComparison.Ordinal)
            || normalized.Contains("move sequence", StringComparison.Ordinal)
            || normalized.Contains("full-turn", StringComparison.Ordinal)
            || normalized.Contains("full turn", StringComparison.Ordinal)
            || normalized.Contains("complete turn", StringComparison.Ordinal))
        {
            return "engine.move-sequences";
        }

        if (normalized.Contains("generatelegalmoves", StringComparison.Ordinal)
            || normalized.Contains("generate legal moves", StringComparison.Ordinal))
        {
            return normalized.Contains("die1", StringComparison.Ordinal)
                || normalized.Contains("die2", StringComparison.Ordinal)
                || normalized.Contains("explicit dice", StringComparison.Ordinal)
                || normalized.Contains("supplied dice", StringComparison.Ordinal)
                ? "engine.generate-legal-moves-explicit-dice"
                : "engine.generate-legal-moves-current-turn";
        }

        return "";
    }

    private static bool MentionsMySqlRepositoryLayer(string normalized)
    {
        return normalized.Contains("mysql repository phase", StringComparison.Ordinal)
            || normalized.Contains("mysql persistence layer", StringComparison.Ordinal)
            || normalized.Contains("mysqlgamerepository", StringComparison.Ordinal)
            || normalized.Contains("igamerepository", StringComparison.Ordinal)
            || normalized.Contains("imovesequencerepository", StringComparison.Ordinal)
            || normalized.Contains("idicerollrepository", StringComparison.Ordinal)
            || normalized.Contains("savemovesequenceasync", StringComparison.Ordinal)
            || normalized.Contains("savedicerollasync", StringComparison.Ordinal)
            || normalized.Contains("repository code", StringComparison.Ordinal)
            || normalized.Contains("repository class", StringComparison.Ordinal)
            || normalized.Contains("repositories", StringComparison.Ordinal)
            || normalized.Contains("move_sequences", StringComparison.Ordinal)
            || normalized.Contains("dice_rolls", StringComparison.Ordinal)
            || (normalized.Contains("games", StringComparison.Ordinal)
                && normalized.Contains("game_state_snapshots", StringComparison.Ordinal)
                && normalized.Contains("repositories", StringComparison.Ordinal))
            || (normalized.Contains("persist games", StringComparison.Ordinal)
                && normalized.Contains("dice rolls", StringComparison.Ordinal));
    }

    private static bool MentionsMySqlSchema(string normalized)
    {
        return normalized.Contains("mysql schema phase", StringComparison.Ordinal)
            || normalized.Contains("migration script", StringComparison.Ordinal)
            || normalized.Contains("migration scripts", StringComparison.Ordinal)
            || normalized.Contains("create table if not exists", StringComparison.Ordinal)
            || (normalized.Contains("migrations/", StringComparison.Ordinal)
                && normalized.Contains("game_state_snapshots", StringComparison.Ordinal))
            || (normalized.Contains("schema", StringComparison.Ordinal)
                && normalized.Contains("dice_rolls", StringComparison.Ordinal));
    }

    private static bool MentionsLoadSnapshot(string normalized)
    {
        if (!(normalized.Contains("snapshot", StringComparison.Ordinal)
            && (normalized.Contains("mysql", StringComparison.Ordinal)
                || normalized.Contains("database", StringComparison.Ordinal)
                || normalized.Contains("gamestaterepository", StringComparison.Ordinal))))
        {
            return false;
        }

        return normalized.Contains("loadsnapshot", StringComparison.Ordinal)
            || normalized.Contains("load-snapshot", StringComparison.Ordinal)
            || Regex.IsMatch(normalized, @"\b(load|loaded|loading|restore|restoring|resume|resuming)\s+(a\s+|the\s+)?(saved\s+)?snapshot\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(normalized, @"\bsnapshot\s+(load|loader|loading|restore|restoration|resume|resumption)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsPromptObjectiveImplemented(string projectFolder, string objectiveKey)
    {
        var gameEnginePath = Path.Combine(projectFolder, "src", "TavlaJules.Engine", "Engine", "GameEngine.cs");
        var movePath = Path.Combine(projectFolder, "src", "TavlaJules.Engine", "Models", "Move.cs");
        var gameStateRepositoryPath = Path.Combine(projectFolder, "src", "TavlaJules.Data", "Repositories", "GameStateRepository.cs");
        var gameEngine = File.Exists(gameEnginePath) ? File.ReadAllText(gameEnginePath) : "";
        var move = File.Exists(movePath) ? File.ReadAllText(movePath) : "";
        var gameStateRepository = File.Exists(gameStateRepositoryPath) ? File.ReadAllText(gameStateRepositoryPath) : "";

        return objectiveKey switch
        {
            "engine.generate-legal-moves-explicit-dice" =>
                Regex.IsMatch(gameEngine, @"GenerateLegalMoves\s*\(\s*PlayerColor\s+player\s*,\s*\(int\s+die1,\s*int\s+die2\)\s+dice\s*\)", RegexOptions.IgnoreCase)
                && move.Contains("DiceUsed", StringComparison.Ordinal),
            "engine.generate-legal-moves-current-turn" =>
                Regex.IsMatch(gameEngine, @"GenerateLegalMoves\s*\(\s*PlayerColor\s+player\s*\)", RegexOptions.IgnoreCase),
            "engine.turn-dice" =>
                gameEngine.Contains("RollDice", StringComparison.Ordinal)
                && gameEngine.Contains("StartTurn", StringComparison.Ordinal)
                && gameEngine.Contains("RemainingDice", StringComparison.Ordinal)
                && gameEngine.Contains("AdvanceTurn", StringComparison.Ordinal),
            "engine.move-sequences" =>
                File.Exists(Path.Combine(projectFolder, "src", "TavlaJules.Engine", "Engine", "MoveSequenceGenerator.cs"))
                || (gameEngine.Contains("GenerateLegalMoveSequences", StringComparison.Ordinal)
                    && gameEngine.Contains("GenerateSequencesRecursive", StringComparison.Ordinal)
                    && gameEngine.Contains("maxDicePlayed", StringComparison.Ordinal)),
            "engine.game-state-snapshot" =>
                Directory.Exists(Path.Combine(projectFolder, "src", "TavlaJules.Engine", "Models"))
                && Directory.GetFiles(Path.Combine(projectFolder, "src", "TavlaJules.Engine", "Models"), "*Snapshot*.cs").Length > 0,
            "data.mysql-schema" =>
                HasMySqlPersistenceMigration(projectFolder)
                && HasMySqlConnectorDependency(projectFolder),
            "data.mysql-repository" =>
                HasMySqlGameRepositoryLayer(projectFolder),
            "data.mysql-game-persistence" =>
                HasMySqlPersistenceMigration(projectFolder)
                && HasMySqlGameRepositoryLayer(projectFolder),
            "data.mysql-load-snapshot" =>
                gameStateRepository.Contains("LoadSnapshotAsync", StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool HasMySqlConnectorDependency(string projectFolder)
    {
        var dataProjectPath = Path.Combine(projectFolder, "src", "TavlaJules.Data", "TavlaJules.Data.csproj");
        if (!File.Exists(dataProjectPath))
        {
            return false;
        }

        var csproj = File.ReadAllText(dataProjectPath);
        return csproj.Contains("MySqlConnector", StringComparison.Ordinal)
            && !csproj.Contains("Npgsql", StringComparison.Ordinal);
    }

    private static bool HasMySqlGameRepositoryLayer(string projectFolder)
    {
        var repositoriesPath = Path.Combine(projectFolder, "src", "TavlaJules.Data", "Repositories");
        if (!Directory.Exists(repositoriesPath))
        {
            return false;
        }

        var repositoryCode = string.Join(
            Environment.NewLine,
            Directory.GetFiles(repositoriesPath, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        return repositoryCode.Contains("SaveSnapshotAsync", StringComparison.Ordinal)
            && repositoryCode.Contains("LoadSnapshotAsync", StringComparison.Ordinal)
            && repositoryCode.Contains("SaveMoveSequenceAsync", StringComparison.Ordinal)
            && repositoryCode.Contains("SaveDiceRollAsync", StringComparison.Ordinal)
            && repositoryCode.Contains("game_state_snapshots", StringComparison.Ordinal)
            && repositoryCode.Contains("move_sequences", StringComparison.Ordinal)
            && repositoryCode.Contains("dice_rolls", StringComparison.Ordinal);
    }

    private static bool HasMySqlPersistenceMigration(string projectFolder)
    {
        var migrationsPath = Path.Combine(projectFolder, "migrations");
        if (!Directory.Exists(migrationsPath))
        {
            return false;
        }

        return Directory.GetFiles(migrationsPath, "*.sql", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Any(sql =>
                sql.Contains("CREATE TABLE IF NOT EXISTS", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("games", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("game_state_snapshots", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("move_sequences", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("dice_rolls", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCompletedPatchWrongTarget(string objectiveKey, string diff)
    {
        if (string.IsNullOrWhiteSpace(diff)
            || !(objectiveKey.StartsWith("data.", StringComparison.Ordinal)
                || objectiveKey.StartsWith("online.", StringComparison.Ordinal)))
        {
            return false;
        }

        var changedFiles = ExtractChangedFilesFromDiff(diff);
        if (changedFiles.Count == 0)
        {
            return false;
        }

        var touchesCorePersistenceTarget = changedFiles.Any(file =>
            file.StartsWith("src/TavlaJules.Data/", StringComparison.Ordinal)
            || file.StartsWith("migrations/", StringComparison.Ordinal)
            || file.StartsWith("src/TavlaJules.Engine.Tests/", StringComparison.Ordinal)
            || file.StartsWith("tests/", StringComparison.Ordinal));

        return !touchesCorePersistenceTarget;
    }

    private static IReadOnlyList<string> ExtractChangedFilesFromDiff(string diff)
    {
        return Regex.Matches(diff, @"^diff --git a/(?<left>.+?) b/(?<right>.+?)$", RegexOptions.Multiline)
            .Select(match => match.Groups["right"].Value.Trim())
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool HasJulesPatchDiff(CommandResult result)
    {
        return result.IsSuccess
            && Regex.IsMatch(result.Output, @"^diff --git ", RegexOptions.Multiline);
    }

    private static bool SessionsContainObjective(string sessionsOutput, string objectiveKey)
    {
        if (string.IsNullOrWhiteSpace(objectiveKey))
        {
            return false;
        }

        return sessionsOutput
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(IsActiveOrPendingSessionLine)
            .Select(line => BuildPromptObjectiveKey(ExtractSessionDescriptionFromLine(line)))
            .Any(sessionObjective => ObjectiveKeysMatch(sessionObjective, objectiveKey));
    }

    private static bool IsActiveOrPendingSessionLine(string line)
    {
        return !Regex.IsMatch(line, @"\b(Completed|Needs\s+review|Archived)\b", RegexOptions.IgnoreCase);
    }

    private static bool ObjectiveKeysMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (left.Equals(right, StringComparison.Ordinal))
        {
            return true;
        }

        if ((left.Equals("data.mysql-game-persistence", StringComparison.Ordinal)
                && (right.Equals("data.mysql-schema", StringComparison.Ordinal)
                    || right.Equals("data.mysql-repository", StringComparison.Ordinal)))
            || (right.Equals("data.mysql-game-persistence", StringComparison.Ordinal)
                && (left.Equals("data.mysql-schema", StringComparison.Ordinal)
                    || left.Equals("data.mysql-repository", StringComparison.Ordinal))))
        {
            return true;
        }

        return left.StartsWith("engine.generate-legal-moves", StringComparison.Ordinal)
            && right.StartsWith("engine.generate-legal-moves", StringComparison.Ordinal);
    }

    private static bool ShouldContinueCompletedSessionInPlace(string completedObjectiveKey, string nextObjectiveKey, string prompt)
    {
        if (string.IsNullOrWhiteSpace(completedObjectiveKey)
            || string.IsNullOrWhiteSpace(nextObjectiveKey))
        {
            return false;
        }

        if (ObjectiveKeysMatch(completedObjectiveKey, nextObjectiveKey))
        {
            return true;
        }

        if (completedObjectiveKey.StartsWith("engine.", StringComparison.Ordinal)
            && nextObjectiveKey.StartsWith("engine.", StringComparison.Ordinal)
            && PromptTargetsEngineOnly(prompt))
        {
            return true;
        }

        return false;
    }

    private static bool PromptTargetsEngineOnly(string prompt)
    {
        var normalized = NormalizeSessionDescription(prompt);
        var mentionsEngine = normalized.Contains("tavlajules.engine", StringComparison.Ordinal)
            || normalized.Contains("gameengine", StringComparison.Ordinal)
            || normalized.Contains("movevalidator", StringComparison.Ordinal)
            || normalized.Contains("movesequence", StringComparison.Ordinal)
            || normalized.Contains("game state snapshot", StringComparison.Ordinal)
            || normalized.Contains("src/tavlajules.engine", StringComparison.Ordinal);

        var mentionsSeparateSurface = normalized.Contains("winforms", StringComparison.Ordinal)
            || normalized.Contains("tavlajules.app", StringComparison.Ordinal)
            || normalized.Contains("database", StringComparison.Ordinal)
            || normalized.Contains("mysql", StringComparison.Ordinal)
            || normalized.Contains("sql", StringComparison.Ordinal)
            || normalized.Contains("dashboard", StringComparison.Ordinal)
            || normalized.Contains("ajanlarim", StringComparison.Ordinal)
            || normalized.Contains("tavla_online", StringComparison.Ordinal);

        return mentionsEngine && !mentionsSeparateSurface;
    }

    private static string BuildCompletedSessionContinuationPrompt(string prompt)
    {
        return $"""
        Continue from the completed work above in this same Jules session.

        Next connected task:
        {prompt}

        Keep this continuation tightly related to the files and architecture already discussed in this session. If the task is already implemented, say so clearly and finalize with no code changes. Do not include secrets or `.env` values.
        """;
    }

    private static bool IsTrackedSessionBusy(
        string sessionsOutput,
        string trackedSessionId,
        AgentAutomationArtifacts automation)
    {
        if (string.IsNullOrWhiteSpace(trackedSessionId)
            || automation.TrackedSessionCompleted
            || automation.TrackedSessionAwaitingInput)
        {
            return false;
        }

        var line = FindSessionLine(sessionsOutput, trackedSessionId);
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return !Regex.IsMatch(line, @"\b(Completed|Needs\s+review)\b", RegexOptions.IgnoreCase);
    }

    private static string BuildGameRoadmapSummary(string projectFolder)
    {
        var checks = new Dictionary<string, string>
        {
            ["turnDice"] = IsPromptObjectiveImplemented(projectFolder, "engine.turn-dice") ? "done" : "missing",
            ["singleLegalMoves"] = IsPromptObjectiveImplemented(projectFolder, "engine.generate-legal-moves-current-turn") ? "done" : "missing",
            ["explicitDiceLegalMoves"] = IsPromptObjectiveImplemented(projectFolder, "engine.generate-legal-moves-explicit-dice") ? "done" : "missing",
            ["fullTurnMoveSequences"] = IsPromptObjectiveImplemented(projectFolder, "engine.move-sequences") ? "done" : "missing",
            ["gameStateSnapshot"] = IsPromptObjectiveImplemented(projectFolder, "engine.game-state-snapshot") ? "done" : "missing",
            ["mysqlSchema"] = IsPromptObjectiveImplemented(projectFolder, "data.mysql-schema") ? "done" : "missing",
            ["mysqlRepository"] = IsPromptObjectiveImplemented(projectFolder, "data.mysql-repository") ? "done" : "missing",
            ["mysqlGamePersistence"] = IsPromptObjectiveImplemented(projectFolder, "data.mysql-game-persistence") ? "done" : "missing"
        };

        return string.Join(Environment.NewLine, checks.Select(item => $"{item.Key}={item.Value}"));
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
        Jules Awaiting User Feedback/Input durumundaysa bunu bekleyen soru olarak raporla; tavlajules bu durumda netlestirilmis kurtarma promptu uretebilir.
        Completed is apply/dogrulama/commit/push ile guvenli hale geldiyse nextPrompt bir sonraki kucuk faz olsun.
        Ayni methodu veya ayni hedefi farkli cumlelerle tekrar onerme. OYUN FAZ DURUMU icinde done gorunen hedefler icin nextPrompt yazma.
        GenerateLegalMoves ve dice/turn isleri done ise sonraki dogru oyun fazi full-turn move sequence generation, sonra game state snapshot, sonra MySQL schema, sonra MySQL repository persistence olmalidir.
        nextPrompt mutlaka dosya/modul bazli, test/dogrulama beklentili, 100-500 satir bandinda ve mevcut prodetayi/yapilanlar disiplinine uygun olsun.
        Proje, kullanicinin daha once yaptigi Batak projesine benzer sekilde fazli, loglu, prodetayi hafizali ve Jules destekli ilerlemelidir.
        Batak yalnizca surec disiplini ornegidir; cevapta Batak, FAZ 95 veya baska eski proje icerigi yazma.
        Konu sadece TavlaJules, tavla oyunu, Jules sessionlari ve ajanlarim SQL raporlamasidir.
        Gizli anahtar, connection string veya .env icerigini asla tekrar etme.
        DB gercegi: kullanici Aiven MySQL kullanir. `ajanlarim` ajan raporlari ve `tavla_online` oyun DB planlari MySQL uyumlu olmali; PostgreSQL, JSONB veya EF Core onermeyeceksin unless repo zaten bu stack'i acikca kullaniyorsa.
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
            statusSummary = automation.TrackedSessionAwaitingInput
                ? "OpenRouter kullanilamadi; izlenen Jules session kullanici girdisi bekliyor ve yerel kurtarma akisi devreye alinabilir."
                : "OpenRouter free modelleri gecici olarak kullanilamadi; tavlajules yerel durum raporu yazdi ve yeni Jules promptu gondermedi.",
            whatJulesDid = automation.TrackedSessionAwaitingInput
                ? "Izlenen Jules session kullanici girdisi bekliyor; tavlajules netlestirilmis yeni gorev hazirlayacak."
                : sessionsOutput.Contains("In Progress", StringComparison.OrdinalIgnoreCase)
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

        OYUN FAZ DURUMU:
        {BuildGameRoadmapSummary(settings.ProjectFolder)}

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
            $"trackedAwaitingInput={automation.TrackedSessionAwaitingInput}",
            $"trackedAwaitingPlanApproval={automation.TrackedSessionAwaitingPlanApproval}",
            $"awaitingInputAlreadyHandled={automation.AwaitingInputAlreadyHandled}",
            $"awaitingInputRecoveryStarted={automation.AwaitingInputRecoveryStarted}",
            $"awaitingInputRecoverySession={automation.AwaitingInputRecoverySession}",
            $"awaitingInputReplySent={automation.AwaitingInputReplySent}",
            $"awaitingPlanApprovalSent={automation.AwaitingPlanApprovalSent}",
            $"awaitingPlanApprovalAlreadySent={automation.AwaitingPlanApprovalAlreadySent}",
            $"awaitingPlanApprovalRetryDue={automation.AwaitingPlanApprovalRetryDue}",
            $"completedSessionContinuedInPlace={automation.CompletedSessionContinuedInPlace}",
            $"completedSessionOpenedNewSession={automation.CompletedSessionOpenedNewSession}",
            $"completedContinuationRelated={automation.CompletedContinuationRelated}",
            $"awaitingInputSessionIds={string.Join(",", automation.AwaitingInputSessionIds)}",
            $"awaitingPlanSessionIds={string.Join(",", automation.AwaitingPlanSessionIds)}",
            $"appliedThisTurn={automation.AppliedThisTurn}",
            $"alreadyApplied={automation.AlreadyApplied}",
            $"duplicateCompletedSession={automation.DuplicateCompletedSession}",
            $"resumedDirtyWorkspace={automation.ResumedDirtyWorkspace}",
            $"blocked={automation.AutomationBlocked}"
        };

        AddCommandLine(lines, "gitStatusBefore", automation.GitStatusBeforeApply);
        AddCommandLine(lines, "julesPull", automation.JulesPullResult);
        AddCommandLine(lines, "julesApply", automation.JulesApplyResult);
        AddCommandLine(lines, "completedContinuation", automation.CompletedContinuationResult);
        foreach (var replyResult in automation.AwaitingInputReplyResults)
        {
            AddCommandLine(lines, "julesReply", replyResult);
        }

        foreach (var approvalResult in automation.AwaitingPlanApprovalResults)
        {
            AddCommandLine(lines, "julesPlanApproval", approvalResult);
        }

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

        if (automation.CompletedSessionContinuedInPlace)
        {
            return "Completed Jules session sonrasi baglantili prompt ayni session'in altina yazildi.";
        }

        if (automation.CompletedSessionOpenedNewSession)
        {
            return "Completed Jules session sonrasi prompt baglantisiz goruldu; yeni Jules session acildi.";
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
