using System.Text.Json;
using TavlaJules.Engine.Models;
using Xunit;

namespace TavlaJules.Engine.Tests;

public class GameStateSnapshotTests
{
    [Fact]
    public void GameStateSnapshot_Serialization_Deserialization_Works()
    {
        // Arrange
        var points = new List<PointSnapshot>
        {
            new PointSnapshot(1, PlayerColor.White, 2),
            new PointSnapshot(24, PlayerColor.Black, 2)
        };

        var snapshot = new GameStateSnapshot(
            points.AsReadOnly(),
            WhiteCheckersOnBar: 1,
            BlackCheckersOnBar: 0,
            WhiteCheckersBorneOff: 2,
            BlackCheckersBorneOff: 0,
            CurrentTurn: PlayerColor.White,
            RemainingDice: new List<int> { 3, 4 }.AsReadOnly(),
            TurnNumber: 5
        );

        // Act
        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<GameStateSnapshot>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(snapshot.WhiteCheckersOnBar, deserialized.WhiteCheckersOnBar);
        Assert.Equal(snapshot.CurrentTurn, deserialized.CurrentTurn);
        Assert.Equal(snapshot.TurnNumber, deserialized.TurnNumber);
        Assert.Equal(2, deserialized.Points.Count);
        Assert.Equal(PlayerColor.White, deserialized.Points[0].Color);
    }
}
