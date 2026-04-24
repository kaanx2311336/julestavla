namespace TavlaJules.Engine.Models;

public class Move
{
    public int SourcePoint { get; set; }
    public int DestinationPoint { get; set; }
    public int CheckerCount { get; set; }
    public bool IsHit { get; set; }
    public int DiceUsed { get; set; }

    public Move(int sourcePoint, int destinationPoint, int checkerCount, bool isHit = false, int diceUsed = 0)
    {
        SourcePoint = sourcePoint;
        DestinationPoint = destinationPoint;
        CheckerCount = checkerCount;
        IsHit = isHit;
        DiceUsed = diceUsed;
    }
}
