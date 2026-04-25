using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class JulesCliService
{
    private const string JulesApiBaseUrl = "https://aida.googleapis.com/v1/swebot";

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

    public Task<CommandResult> ListSessionsAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        return RunAsync(settings.ProjectFolder, settings.JulesCommand, "remote list --session", cancellationToken);
    }

    public Task<CommandResult> PullSessionAsync(ProjectSettings settings, string sessionId, bool apply, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Pull icin Jules session ID bos olamaz.");
        }

        var arguments = $"remote pull --session {Quote(sessionId)}";
        if (apply)
        {
            arguments += " --apply";
        }

        return RunAsync(settings.ProjectFolder, settings.JulesCommand, arguments, cancellationToken);
    }

    public async Task<CommandResult> ReplyToSessionAsync(ProjectSettings settings, string sessionId, string feedback, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Jules cevap icin session ID bos olamaz.");
        }

        if (string.IsNullOrWhiteSpace(feedback))
        {
            throw new InvalidOperationException("Jules cevap metni bos olamaz.");
        }

        try
        {
            var token = ReadJulesAccessToken();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TavlaJules/1.0");

            var endpoint = $"{JulesApiBaseUrl}/tasks/{Uri.EscapeDataString(sessionId)}:interact";
            var attempts = new[]
            {
                new
                {
                    name = "feedbackGiven",
                    body = (object)new
                    {
                        taskId = sessionId,
                        userActivity = new
                        {
                            feedbackGiven = new
                            {
                                feedback
                            }
                        }
                    }
                },
                new
                {
                    name = "freeFormText",
                    body = (object)new
                    {
                        taskId = sessionId,
                        userActivity = new
                        {
                            freeFormText = feedback
                        }
                    }
                }
            };

            var errors = new List<string>();
            foreach (var attempt in attempts)
            {
                var payload = JsonSerializer.Serialize(attempt.body);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(endpoint, content, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return new CommandResult
                    {
                        ExitCode = 0,
                        Output = $"Jules feedback gonderildi: session={sessionId}; mode={attempt.name}",
                        Error = ""
                    };
                }

                errors.Add($"{attempt.name}: {(int)response.StatusCode} {response.ReasonPhrase} {TrimForCommand(responseText, 900)}");

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    break;
                }
            }

            return new CommandResult
            {
                ExitCode = 1,
                Output = "",
                Error = "Jules feedback gonderilemedi. " + string.Join(" | ", errors)
            };
        }
        catch (Exception exception)
        {
            return new CommandResult
            {
                ExitCode = 1,
                Output = "",
                Error = $"Jules feedback gonderilemedi: {exception.Message}"
            };
        }
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

    private static string ReadJulesAccessToken()
    {
        var json = WindowsCredentialReader.ReadCredentialBlob("jules-cli:default");
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Windows Credential Manager icinde jules-cli:default tokeni bulunamadi.");
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("access_token", out var accessToken))
        {
            throw new InvalidOperationException("Jules credential icinde access_token yok.");
        }

        return accessToken.GetString() ?? throw new InvalidOperationException("Jules access_token bos.");
    }

    private static string TrimForCommand(string value, int maxChars)
    {
        value = value.Trim();
        return value.Length <= maxChars ? value : value[..maxChars] + "...";
    }

    private static class WindowsCredentialReader
    {
        private const int CredentialTypeGeneric = 1;

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        public static string ReadCredentialBlob(string target)
        {
            if (!CredRead(target, CredentialTypeGeneric, 0, out var credentialPtr))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
                if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
                {
                    return "";
                }

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);

                var unicode = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                if (unicode.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    return unicode;
                }

                return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }
    }
}
