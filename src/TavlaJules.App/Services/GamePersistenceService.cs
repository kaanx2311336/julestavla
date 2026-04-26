using TavlaJules.Data.Repositories;
using TavlaJules.Engine.Models;

namespace TavlaJules.App.Services;

public sealed class GamePersistenceService(MySqlGameRepository repository)
{
    public Task SaveSnapshotAsync(GameStateSnapshot snapshot, string gameId, CancellationToken cancellationToken = default)
    {
        return repository.SaveSnapshotAsync(snapshot, gameId, cancellationToken);
    }

    public Task<GameStateSnapshot?> LoadSnapshotAsync(string gameId, CancellationToken cancellationToken = default)
    {
        return repository.LoadSnapshotAsync(gameId, cancellationToken);
    }

    public Task SaveMoveSequenceAsync(string gameId, IEnumerable<Move> sequence, CancellationToken cancellationToken = default)
    {
        return repository.SaveMoveSequenceAsync(gameId, sequence, cancellationToken);
    }

    public Task SaveDiceRollAsync(string gameId, int die1, int die2, PlayerColor player, CancellationToken cancellationToken = default)
    {
        return repository.SaveDiceRollAsync(gameId, die1, die2, player, cancellationToken);
    }
}
