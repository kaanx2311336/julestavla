using System.Diagnostics;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class JulesCliService
{
    public Task<CommandResult> VersionAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, settings.JulesCommand, "version", cancellationToken);
    }

    public Task<CommandResult> CreateSessionAsync(ProjectSettings settings, string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("Jules gorevi icin prompt bos olamaz.");
        }

        var arguments = $"new --repo {Quote(settings.GitHubRepo)} {Quote(prompt)}";
        return RunAsync(settings.ProjectFolder, settings.JulesCommand, arguments, cancellationToken);
    }

    private static async Task<CommandResult> RunAsync(string workingDirectory, string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            Output = await outputTask,
            Error = await errorTask
        };
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
