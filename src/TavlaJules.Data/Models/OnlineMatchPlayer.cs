using System;

namespace TavlaJules.Data.Models;

public class OnlineMatchPlayer
{
    public string Id { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string ColorAssignment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
