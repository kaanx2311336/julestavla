using System;
using Xunit;
using TavlaJules.Engine.Engine;
using TavlaJules.Engine.Models;

namespace TavlaJules.Engine.Tests.Engine;

public class GameEngineTests
{
    [Fact]
    public void ApplyMove_ValidNormalMove_UpdatesBoardState()
    {
        // Arrange
        var engine = new GameEngine();
        engine.SetTurn(PlayerColor.White);
        
        // Initial state for White at index 1 is 2 checkers
        var move = new Move(1, 4, 1); // Move 1 checker from 1 to 4 (dice roll 3)
        
        // Act
        bool result = engine.ApplyMove(move, PlayerColor.White);
        
        // Assert
        Assert.True(result);
        Assert.Equal(1, engine.Board.Points[1].CheckerCount);
        Assert.Equal(PlayerColor.White, engine.Board.Points[1].Color);
        Assert.Equal(1, engine.Board.Points[4].CheckerCount);
        Assert.Equal(PlayerColor.White, engine.Board.Points[4].Color);
        Assert.Equal(PlayerColor.Black, engine.CurrentTurn);
    }
    
    [Fact]
    public void ApplyMove_InvalidMoveFromEmptyPoint_ReturnsFalse()
    {
        // Arrange
        var engine = new GameEngine();
        engine.SetTurn(PlayerColor.White);
        
        // Point 2 is empty at start
        var move = new Move(2, 5, 1); // dice roll 3
        
        // Act
        bool result = engine.ApplyMove(move, PlayerColor.White);
        
        // Assert
        Assert.False(result);
        Assert.Equal(0, engine.Board.Points[2].CheckerCount);
        Assert.Equal(PlayerColor.None, engine.Board.Points[2].Color);
        Assert.Equal(PlayerColor.White, engine.CurrentTurn); // Turn doesn't switch on invalid move
    }

    [Fact]
    public void ApplyMove_ValidBearingOffMove_UpdatesBorneOffCount()
    {
        // Arrange
        var board = new Board();
        // Clear board
        for (int i = 1; i <= 24; i++)
        {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }
        
        // Put all White checkers in home board (19-24)
        board.Points[22].CheckerCount = 3;
        board.Points[22].Color = PlayerColor.White;
        board.Points[24].CheckerCount = 2;
        board.Points[24].Color = PlayerColor.White;
        
        var engine = new GameEngine(board);
        engine.SetTurn(PlayerColor.White);
        
        var move = new Move(22, 25, 1); // Move off board with exact roll 3
        
        // Act
        bool result = engine.ApplyMove(move, PlayerColor.White);
        
        // Assert
        Assert.True(result);
        Assert.Equal(2, engine.Board.Points[22].CheckerCount);
        Assert.Equal(1, engine.Board.WhiteCheckersBorneOff);
        Assert.Equal(PlayerColor.Black, engine.CurrentTurn);
    }
    
    [Fact]
    public void ApplyMove_ValidHit_SendsOpponentToBar()
    {
        // Arrange
        var board = new Board();
        // Clear board
        for (int i = 1; i <= 24; i++)
        {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }
        
        board.Points[10].CheckerCount = 1;
        board.Points[10].Color = PlayerColor.White;
        
        board.Points[12].CheckerCount = 1;
        board.Points[12].Color = PlayerColor.Black;
        
        var engine = new GameEngine(board);
        engine.SetTurn(PlayerColor.White);
        
        var move = new Move(10, 12, 1); // Move off board with exact roll 2
        
        // Act
        bool result = engine.ApplyMove(move, PlayerColor.White);
        
        // Assert
        Assert.True(result);
        Assert.True(move.IsHit);
        Assert.Equal(0, engine.Board.Points[10].CheckerCount);
        Assert.Equal(1, engine.Board.Points[12].CheckerCount);
        Assert.Equal(PlayerColor.White, engine.Board.Points[12].Color);
        Assert.Equal(1, engine.Board.BlackCheckersOnBar);
        Assert.Equal(PlayerColor.Black, engine.CurrentTurn);
    }
}
