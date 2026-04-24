namespace TavlaJules.App.Services;

public sealed class EnvFileService
{
    public string GetEnvPath(string projectFolder)
    {
        return Path.Combine(projectFolder, ".env");
    }

    public string? GetValue(string projectFolder, string key)
    {
        var values = Read(projectFolder);
        return values.TryGetValue(key, out var value) ? value : null;
    }

    public bool HasValue(string projectFolder, string key)
    {
        return !string.IsNullOrWhiteSpace(GetValue(projectFolder, key));
    }

    public void UpsertValues(string projectFolder, IReadOnlyDictionary<string, string> values)
    {
        Directory.CreateDirectory(projectFolder);
        var path = GetEnvPath(projectFolder);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];

        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            var prefix = pair.Key + "=";
            var existingIndex = lines.FindIndex(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var newLine = $"{pair.Key}={pair.Value}";

            if (existingIndex >= 0)
            {
                lines[existingIndex] = newLine;
            }
            else
            {
                lines.Add(newLine);
            }
        }

        File.WriteAllLines(path, lines);
    }

    private Dictionary<string, string> Read(string projectFolder)
    {
        var path = GetEnvPath(projectFolder);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            values[key] = value;
        }

        return values;
    }
}
