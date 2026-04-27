using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TavlaJules.Engine.Engine;
using TavlaJules.Engine.Models;

namespace TavlaJules.Engine.Services;

public class AiOpponentService
{
    public List<Move> GetBestMoveSequence(GameEngine engine)
    {
        if (engine.RemainingDice.Count == 0 || engine.IsTurnComplete)
        {
            return new List<Move>();
        }

        var sequences = new List<List<Move>>();
        GeneratePaths(engine, new List<Move>(), sequences);

        if (sequences.Count == 0)
        {
            return new List<Move>();
        }

        int maxDiceUsed = sequences.Max(s => s.Count);
        var validSequences = sequences.Where(s => s.Count == maxDiceUsed).ToList();

        return validSequences.OrderByDescending(seq => EvaluateSequence(engine.CurrentTurn, seq)).First();
    }

    private void GeneratePaths(GameEngine currentEngine, List<Move> currentPath, List<List<Move>> allPaths)
    {
        var legalMoves = currentEngine.GenerateLegalMoves(currentEngine.CurrentTurn).ToList();

        if (legalMoves.Count == 0)
        {
            if (currentPath.Count > 0)
            {
                allPaths.Add(new List<Move>(currentPath));
            }
            return;
        }

        var originalSnapshot = currentEngine.CaptureGameStateSnapshot();

        foreach (var move in legalMoves)
        {
            currentEngine.ApplyMove(move);
            currentPath.Add(move);

            GeneratePaths(currentEngine, currentPath, allPaths);

            currentPath.RemoveAt(currentPath.Count - 1);
            RestoreEngine(currentEngine, originalSnapshot);
        }
    }

    private void RestoreEngine(GameEngine engine, GameStateSnapshot snapshot)
    {
        // Clear and restore board points
        for (int i = 1; i <= 24; i++)
        {
            engine.Board.Points[i].CheckerCount = 0;
            engine.Board.Points[i].Color = PlayerColor.None;
        }

        foreach (var point in snapshot.Points)
        {
            if (point.CheckerCount > 0)
            {
                engine.Board.Points[point.Index].Color = point.Color;
                engine.Board.Points[point.Index].CheckerCount = point.CheckerCount;
            }
        }

        engine.Board.WhiteCheckersOnBar = snapshot.WhiteCheckersOnBar;
        engine.Board.BlackCheckersOnBar = snapshot.BlackCheckersOnBar;
        engine.Board.WhiteCheckersBorneOff = snapshot.WhiteCheckersBorneOff;
        engine.Board.BlackCheckersBorneOff = snapshot.BlackCheckersBorneOff;

        // Restore engine state
        var currentTurnField = typeof(GameEngine).GetProperty("CurrentTurn");
        if (currentTurnField != null && currentTurnField.CanWrite)
        {
            currentTurnField.SetValue(engine, snapshot.CurrentTurn);
        }

        var remainingDiceField = typeof(GameEngine).GetField("_remainingDice", BindingFlags.Instance | BindingFlags.NonPublic);
        if (remainingDiceField != null)
        {
            remainingDiceField.SetValue(engine, snapshot.RemainingDice.ToList());
        }

        var turnNumberField = typeof(GameEngine).GetProperty("TurnNumber");
        if (turnNumberField != null && turnNumberField.CanWrite)
        {
            turnNumberField.SetValue(engine, snapshot.TurnNumber);
        }
    }

    private int EvaluateSequence(PlayerColor color, List<Move> sequence)
    {
        int score = 0;
        score += sequence.Count * 1000;

        foreach (var move in sequence)
        {
            if (move.IsHit)
            {
                score += 500;
            }

            int oppOffPoint = color == PlayerColor.White ? 25 : 0;
            if (move.DestinationPoint == oppOffPoint)
            {
                score += 300;
            }

            int oppBarPoint = color == PlayerColor.White ? 0 : 25;
            if (move.SourcePoint == oppBarPoint)
            {
                score += 200;
            }

            score += Math.Abs(move.SourcePoint - oppOffPoint);
        }

        return score;
    }
}
