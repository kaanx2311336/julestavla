using TavlaJules.Data.Models;
using TavlaJules.Data.Repositories;

namespace TavlaJules.App.Services;

public sealed class MatchmakingService(OnlineMatchRepository repository)
{
    public async Task<string> CreateMatchAsync(
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        var matchId = await repository.CreateMatchAsync(cancellationToken);
        var joined = await repository.JoinMatchAsync(matchId, playerId, playerName, "White", cancellationToken);
        if (!joined)
        {
            throw new InvalidOperationException("Yeni mac olusturuldu ama oyuncu kaydi eklenemedi.");
        }

        return matchId;
    }

    public Task<IReadOnlyList<OnlineMatch>> ListOpenMatchesAsync(CancellationToken cancellationToken = default)
    {
        return repository.ListOpenMatchesAsync(cancellationToken);
    }

    public Task<bool> JoinMatchAsync(
        string matchId,
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        return repository.JoinMatchAsync(matchId, playerId, playerName, "Black", cancellationToken);
    }

    public Task UpdateMatchStatusAsync(
        string matchId,
        string status,
        string? currentSnapshotId = null,
        CancellationToken cancellationToken = default)
    {
        return repository.UpdateMatchStatusAsync(matchId, status, currentSnapshotId, cancellationToken);
    }
}
