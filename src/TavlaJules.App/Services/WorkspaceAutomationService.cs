using System.Diagnostics;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class WorkspaceAutomationService
{
    public Task<CommandResult> GetGitStatusAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, "git", "status --porcelain", 30, cancellationToken);
    }

    public Task<CommandResult> BuildAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, "dotnet", @"build .\TavlaJules.sln -c Release", 180, cancellationToken);
    }

    public Task<CommandResult> TestAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, "dotnet", @"test .\TavlaJules.sln -c Release --no-build", 180, cancellationToken);
    }

    public Task<CommandResult> StageAllAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, "git", "add -A", 60, cancellationToken);
    }

    public Task<CommandResult> SecretScanAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        var aivenPrefix = "AV" + "NS_";
        var mysqlPrefix = "mysql://avn" + "admin";
        var openRouterPrefix = "sk-" + "or";
        var pattern = $"{aivenPrefix}\\|{mysqlPrefix}\\|{openRouterPrefix}";
        return RunAsync(settings.ProjectFolder, "git", $"grep --cached -n {Quote(pattern)}", 30, cancellationToken);
    }

    public Task<CommandResult> CommitAsync(ProjectSettings settings, string sessionId, CancellationToken cancellationToken = default)
    {
        var message = $"Auto-apply Jules session {sessionId}";
        return CommitWithMessageAsync(settings, message, cancellationToken);
    }

    public Task<CommandResult> CommitWithMessageAsync(ProjectSettings settings, string message, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, "git", $"commit -m {Quote(message)}", 120, cancellationToken);
    }

    public Task<CommandResult> PushAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, "git", "push origin main", 180, cancellationToken);
    }

    public static bool IsCleanStatus(CommandResult status)
    {
        return status.IsSuccess && string.IsNullOrWhiteSpace(status.Output) && string.IsNullOrWhiteSpace(status.Error);
    }

    public static bool SecretScanPassed(CommandResult scan)
    {
        return scan.ExitCode == 1;
    }

    public static bool HasNothingToCommit(CommandResult commit)
    {
        var text = $"{commit.Output}{Environment.NewLine}{commit.Error}";
        return text.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CommandResult> RunAsync(
        string workingDirectory,
        string fileName,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

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

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new CommandResult
                {
                    ExitCode = -1,
                    Error = $"{fileName} {arguments} zaman asimina ugradi ({timeoutSeconds} saniye)."
                };
            }

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (Exception exception)
        {
            return new CommandResult
            {
                ExitCode = -1,
                Error = exception.Message
            };
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup after a timeout.
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
