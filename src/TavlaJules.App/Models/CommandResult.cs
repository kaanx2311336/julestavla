namespace TavlaJules.App.Models;

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
    public bool IsSuccess => ExitCode == 0;
}
