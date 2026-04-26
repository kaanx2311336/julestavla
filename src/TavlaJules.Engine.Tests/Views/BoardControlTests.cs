using System;
using Xunit;
using TavlaJules.App.Views;
using System.Windows.Forms;

namespace TavlaJules.Engine.Tests.Views;

public class BoardControlTests
{
    [Fact]
    public void BoardControl_Loads_Without_Exceptions()
    {
        var exception = Record.Exception(() => new BoardControl());
        Assert.Null(exception);
    }

    [Fact]
    public void BoardControl_Initializes_Expected_Components()
    {
        var board = new BoardControl();

        Assert.NotNull(board.Points);
        Assert.Equal(24, board.Points.Length);
        foreach (var point in board.Points)
        {
            Assert.NotNull(point);
            Assert.True(board.Controls.Contains(point));
        }

        Assert.NotNull(board.BarAreas);
        Assert.Equal(2, board.BarAreas.Length);
        foreach (var bar in board.BarAreas)
        {
            Assert.NotNull(bar);
            Assert.True(board.Controls.Contains(bar));
        }

        Assert.NotNull(board.DiceDisplay);
        Assert.True(board.Controls.Contains(board.DiceDisplay));
    }
}
