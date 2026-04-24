using Xunit;
using TavlaJules.Engine.Models;

namespace TavlaJules.Engine.Tests.Models;

public class MoveTests
{
    [Fact]
    public void Move_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        int source = 1;
        int destination = 6;
        int count = 1;
        bool isHit = true;

        // Act
        var move = new Move(source, destination, count, isHit);

        // Assert
        Assert.Equal(source, move.SourcePoint);
        Assert.Equal(destination, move.DestinationPoint);
        Assert.Equal(count, move.CheckerCount);
        Assert.Equal(isHit, move.IsHit);
    }

    [Fact]
    public void Move_Constructor_IsHitDefaultsToFalse()
    {
        // Arrange
        int source = 24;
        int destination = 18;
        int count = 2;

        // Act
        var move = new Move(source, destination, count);

        // Assert
        Assert.False(move.IsHit);
    }
}
