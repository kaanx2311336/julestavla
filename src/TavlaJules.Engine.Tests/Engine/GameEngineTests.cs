using System;
using System.Linq;
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

    [Fact]
    public void GenerateLegalMoves_WithExplicitDice_ReturnsMovesWithDiceUsed()
    {
        var board = CreateEmptyBoard();
        board.Points[1].CheckerCount = 1;
        board.Points[1].Color = PlayerColor.White;

        var engine = new GameEngine(board);
        engine.SetTurn(PlayerColor.Black);

        var legalMoves = engine.GenerateLegalMoves(PlayerColor.White, (2, 3)).ToList();

        Assert.Equal(2, legalMoves.Count);
        Assert.Contains(legalMoves, move => move.SourcePoint == 1 && move.DestinationPoint == 3 && move.DiceUsed == 2);
        Assert.Contains(legalMoves, move => move.SourcePoint == 1 && move.DestinationPoint == 4 && move.DiceUsed == 3);
    }

    [Fact]
    public void GenerateLegalMoves_WithExplicitDice_RespectsBlockedPoint()
    {
        var board = CreateEmptyBoard();
        board.Points[1].CheckerCount = 1;
        board.Points[1].Color = PlayerColor.White;
        board.Points[4].CheckerCount = 2;
        board.Points[4].Color = PlayerColor.Black;

        var engine = new GameEngine(board);

        var legalMoves = engine.GenerateLegalMoves(PlayerColor.White, (2, 3)).ToList();

        Assert.DoesNotContain(legalMoves, move => move.DestinationPoint == 4);
        Assert.Contains(legalMoves, move => move.DestinationPoint == 3 && move.DiceUsed == 2);
    }

    [Fact]
    public void GenerateLegalMoves_WithExplicitDice_ForcesBarEntry()
    {
        var board = CreateEmptyBoard();
        board.WhiteCheckersOnBar = 1;
        board.Points[1].CheckerCount = 1;
        board.Points[1].Color = PlayerColor.White;

        var engine = new GameEngine(board);

        var legalMoves = engine.GenerateLegalMoves(PlayerColor.White, (2, 3)).ToList();

        Assert.All(legalMoves, move => Assert.Equal(0, move.SourcePoint));
        Assert.Contains(legalMoves, move => move.DestinationPoint == 2 && move.DiceUsed == 2);
        Assert.Contains(legalMoves, move => move.DestinationPoint == 3 && move.DiceUsed == 3);
    }

    [Fact]
    public void GenerateLegalMoves_WithExplicitDice_AllowsBearingOffWithLargerDie()
    {
        var board = CreateEmptyBoard();
        board.Points[24].CheckerCount = 1;
        board.Points[24].Color = PlayerColor.White;

        var engine = new GameEngine(board);

        var legalMoves = engine.GenerateLegalMoves(PlayerColor.White, (6, 1)).ToList();

        Assert.Contains(legalMoves, move => move.SourcePoint == 24 && move.DestinationPoint == 25 && move.DiceUsed == 6);
        Assert.Contains(legalMoves, move => move.SourcePoint == 24 && move.DestinationPoint == 25 && move.DiceUsed == 1);
    }

    [Fact]
    public void GenerateLegalMoveSequences_ReturnsSequencesConsumingAllDice()
    {
        var board = CreateEmptyBoard();
        board.Points[1].CheckerCount = 2;
        board.Points[1].Color = PlayerColor.White;

        var engine = new GameEngine(board);
        var sequences = engine.GenerateLegalMoveSequences(PlayerColor.White, (2, 3)).ToList();

        // Should return sequences of length 2
        Assert.NotEmpty(sequences);
        Assert.All(sequences, s => Assert.Equal(2, s.Count));
        
        // Sequence options (can move 1 checker 1->3->6 or 1->4->6, or two checkers 1->3 and 1->4)
        var seq1 = sequences.FirstOrDefault(s => s[0].SourcePoint == 1 && s[0].DestinationPoint == 3 && s[1].SourcePoint == 1 && s[1].DestinationPoint == 4);
        var seq2 = sequences.FirstOrDefault(s => s[0].SourcePoint == 1 && s[0].DestinationPoint == 4 && s[1].SourcePoint == 1 && s[1].DestinationPoint == 3);
        
        Assert.True(seq1 != null || seq2 != null);
    }

    [Fact]
    public void GenerateLegalMoveSequences_BlockedDie_ReturnsMaxPossibleDice()
    {
        var board = CreateEmptyBoard();
        board.Points[1].CheckerCount = 1;
        board.Points[1].Color = PlayerColor.White;
        
        // Block point 4 (prevents using die 3 directly from 1)
        board.Points[4].CheckerCount = 2;
        board.Points[4].Color = PlayerColor.Black;
        
        // Block point 6 (prevents 1 -> 3 -> 6, meaning if we play 2 first, we can't play 3 next)
        board.Points[6].CheckerCount = 2;
        board.Points[6].Color = PlayerColor.Black;

        var engine = new GameEngine(board);
        var sequences = engine.GenerateLegalMoveSequences(PlayerColor.White, (2, 3)).ToList();

        // The only move is 1 -> 3 (uses die 2). From 3, playing 3 would go to 6, which is blocked.
        Assert.NotEmpty(sequences);
        Assert.All(sequences, s => Assert.Single(s));
        Assert.Contains(sequences, s => s[0].DestinationPoint == 3 && s[0].DiceUsed == 2);
    }

    [Fact]
    public void GenerateLegalMoveSequences_MustPlayHigherDie_WhenOnlyOneCanBePlayed()
    {
        var board = CreateEmptyBoard();
        board.Points[1].CheckerCount = 1;
        board.Points[1].Color = PlayerColor.White;

        // Block point 4 and point 6 (like above)
        board.Points[4].CheckerCount = 2;
        board.Points[4].Color = PlayerColor.Black;
        board.Points[6].CheckerCount = 2;
        board.Points[6].Color = PlayerColor.Black;

        // But what if we swap the dice to 5 and 2?
        // Move from 1 using 2 -> 3 (allowed) -> from 3 using 5 -> 8 (blocked let's say)
        // Move from 1 using 5 -> 6 (allowed) -> from 6 using 2 -> 8 (blocked let's say)
        board.Points[8].CheckerCount = 2;
        board.Points[8].Color = PlayerColor.Black;
        
        // Unblock 6 so we can play the 5. 
        board.Points[6].CheckerCount = 0;
        board.Points[6].Color = PlayerColor.None;

        // Now:
        // Play 2: 1 -> 3. Next needs 5, goes to 8 (blocked). Max length 1.
        // Play 5: 1 -> 6. Next needs 2, goes to 8 (blocked). Max length 1.
        // Rule: Must play the higher die (5).
        
        var engine = new GameEngine(board);
        var sequences = engine.GenerateLegalMoveSequences(PlayerColor.White, (5, 2)).ToList();

        Assert.NotEmpty(sequences);
        Assert.All(sequences, s => Assert.Single(s));
        // Only the move using die 5 should be returned.
        Assert.Contains(sequences, s => s[0].DestinationPoint == 6 && s[0].DiceUsed == 5);
        Assert.DoesNotContain(sequences, s => s[0].DiceUsed == 2);
    }

    private static Board CreateEmptyBoard()
    {
        var board = new Board();
        for (int i = 1; i <= 24; i++)
        {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }

        board.WhiteCheckersOnBar = 0;
        board.BlackCheckersOnBar = 0;
        board.WhiteCheckersBorneOff = 0;
        board.BlackCheckersBorneOff = 0;
        return board;
    }
}
