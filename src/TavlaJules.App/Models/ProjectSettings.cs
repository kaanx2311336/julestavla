namespace TavlaJules.App.Models;

public sealed class ProjectSettings
{
    public string ProjectFolder { get; set; } = @"C:\Users\PC\Desktop\projeler\tavlajules";
    public string GitHubRepo { get; set; } = "kaanx2311336/julestavla";
    public string JulesUrl { get; set; } = "https://jules.google.com";
    public string JulesCommand { get; set; } = "jules.cmd";
    public string OpenRouterModel { get; set; } = "openai/gpt-oss-120b:free";
    public string OpenRouterFallbackModels { get; set; } = "google/gemma-3n-e2b-it:free,qwen/qwen3-coder:free";
    public string OpenRouterEndpoint { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
    public string Goal { get; set; } =
        "Jules ile telefon icin tavla uygulamasi yaptiran, OpenRouter uzerinden AI kontrol raporu alan ve fazlari takip eden masaustu kontrol paneli.";
}
