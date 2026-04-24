namespace TavlaJules.App.Models;

public sealed class AgentAutomationArtifacts
{
    public bool TrackedSessionCompleted { get; set; }
    public bool AppliedThisTurn { get; set; }
    public bool AlreadyApplied { get; set; }
    public bool DuplicateCompletedSession { get; set; }
    public bool ResumedDirtyWorkspace { get; set; }
    public bool TrackedSessionAwaitingInput { get; set; }
    public bool AwaitingInputAlreadyHandled { get; set; }
    public bool AwaitingInputRecoveryStarted { get; set; }
    public bool AwaitingInputRecoverySession { get; set; }
    public bool AwaitingInputReplySent { get; set; }
    public bool AwaitingPlanApprovalSent { get; set; }
    public bool AutomationBlocked { get; set; }
    public string Summary { get; set; } = "";
    public string DuplicateOfSessionId { get; set; } = "";
    public string AwaitingInputRecoveryPrompt { get; set; } = "";
    public string AwaitingInputReplyPrompt { get; set; } = "";
    public string AwaitingPlanApprovalPrompt { get; set; } = "";
    public List<string> AwaitingInputSessionIds { get; set; } = [];
    public List<string> AwaitingPlanSessionIds { get; set; } = [];
    public List<CommandResult> AwaitingInputReplyResults { get; set; } = [];
    public List<CommandResult> AwaitingPlanApprovalResults { get; set; } = [];
    public CommandResult? GitStatusBeforeApply { get; set; }
    public CommandResult? JulesPullResult { get; set; }
    public CommandResult? JulesApplyResult { get; set; }
    public CommandResult? VerificationBuildResult { get; set; }
    public CommandResult? VerificationTestResult { get; set; }
    public CommandResult? GitStageResult { get; set; }
    public CommandResult? SecretScanResult { get; set; }
    public CommandResult? GitCommitResult { get; set; }
    public CommandResult? GitPushResult { get; set; }
    public CommandResult? GitStatusAfterAutomation { get; set; }
}
