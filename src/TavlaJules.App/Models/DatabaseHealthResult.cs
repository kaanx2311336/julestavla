namespace TavlaJules.App.Models;

public sealed class DatabaseHealthResult
{
    public bool IsConfigured { get; init; }
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = "";
    public int? TableCount { get; init; }
}
