namespace TavlaJules.App.Models;

public sealed class PhaseItem
{
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsDone { get; set; }
}
