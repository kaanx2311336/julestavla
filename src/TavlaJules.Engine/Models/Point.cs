namespace TavlaJules.Engine.Models;

public class Point
{
    public int Index { get; set; }
    public PlayerColor Color { get; set; }
    public int CheckerCount { get; set; }

    public Point(int index)
    {
        Index = index;
        Color = PlayerColor.None;
        CheckerCount = 0;
    }
}
