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
    public int TurnNumber { get; private set; } = 1;

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
        TurnNumber++;
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

        foreach (var move in GenerateLegalMovesForDice(player, _remainingDice, Board))
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

        foreach (var move in GenerateLegalMovesForDice(player, diceValues, Board))
        {
            yield return move;
        }
    }

    public IEnumerable<List<Move>> GenerateLegalMoveSequences(PlayerColor player, (int die1, int die2) dice)
    {
        ValidateDie(dice.die1);
        ValidateDie(dice.die2);

        var diceValues = dice.die1 == dice.die2
            ? new List<int> { dice.die1, dice.die1, dice.die1, dice.die1 }
            : new List<int> { dice.die1, dice.die2 }.OrderByDescending(d => d).ToList();

        var sequences = new List<List<Move>>();
        GenerateSequencesRecursive(Board, player, diceValues, new List<Move>(), sequences);

        if (!sequences.Any())
            return sequences;

        // Apply max consumption rule: find max number of dice played
        int maxDicePlayed = sequences.Max(s => s.Count);

        // If not all dice can be played and it was a non-double roll,
        // and max played is 1, we MUST play the higher die if possible.
        if (maxDicePlayed == 1 && dice.die1 != dice.die2)
        {
            int higherDie = Math.Max(dice.die1, dice.die2);
            bool canPlayHigher = sequences.Any(s => s[0].DiceUsed == higherDie);
            if (canPlayHigher)
            {
                return sequences.Where(s => s.Count == 1 && s[0].DiceUsed == higherDie).ToList();
            }
        }

        // Return sequences that consume the maximum possible number of dice
        return sequences.Where(s => s.Count == maxDicePlayed).ToList();
    }

    private void GenerateSequencesRecursive(Board board, PlayerColor player, List<int> remainingDice, List<Move> currentSequence, List<List<Move>> allSequences)
    {
        if (remainingDice.Count == 0)
        {
            if (currentSequence.Count > 0)
                allSequences.Add(new List<Move>(currentSequence));
            return;
        }

        var possibleMoves = GenerateLegalMovesForDice(player, remainingDice, board).ToList();

        if (!possibleMoves.Any())
        {
            if (currentSequence.Count > 0)
                allSequences.Add(new List<Move>(currentSequence));
            return;
        }

        // Deduplicate initial moves if they are exactly the same conceptually, 
        // but wait, since DiceUsed could be different for the same move, let's keep all.
        // Actually, distinct based on Source, Dest, and DiceUsed to avoid exponential branch explosion.
        var distinctMoves = possibleMoves
            .GroupBy(m => new { m.SourcePoint, m.DestinationPoint, m.DiceUsed })
            .Select(g => g.First())
            .ToList();

        foreach (var move in distinctMoves)
        {
            var boardClone = board.Clone();
            var engineClone = new GameEngine(boardClone);
            engineClone.SetTurn(player);
            // Apply move core logic securely via helper
            engineClone.ApplyCoreMoveLogic(move, player, engineClone.IsBearingOffMove(move, player));

            currentSequence.Add(move);
            var nextRemainingDice = new List<int>(remainingDice);
            nextRemainingDice.Remove(move.DiceUsed);

            GenerateSequencesRecursive(boardClone, player, nextRemainingDice, currentSequence, allSequences);

            currentSequence.RemoveAt(currentSequence.Count - 1); // backtrack
        }
    }

    private IEnumerable<Move> GenerateLegalMovesForDice(PlayerColor player, IEnumerable<int> diceValues, Board board)
    {
        if (player == PlayerColor.None)
            yield break;

        var uniqueDice = diceValues.Distinct().OrderByDescending(die => die).ToList();
        if (uniqueDice.Count == 0)
            yield break;

        int checkersOnBar = player == PlayerColor.White ? board.WhiteCheckersOnBar : board.BlackCheckersOnBar;
        if (checkersOnBar > 0)
        {
            foreach (var die in uniqueDice)
            {
                int sourcePoint = player == PlayerColor.White ? 0 : 25;
                int destPoint = player == PlayerColor.White ? die : 25 - die;
                var move = CreateMove(sourcePoint, destPoint, player, die, board);

                if (_moveValidator.IsValidMove(board, player, move, die))
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
                var point = board.Points[sourcePoint];
                if (point.Color != player || point.CheckerCount <= 0)
                {
                    continue;
                }

                int destPoint = player == PlayerColor.White ? sourcePoint + die : sourcePoint - die;
                if (player == PlayerColor.White && destPoint > 24) destPoint = 25;
                if (player == PlayerColor.Black && destPoint < 1) destPoint = 0;

                var move = CreateMove(sourcePoint, destPoint, player, die, board);
                if (_moveValidator.IsValidMove(board, player, move, die))
                {
                    yield return move;
                }
            }
        }
    }

    private Move CreateMove(int sourcePoint, int destPoint, PlayerColor player, int die)
    {
        return CreateMove(sourcePoint, destPoint, player, die, Board);
    }

    private Move CreateMove(int sourcePoint, int destPoint, PlayerColor player, int die, Board board)
    {
        var move = new Move(sourcePoint, destPoint, 1, diceUsed: die);
        if (!IsBearingOffMove(move, player) && destPoint is >= 1 and <= 24)
        {
            var destination = board.Points[destPoint];
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

    public GameStateSnapshot CaptureGameStateSnapshot()
    {
        var points = Board.Points
            .Where(p => p != null && p.Index >= 1 && p.Index <= 24)
            .Select(p => new PointSnapshot(p.Index, p.Color, p.CheckerCount))
            .ToList();

        return new GameStateSnapshot(
            points.AsReadOnly(),
            Board.WhiteCheckersOnBar,
            Board.BlackCheckersOnBar,
            Board.WhiteCheckersBorneOff,
            Board.BlackCheckersBorneOff,
            CurrentTurn,
            RemainingDice.ToList().AsReadOnly(),
            TurnNumber
        );
    }

    public static GameStateSnapshot CaptureGameStateSnapshot(GameEngine engine)
    {
        return engine.CaptureGameStateSnapshot();
    }
}
