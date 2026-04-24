using System.Text.Json;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class ProjectSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string settingsPath;

    public ProjectSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        settingsPath = Path.Combine(appData, "TavlaJules", "settings.json");
    }

    public string SettingsPath => settingsPath;

    public ProjectSettings Load()
    {
        if (!File.Exists(settingsPath))
        {
            return new ProjectSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return Normalize(JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions) ?? new ProjectSettings());
        }
        catch
        {
            return new ProjectSettings();
        }
    }

    public void Save(ProjectSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(settingsPath, json);
    }

    private static ProjectSettings Normalize(ProjectSettings settings)
    {
        settings.OpenRouterFallbackModels = string.Join(
            ',',
            settings.OpenRouterFallbackModels
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(model => !model.Equals("qwen/qwen3-coder:free", StringComparison.OrdinalIgnoreCase)));

        if (string.IsNullOrWhiteSpace(settings.OpenRouterFallbackModels))
        {
            settings.OpenRouterFallbackModels = "google/gemma-3n-e2b-it:free";
        }

        return settings;
    }
}
