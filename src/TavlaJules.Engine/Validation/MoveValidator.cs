using System;
using System.Linq;
using TavlaJules.Engine.Models;

namespace TavlaJules.Engine.Validation;

public class MoveValidator
{
    public bool IsValidMove(Board board, PlayerColor playerColor, Move move, int diceRoll)
    {
        // 1. Basic validation
        if (playerColor == PlayerColor.None) return false;
        
        // 2. Bar validation
        if (playerColor == PlayerColor.White && board.WhiteCheckersOnBar > 0)
        {
            if (move.SourcePoint != 0) return false; // Must move from bar
        }
        else if (playerColor == PlayerColor.Black && board.BlackCheckersOnBar > 0)
        {
            if (move.SourcePoint != 25) return false; // Must move from bar
        }
        else
        {
            // If not on bar, validate source point
            if (move.SourcePoint < 1 || move.SourcePoint > 24) return false;
            
            var sourcePoint = board.Points[move.SourcePoint];
            if (sourcePoint.Color != playerColor) return false; // Not player's checker
            if (sourcePoint.CheckerCount < 1) return false; // No checkers to move
        }

        // 3. Direction and Distance Validation
        int expectedDistance = playerColor == PlayerColor.White 
            ? move.DestinationPoint - move.SourcePoint 
            : move.SourcePoint - move.DestinationPoint;

        // Special case for bearing off
        bool isBearingOff = playerColor == PlayerColor.White && move.DestinationPoint == 25 ||
                            playerColor == PlayerColor.Black && move.DestinationPoint == 0;

        if (isBearingOff)
        {
            // Verify if all checkers are in home board
            if (!AreAllCheckersInHomeBoard(board, playerColor)) return false;
            
            // For bearing off, distance can be less than or equal to dice roll if no checkers are behind
            if (expectedDistance != diceRoll)
            {
                if (expectedDistance > diceRoll) return false;
                
                // If expectedDistance < diceRoll, check if there are any checkers further back
                int start = playerColor == PlayerColor.White ? 19 : 6;
                int end = move.SourcePoint;
                
                if (playerColor == PlayerColor.White)
                {
                    for (int i = start; i < end; i++)
                    {
                        if (board.Points[i].Color == playerColor && board.Points[i].CheckerCount > 0)
                            return false; // Found a checker further back
                    }
                }
                else // Black
                {
                    for (int i = start; i > end; i--)
                    {
                        if (board.Points[i].Color == playerColor && board.Points[i].CheckerCount > 0)
                            return false; // Found a checker further back
                    }
                }
            }
        }
        else
        {
            // Normal move, must match dice exactly
            if (expectedDistance != diceRoll) return false;
            
            // Bounds check for normal move
            if (move.DestinationPoint < 1 || move.DestinationPoint > 24) return false;

            // 4. Destination Validation
            var destPoint = board.Points[move.DestinationPoint];
            if (destPoint.Color != playerColor && destPoint.Color != PlayerColor.None)
            {
                // Opponent's point
                if (destPoint.CheckerCount > 1) return false; // Blocked
                // If CheckerCount == 1, it's a hit, which is valid
            }
        }

        return true;
    }

    private bool AreAllCheckersInHomeBoard(Board board, PlayerColor color)
    {
        if (color == PlayerColor.White)
        {
            if (board.WhiteCheckersOnBar > 0) return false;
            // White home board is 19-24. Check 1-18.
            for (int i = 1; i <= 18; i++)
            {
                if (board.Points[i].Color == color && board.Points[i].CheckerCount > 0)
                    return false;
            }
        }
        else // Black
        {
            if (board.BlackCheckersOnBar > 0) return false;
            // Black home board is 1-6. Check 7-24.
            for (int i = 7; i <= 24; i++)
            {
                if (board.Points[i].Color == color && board.Points[i].CheckerCount > 0)
                    return false;
            }
        }
        
        return true;
    }
}
