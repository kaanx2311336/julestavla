namespace TavlaJules.App.Models;

public sealed class ProjectSettings
{
    public string AgentName { get; set; } = "tavlajules";
    public string ProjectFolder { get; set; } = @"C:\Users\PC\Desktop\projeler\tavlajules";
    public string GitHubRepo { get; set; } = "kaanx2311336/julestavla";
    public string JulesUrl { get; set; } = "https://jules.google.com";
    public string JulesCommand { get; set; } = "jules.cmd";
    public string OpenRouterModel { get; set; } = "openai/gpt-oss-120b:free";
    public string OpenRouterFallbackModels { get; set; } = "google/gemma-3n-e2b-it:free";
    public string OpenRouterEndpoint { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
    public string AgentModel { get; set; } = "openai/gpt-oss-120b:free";
    public int AgentIntervalSeconds { get; set; } = 60;
    public string TrackedJulesSessionId { get; set; } = "14009672719483814558";
    public bool AllowAutoJulesSessions { get; set; } = true;
    public bool AutoStartAgent { get; set; } = true;
    public bool AutoContinueCompletedSessions { get; set; } = true;
    public bool AutoReplyAwaitingInputSessions { get; set; } = true;
    public bool AutoRecoverAwaitingInputSessions { get; set; } = true;
    public bool AutoApplyCompletedSessionPatch { get; set; } = true;
    public bool AutoCommitAndPushAppliedChanges { get; set; } = true;
    public bool AutoRunVerification { get; set; } = true;
    public string Goal { get; set; } =
        "Jules ile telefon icin tavla uygulamasi yaptiran, OpenRouter uzerinden AI kontrol raporu alan ve fazlari takip eden masaustu kontrol paneli.";
}
