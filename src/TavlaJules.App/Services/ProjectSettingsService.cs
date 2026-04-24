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
            return JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions) ?? new ProjectSettings();
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
}
