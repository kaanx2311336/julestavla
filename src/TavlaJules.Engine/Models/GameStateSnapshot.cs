using System.Collections.Generic;

namespace TavlaJules.Engine.Models;

public record PointSnapshot(int Index, PlayerColor Color, int CheckerCount);

public record GameStateSnapshot(
    IReadOnlyList<PointSnapshot> Points,
    int WhiteCheckersOnBar,
    int BlackCheckersOnBar,
    int WhiteCheckersBorneOff,
    int BlackCheckersBorneOff,
    PlayerColor CurrentTurn,
    IReadOnlyList<int> RemainingDice,
    int TurnNumber
);
