using System;
using TavlaJules.Engine.Models;
using TavlaJules.Engine.Validation;

namespace TavlaJules.Engine.Engine;

using System.Collections.Generic;
using System.Linq;

public class GameEngine
{
    public Board Board { get; private set; }
    private readonly MoveValidator _moveValidator;
    public PlayerColor CurrentTurn { get; private set; }

    private List<int> _remainingDice = new List<int>();
    public IReadOnlyList<int> RemainingDice => _remainingDice.AsReadOnly();

    public bool IsTurnComplete => _remainingDice.Count == 0;

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

    public (int die1, int die2) RollDice(Random? random = null)
    {
        var rng = random ?? new Random();
        return (rng.Next(1, 7), rng.Next(1, 7));
    }

    public void StartTurn(PlayerColor player, int die1, int die2)
    {
        if (die1 < 1 || die1 > 6 || die2 < 1 || die2 > 6)
            throw new ArgumentOutOfRangeException("Dice values must be between 1 and 6.");

        CurrentTurn = player;
        _remainingDice.Clear();

        if (die1 == die2)
        {
            _remainingDice.AddRange(new[] { die1, die1, die1, die1 });
        }
        else
        {
            _remainingDice.Add(die1);
            _remainingDice.Add(die2);
            // Sort descending so larger dice are used first if needed
            _remainingDice.Sort((a, b) => b.CompareTo(a));
        }
    }

    public void AdvanceTurn()
    {
        _remainingDice.Clear();
        CurrentTurn = CurrentTurn == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
    }

    public bool ApplyMove(Move move)
    {
        PlayerColor player = CurrentTurn;

        // Infer dice roll distance
        int diceRoll = InferDiceRoll(move, player);

        if (!_moveValidator.IsValidMove(Board, player, move, diceRoll))
            return false;

        // Find the die to use. If exact match not found, but it's a bearing off move
        // and we have a die larger than the required distance, we can use it.
        int dieIndex = -1;
        bool isBearingOff = IsBearingOffMove(move, player);

        for (int i = 0; i < _remainingDice.Count; i++)
        {
            if (_remainingDice[i] == diceRoll)
            {
                dieIndex = i;
                break;
            }
        }

        if (dieIndex == -1 && isBearingOff)
        {
            // If exact die is not found during bear off, find the smallest die that is larger than the needed roll.
            // Since the list is sorted descending, we iterate backwards to find the smallest valid die.
            for (int i = _remainingDice.Count - 1; i >= 0; i--)
            {
                if (_remainingDice[i] > diceRoll)
                {
                    dieIndex = i;
                    break;
                }
            }
        }

        if (dieIndex == -1)
            return false;

        // Apply the move using core logic
        ApplyCoreMoveLogic(move, player, isBearingOff);

        // Consume the die
        _remainingDice.RemoveAt(dieIndex);

        if (IsTurnComplete)
        {
            AdvanceTurn();
        }

        return true;
    }

    public IEnumerable<Move> GenerateLegalMoves(PlayerColor player)
    {
        if (player != CurrentTurn || IsTurnComplete)
            yield break;

        foreach (var move in GenerateLegalMovesForDice(player, _remainingDice))
        {
            yield return move;
        }
    }

    public IEnumerable<Move> GenerateLegalMoves(PlayerColor player, (int die1, int die2) dice)
    {
        ValidateDie(dice.die1);
        ValidateDie(dice.die2);

        var diceValues = dice.die1 == dice.die2
            ? new[] { dice.die1, dice.die1, dice.die1, dice.die1 }
            : new[] { dice.die1, dice.die2 };

        foreach (var move in GenerateLegalMovesForDice(player, diceValues))
        {
            yield return move;
        }
    }

    private IEnumerable<Move> GenerateLegalMovesForDice(PlayerColor player, IEnumerable<int> diceValues)
    {
        if (player == PlayerColor.None)
            yield break;

        var uniqueDice = diceValues.Distinct().OrderByDescending(die => die).ToList();
        if (uniqueDice.Count == 0)
            yield break;

        int checkersOnBar = player == PlayerColor.White ? Board.WhiteCheckersOnBar : Board.BlackCheckersOnBar;
        if (checkersOnBar > 0)
        {
            foreach (var die in uniqueDice)
            {
                int sourcePoint = player == PlayerColor.White ? 0 : 25;
                int destPoint = player == PlayerColor.White ? die : 25 - die;
                var move = CreateMove(sourcePoint, destPoint, player, die);

                if (_moveValidator.IsValidMove(Board, player, move, die))
                {
                    yield return move;
                }
            }

            yield break;
        }

        foreach (var die in uniqueDice)
        {
            for (int sourcePoint = 1; sourcePoint <= 24; sourcePoint++)
            {
                var point = Board.Points[sourcePoint];
                if (point.Color != player || point.CheckerCount <= 0)
                {
                    continue;
                }

                int destPoint = player == PlayerColor.White ? sourcePoint + die : sourcePoint - die;
                if (player == PlayerColor.White && destPoint > 24) destPoint = 25;
                if (player == PlayerColor.Black && destPoint < 1) destPoint = 0;

                var move = CreateMove(sourcePoint, destPoint, player, die);
                if (_moveValidator.IsValidMove(Board, player, move, die))
                {
                    yield return move;
                }
            }
        }
    }

    private Move CreateMove(int sourcePoint, int destPoint, PlayerColor player, int die)
    {
        var move = new Move(sourcePoint, destPoint, 1, diceUsed: die);
        if (!IsBearingOffMove(move, player) && destPoint is >= 1 and <= 24)
        {
            var destination = Board.Points[destPoint];
            move.IsHit = destination.Color != PlayerColor.None
                && destination.Color != player
                && destination.CheckerCount == 1;
        }

        return move;
    }

    private static void ValidateDie(int die)
    {
        if (die < 1 || die > 6)
            throw new ArgumentOutOfRangeException(nameof(die), "Dice values must be between 1 and 6.");
    }

    private int InferDiceRoll(Move move, PlayerColor player)
    {
        bool isBearingOff = IsBearingOffMove(move, player);

        if (player == PlayerColor.White)
        {
            if (move.SourcePoint == 0) return move.DestinationPoint;
            if (isBearingOff) return move.DestinationPoint - move.SourcePoint;
            return move.DestinationPoint - move.SourcePoint;
        }
        else
        {
            if (move.SourcePoint == 25) return 25 - move.DestinationPoint;
            if (isBearingOff) return move.SourcePoint;
            return move.SourcePoint - move.DestinationPoint;
        }
    }

    private bool IsBearingOffMove(Move move, PlayerColor player)
    {
        return (player == PlayerColor.White && move.DestinationPoint == 25) ||
               (player == PlayerColor.Black && move.DestinationPoint == 0);
    }

    private void ApplyCoreMoveLogic(Move move, PlayerColor player, bool isBearingOff)
    {
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
    }

    public bool ApplyMove(Move move, PlayerColor player)
    {
        if (player != CurrentTurn)
            return false;

        bool isBearingOff = IsBearingOffMove(move, player);
        int diceRoll = InferDiceRoll(move, player);

        if (!_moveValidator.IsValidMove(Board, player, move, diceRoll))
            return false;

        ApplyCoreMoveLogic(move, player, isBearingOff);

        // Switch turn immediately for backward compatibility tests
        CurrentTurn = player == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

        return true;
    }
}
