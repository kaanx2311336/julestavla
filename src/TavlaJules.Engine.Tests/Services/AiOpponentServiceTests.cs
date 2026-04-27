using System.Collections.Generic;
using System.Linq;
using Moq;
using TavlaJules.Engine.Services;
using TavlaJules.Engine.Engine;
using TavlaJules.Engine.Models;
using Xunit;

namespace TavlaJules.Engine.Tests.Services;

public class AiOpponentServiceTests
{
    [Fact]
    public void GetBestMoveSequence_ShouldReturnEmpty_WhenNoValidSequences()
    {
        var engine = new GameEngine();
        // Setup a situation where there are no valid moves. But default start has moves.
        // Let's just create an engine, clear remaining dice, so IsTurnComplete returns true,
        // Wait, the new logic calls engine.GenerateLegalMoveSequences, which will return empty if no dice or no valid moves.
        engine.StartTurn(PlayerColor.White, 3, 4);
        // Put all checkers on bar, and make opponent have closed board
        for (int i = 1; i <= 24; i++)
        {
            engine.Board.Points[i].CheckerCount = 0;
            engine.Board.Points[i].Color = PlayerColor.None;
        }
        for (int i = 1; i <= 6; i++)
        {
            engine.Board.Points[i].Color = PlayerColor.Black;
            engine.Board.Points[i].CheckerCount = 2;
        }
        engine.Board.WhiteCheckersOnBar = 1;
        
        var service = new AiOpponentService();
        var sequence = service.GetBestMoveSequence(engine);

        Assert.Empty(sequence);
    }

    [Fact]
    public void GetBestMoveSequence_ShouldPreferHit_WhenMultipleSequencesAvailable()
    {
        var engine = new GameEngine();
        // Clear board
        for (int i = 1; i <= 24; i++) { engine.Board.Points[i].CheckerCount = 0; engine.Board.Points[i].Color = PlayerColor.None; }
        
        engine.StartTurn(PlayerColor.White, 2, 1);
        
        // Put one white checker at point 20 so die 2 can hit point 22.
        engine.Board.Points[20].Color = PlayerColor.White;
        engine.Board.Points[20].CheckerCount = 1;

        // Put one black checker at point 22 -> 2 points away in White's direction.
        engine.Board.Points[22].Color = PlayerColor.Black;
        engine.Board.Points[22].CheckerCount = 1;

        // Put one black checker at point 15 (index 14) -> not reachable with 1 or 2
        engine.Board.Points[15].Color = PlayerColor.Black;
        engine.Board.Points[15].CheckerCount = 1;
        
        var service = new AiOpponentService();
        var sequence = service.GetBestMoveSequence(engine);
        
        // Expected sequence to hit point 21 using the 2 die
        Assert.NotEmpty(sequence);
        Assert.Contains(sequence, m => m.IsHit || m.DestinationPoint == 22);
    }

    [Fact]
    public void GetBestMoveSequence_ShouldPreferBearingOff()
    {
        var engine = new GameEngine();
        // Clear board
        for (int i = 1; i <= 24; i++) { engine.Board.Points[i].CheckerCount = 0; engine.Board.Points[i].Color = PlayerColor.None; }
        
        engine.StartTurn(PlayerColor.White, 3, 2);
        
        // Put one white checker at point 3 (index 2) -> 3 away from bearing off (off is 25)
        // Actually for white, bearing off is point 25, starting from 1..24
        // Index 2 is point 3. So it needs 22 to bear off?
        // Wait, white moves 1 to 24. Bearing off is from 19-24. 
        // Let's put white at point 22 (index 21). It needs 3 to bear off (22+3=25).
        engine.Board.Points[22].Color = PlayerColor.White;
        engine.Board.Points[22].CheckerCount = 2;
        
        // Another white checker at point 20 (index 19). It needs 2 to move to 22.
        engine.Board.Points[20].Color = PlayerColor.White;
        engine.Board.Points[20].CheckerCount = 2;
        
        // Ensure all white checkers are in the home board so bearing off is legal
        // No other white checkers on board.
        
        var service = new AiOpponentService();
        var sequence = service.GetBestMoveSequence(engine);

        Assert.NotEmpty(sequence);
        // The sequence should include bearing off (DestinationPoint = 25)
        Assert.Contains(sequence, m => m.DestinationPoint == 25);
    }
}
