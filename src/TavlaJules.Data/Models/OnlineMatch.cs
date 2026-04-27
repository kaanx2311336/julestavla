using System;

namespace TavlaJules.Data.Models;

public class OnlineMatch
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = "WaitingForPlayers";
    public string? CurrentSnapshotId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
