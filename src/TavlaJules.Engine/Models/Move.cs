namespace TavlaJules.Engine.Models;

public class Move
{
    public int SourcePoint { get; set; }
    public int DestinationPoint { get; set; }
    public int CheckerCount { get; set; }
    public bool IsHit { get; set; }

    public Move(int sourcePoint, int destinationPoint, int checkerCount, bool isHit = false)
    {
        SourcePoint = sourcePoint;
        DestinationPoint = destinationPoint;
        CheckerCount = checkerCount;
        IsHit = isHit;
    }
}
