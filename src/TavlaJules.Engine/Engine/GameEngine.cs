using System;
using TavlaJules.Engine.Models;
using TavlaJules.Engine.Validation;

namespace TavlaJules.Engine.Engine;

public class GameEngine
{
    public Board Board { get; private set; }
    private readonly MoveValidator _moveValidator;
    public PlayerColor CurrentTurn { get; private set; }

    public GameEngine()
    {
        Board = new Board();
        _moveValidator = new MoveValidator();
        CurrentTurn = PlayerColor.White; // Default starting turn
    }
    
    public GameEngine(Board board)
    {
        Board = board;
        _moveValidator = new MoveValidator();
        CurrentTurn = PlayerColor.White;
    }

    public void SetTurn(PlayerColor turn)
    {
        CurrentTurn = turn;
    }

    public bool ApplyMove(Move move, PlayerColor player)
    {
        if (player != CurrentTurn)
            return false;

        // Infer dice roll distance
        int diceRoll;
        bool isBearingOff = (player == PlayerColor.White && move.DestinationPoint == 25) ||
                            (player == PlayerColor.Black && move.DestinationPoint == 0);

        if (player == PlayerColor.White)
        {
            if (move.SourcePoint == 0) // from bar
                diceRoll = move.DestinationPoint;
            else if (isBearingOff)
                diceRoll = move.DestinationPoint - move.SourcePoint; // usually 25 - source
            else
                diceRoll = move.DestinationPoint - move.SourcePoint;
        }
        else // Black
        {
            if (move.SourcePoint == 25) // from bar
                diceRoll = 25 - move.DestinationPoint;
            else if (isBearingOff)
                diceRoll = move.SourcePoint; // move.SourcePoint - 0
            else
                diceRoll = move.SourcePoint - move.DestinationPoint;
        }

        if (!_moveValidator.IsValidMove(Board, player, move, diceRoll))
            return false;

        // Apply move

        // 1. Remove checker from source (or bar)
        if (player == PlayerColor.White && Board.WhiteCheckersOnBar > 0 && move.SourcePoint == 0)
        {
            Board.WhiteCheckersOnBar--;
        }
        else if (player == PlayerColor.Black && Board.BlackCheckersOnBar > 0 && move.SourcePoint == 25)
        {
            Board.BlackCheckersOnBar--;
        }
        else
        {
            var sourcePoint = Board.Points[move.SourcePoint];
            sourcePoint.CheckerCount--;
            if (sourcePoint.CheckerCount == 0)
            {
                sourcePoint.Color = PlayerColor.None;
            }
        }

        // 2. Add checker to destination
        if (isBearingOff)
        {
            if (player == PlayerColor.White)
                Board.WhiteCheckersBorneOff++;
            else
                Board.BlackCheckersBorneOff++;
        }
        else
        {
            var destPoint = Board.Points[move.DestinationPoint];

            // Handle hits
            if (destPoint.Color != player && destPoint.Color != PlayerColor.None && destPoint.CheckerCount == 1)
            {
                // Hit opponent
                if (destPoint.Color == PlayerColor.White)
                    Board.WhiteCheckersOnBar++;
                else
                    Board.BlackCheckersOnBar++;
                    
                destPoint.CheckerCount = 1; // It was 1, now it's our 1 checker
                destPoint.Color = player;
                move.IsHit = true;
            }
            else
            {
                // Normal add
                destPoint.Color = player;
                destPoint.CheckerCount++;
            }
        }

        // Switch turn
        CurrentTurn = player == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

        return true;
    }
}
