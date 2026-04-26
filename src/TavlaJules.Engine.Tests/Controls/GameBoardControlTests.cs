using System;
using Xunit;
using TavlaJules.App.Controls;
using System.Windows.Forms;

namespace TavlaJules.Engine.Tests.Controls;

public class GameBoardControlTests
{
    [Fact]
    public void GameBoardControl_Loads_Without_Exceptions()
    {
        var exception = Record.Exception(() => new GameBoardControl());
        Assert.Null(exception);
    }

    [Fact]
    public void GameBoardControl_Initializes_Expected_Components()
    {
        var board = new GameBoardControl();

        Assert.NotNull(board.Points);
        Assert.Equal(24, board.Points.Length);

        Assert.NotNull(board.BarAreas);
        Assert.Equal(2, board.BarAreas.Length);

        Assert.NotNull(board.DiceDisplay);
        
        Assert.NotNull(board.BorneOffAreas);
        Assert.Equal(2, board.BorneOffAreas.Length);
        
        Assert.NotNull(board.CurrentPlayerLabel);
        Assert.NotNull(board.NewGameButton);
        Assert.NotNull(board.RollDiceButton);
        Assert.NotNull(board.ApplyMoveButton);
    }
}
