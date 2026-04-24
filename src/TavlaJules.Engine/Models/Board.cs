namespace TavlaJules.Engine.Models;

public class Board
{
    // Points are 1-indexed. Index 0 is not used or could represent the bar for White.
    // 1-24 are the main board points.
    public Point[] Points { get; private set; }

    // Bar properties
    public int WhiteCheckersOnBar { get; set; }
    public int BlackCheckersOnBar { get; set; }

    // Borne off properties
    public int WhiteCheckersBorneOff { get; set; }
    public int BlackCheckersBorneOff { get; set; }

    public Board()
    {
        Points = new Point[25]; // 1-24 are standard points.
        for (int i = 1; i <= 24; i++)
        {
            Points[i] = new Point(i);
        }
        
        InitializeStartingPosition();
    }

    private void InitializeStartingPosition()
    {
        // For White: moves 1 to 24
        // For Black: moves 24 to 1
        
        // Setup White checkers
        SetPoint(1, PlayerColor.White, 2);
        SetPoint(12, PlayerColor.White, 5);
        SetPoint(17, PlayerColor.White, 3);
        SetPoint(19, PlayerColor.White, 5);

        // Setup Black checkers
        SetPoint(24, PlayerColor.Black, 2);
        SetPoint(13, PlayerColor.Black, 5);
        SetPoint(8, PlayerColor.Black, 3);
        SetPoint(6, PlayerColor.Black, 5);
    }

    private void SetPoint(int index, PlayerColor color, int count)
    {
        Points[index].Color = color;
        Points[index].CheckerCount = count;
    }

    public Board Clone()
    {
        var clone = new Board();
        // Board constructor calls InitializeStartingPosition, so we overwrite the points.
        for (int i = 1; i <= 24; i++)
        {
            clone.Points[i].Color = this.Points[i].Color;
            clone.Points[i].CheckerCount = this.Points[i].CheckerCount;
        }

        clone.WhiteCheckersOnBar = this.WhiteCheckersOnBar;
        clone.BlackCheckersOnBar = this.BlackCheckersOnBar;
        clone.WhiteCheckersBorneOff = this.WhiteCheckersBorneOff;
        clone.BlackCheckersBorneOff = this.BlackCheckersBorneOff;

        return clone;
    }
}
