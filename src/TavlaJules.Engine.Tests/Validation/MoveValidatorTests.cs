using Xunit;
using TavlaJules.Engine.Models;
using TavlaJules.Engine.Validation;

namespace TavlaJules.Engine.Tests.Validation;

public class MoveValidatorTests
{
    private MoveValidator _validator;

    public MoveValidatorTests()
    {
        _validator = new MoveValidator();
    }

    [Fact]
    public void IsValidMove_NormalMoveToEmptyPoint_ReturnsTrue()
    {
        var board = new Board(); // Standard initial setup
        // White has 2 checkers on point 1. Move 1 to point 2 (empty).
        var move = new Move(1, 2, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 1); // dice roll 1
        
        Assert.True(result);
    }

    [Fact]
    public void IsValidMove_MoveToPointWithOwnCheckers_ReturnsTrue()
    {
        var board = new Board(); 
        // White has 2 checkers on 1, 5 on 12. Move 1 to 12 requires dice 11.
        var move = new Move(1, 12, 1);
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 11);
        
        Assert.True(result);
    }

    [Fact]
    public void IsValidMove_MoveToPointWithOneOpponentChecker_ReturnsTrue_IsHit()
    {
        var board = new Board();
        // Clear board and setup a specific scenario
        for(int i = 1; i <= 24; i++) {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }

        board.Points[1].Color = PlayerColor.White;
        board.Points[1].CheckerCount = 2;
        
        board.Points[5].Color = PlayerColor.Black;
        board.Points[5].CheckerCount = 1; // Blot

        var move = new Move(1, 5, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 4);
        
        Assert.True(result);
    }

    [Fact]
    public void IsValidMove_MoveToPointWithMultipleOpponentCheckers_ReturnsFalse()
    {
        var board = new Board();
        // White on 1, Black has 5 on 6. Move from 1 to 6 (dice 5) should be false
        var move = new Move(1, 6, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 5);
        
        Assert.False(result);
    }

    [Fact]
    public void IsValidMove_WrongPlayerColor_ReturnsFalse()
    {
        var board = new Board();
        // Try to move Black's checker (on 24) as White
        var move = new Move(24, 23, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 1);
        
        Assert.False(result);
    }

    [Fact]
    public void IsValidMove_MoveDistanceDoesNotMatchDice_ReturnsFalse()
    {
        var board = new Board();
        // White on 1. Move to 3, but dice is 3 (should be 4)
        var move = new Move(1, 4, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 2);
        
        Assert.False(result);
    }

    [Fact]
    public void IsValidMove_BlackMovesBackwards_ReturnsTrue()
    {
        var board = new Board();
        // Black has 2 on 24. Move to 23 with dice 1.
        var move = new Move(24, 23, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.Black, move, 1);
        
        Assert.True(result);
    }
    
    [Fact]
    public void IsValidMove_BlackMovesForwards_ReturnsFalse()
    {
        var board = new Board();
        // Black has 5 on 13. Try to move to 14 (wrong direction)
        var move = new Move(13, 14, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.Black, move, 1);
        
        Assert.False(result);
    }

    [Fact]
    public void IsValidMove_WhiteHasCheckerOnBar_MustMoveFromBar_ReturnsTrue()
    {
        var board = new Board();
        board.WhiteCheckersOnBar = 1;
        
        // Move from bar (represented as source 0 or 25 depending on convention, let's use 0 for white bar)
        // White enters to point 1 with dice 1
        var move = new Move(0, 1, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 1);
        
        Assert.True(result);
    }

    [Fact]
    public void IsValidMove_WhiteHasCheckerOnBar_TryToMoveOtherChecker_ReturnsFalse()
    {
        var board = new Board();
        board.WhiteCheckersOnBar = 1;
        
        // Try to move checker on point 1
        var move = new Move(1, 2, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 1);
        
        Assert.False(result);
    }

    [Fact]
    public void IsValidMove_BlackHasCheckerOnBar_MustMoveFromBar_ReturnsTrue()
    {
        var board = new Board();
        board.BlackCheckersOnBar = 1;
        
        // Black bar is 25. Enters to 24 with dice 1
        var move = new Move(25, 24, 1);
        
        var result = _validator.IsValidMove(board, PlayerColor.Black, move, 1);
        
        Assert.True(result);
    }

    [Fact]
    public void IsValidMove_BearingOff_AllCheckersInHomeBoard_ReturnsTrue()
    {
        var board = new Board();
        // Clear board
        for(int i = 1; i <= 24; i++) {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }

        // White all in home board (19-24)
        board.Points[20].Color = PlayerColor.White;
        board.Points[20].CheckerCount = 2;
        board.Points[22].Color = PlayerColor.White;
        board.Points[22].CheckerCount = 2;
        
        // Bear off from 20 with dice 5 (Destination 25 represents off board for white)
        var move = new Move(20, 25, 1);
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 5);
        
        Assert.True(result);
    }
    
    [Fact]
    public void IsValidMove_BearingOff_NotAllCheckersInHomeBoard_ReturnsFalse()
    {
        var board = new Board();
        // Clear board
        for(int i = 1; i <= 24; i++) {
            board.Points[i].CheckerCount = 0;
            board.Points[i].Color = PlayerColor.None;
        }

        // White has checker outside home board
        board.Points[18].Color = PlayerColor.White;
        board.Points[18].CheckerCount = 1;
        
        board.Points[20].Color = PlayerColor.White;
        board.Points[20].CheckerCount = 2;
        
        // Try to bear off from 20
        var move = new Move(20, 25, 1);
        var result = _validator.IsValidMove(board, PlayerColor.White, move, 5);
        
        Assert.False(result);
    }
}
