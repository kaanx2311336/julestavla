using Xunit;
using TavlaJules.Engine.Models;

namespace TavlaJules.Engine.Tests.Models;

public class BoardTests
{
    [Fact]
    public void Board_Constructor_InitializesCorrectStartingPosition()
    {
        // Arrange
        var board = new Board();

        // Act & Assert - White check
        Assert.Equal(PlayerColor.White, board.Points[1].Color);
        Assert.Equal(2, board.Points[1].CheckerCount);

        Assert.Equal(PlayerColor.White, board.Points[12].Color);
        Assert.Equal(5, board.Points[12].CheckerCount);

        Assert.Equal(PlayerColor.White, board.Points[17].Color);
        Assert.Equal(3, board.Points[17].CheckerCount);

        Assert.Equal(PlayerColor.White, board.Points[19].Color);
        Assert.Equal(5, board.Points[19].CheckerCount);

        // Act & Assert - Black check
        Assert.Equal(PlayerColor.Black, board.Points[24].Color);
        Assert.Equal(2, board.Points[24].CheckerCount);

        Assert.Equal(PlayerColor.Black, board.Points[13].Color);
        Assert.Equal(5, board.Points[13].CheckerCount);

        Assert.Equal(PlayerColor.Black, board.Points[8].Color);
        Assert.Equal(3, board.Points[8].CheckerCount);

        Assert.Equal(PlayerColor.Black, board.Points[6].Color);
        Assert.Equal(5, board.Points[6].CheckerCount);

        // Act & Assert - Empty points check (check a few)
        Assert.Equal(PlayerColor.None, board.Points[2].Color);
        Assert.Equal(0, board.Points[2].CheckerCount);

        Assert.Equal(PlayerColor.None, board.Points[10].Color);
        Assert.Equal(0, board.Points[10].CheckerCount);
    }
}
