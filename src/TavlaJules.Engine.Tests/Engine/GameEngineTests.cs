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
    [Fact]
    public void RollDice_DeterministicRandom_ReturnsExpectedValues()
    {
        var engine = new GameEngine();
        var random = new Random(42); // Deterministic seed

        // Expected values for seed 42
        var (die1, die2) = engine.RollDice(random);
        // Next(1, 7) with seed 42 gives 5 then 1 on current .NET runtime
        Assert.Equal(5, die1);
        Assert.Equal(1, die2);
    }

    [Fact]
    public void StartTurn_SetsTurnAndRemainingDice()
    {
        var engine = new GameEngine();
        engine.StartTurn(PlayerColor.White, 3, 5);

        Assert.Equal(PlayerColor.White, engine.CurrentTurn);
        Assert.Equal(2, engine.RemainingDice.Count);
        Assert.Equal(5, engine.RemainingDice[0]); // Sorted descending
        Assert.Equal(3, engine.RemainingDice[1]);
        Assert.False(engine.IsTurnComplete);
    }

    [Fact]
    public void StartTurn_DoubleDice_SetsFourRemainingDice()
    {
        var engine = new GameEngine();
        engine.StartTurn(PlayerColor.Black, 4, 4);

        Assert.Equal(PlayerColor.Black, engine.CurrentTurn);
        Assert.Equal(4, engine.RemainingDice.Count);
        Assert.All(engine.RemainingDice, d => Assert.Equal(4, d));
        Assert.False(engine.IsTurnComplete);
    }

    [Fact]
    public void StartTurn_InvalidDice_ThrowsArgumentOutOfRangeException()
    {
        var engine = new GameEngine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.StartTurn(PlayerColor.White, 7, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.StartTurn(PlayerColor.White, 0, 2));
    }

    [Fact]
    public void ApplyMove_ValidMove_ConsumesDieAndDoesNotSwitchTurnImmediately()
    {
        var engine = new GameEngine();
        // Setup a normal board
        // Start turn with dice 3 and 1
        engine.StartTurn(PlayerColor.White, 3, 1);
        
        // Move 1 checker from 1 to 4 (dice roll 3)
        var move = new Move(1, 4, 1);
        
        bool result = engine.ApplyMove(move);
        
        Assert.True(result);
        Assert.Single(engine.RemainingDice);
        Assert.Equal(1, engine.RemainingDice[0]); // Only 1 is left
        Assert.Equal(PlayerColor.White, engine.CurrentTurn); // Turn is still White
        Assert.False(engine.IsTurnComplete);
    }

    [Fact]
    public void ApplyMove_ValidMoveCompletingTurn_SwitchesTurn()
    {
        var engine = new GameEngine();
        engine.StartTurn(PlayerColor.White, 3, 1);
        
        var move1 = new Move(1, 4, 1); // Uses 3
        var move2 = new Move(4, 5, 1); // Uses 1
        
        engine.ApplyMove(move1);
        bool result = engine.ApplyMove(move2);
        
        Assert.True(result);
        Assert.Empty(engine.RemainingDice);
        Assert.Equal(PlayerColor.Black, engine.CurrentTurn); // Turn switched
        Assert.True(engine.IsTurnComplete);
    }

    [Fact]
    public void ApplyMove_NoMatchingDie_ReturnsFalse()
    {
        var engine = new GameEngine();
        engine.StartTurn(PlayerColor.White, 5, 2);
        
        // Try moving 3
        var move = new Move(1, 4, 1);
        
        bool result = engine.ApplyMove(move);
        
        Assert.False(result);
        Assert.Equal(2, engine.RemainingDice.Count);
    }

    [Fact]
    public void AdvanceTurn_ClearsDiceAndSwitchesTurn()
    {
        var engine = new GameEngine();
        engine.StartTurn(PlayerColor.White, 3, 1);
        
        engine.AdvanceTurn();

        Assert.Empty(engine.RemainingDice);
        Assert.Equal(PlayerColor.Black, engine.CurrentTurn);
        Assert.True(engine.IsTurnComplete);
    }

    [Fact]
    public void GenerateLegalMoves_ReturnsExpectedMovesForRemainingDice()
    {
        var board = new Board();
        // Clear board
        for (int i = 1; i <= 24; i++)
        {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }
        
        board.Points[1].CheckerCount = 1;
        board.Points[1].Color = PlayerColor.White;

        var engine = new GameEngine(board);
        engine.StartTurn(PlayerColor.White, 3, 2);

        var legalMoves = engine.GenerateLegalMoves(PlayerColor.White).ToList();

        // Should have a move for 2 and a move for 3
        Assert.Equal(2, legalMoves.Count);
        Assert.Contains(legalMoves, m => m.SourcePoint == 1 && m.DestinationPoint == 3);
        Assert.Contains(legalMoves, m => m.SourcePoint == 1 && m.DestinationPoint == 4);
    }
}
