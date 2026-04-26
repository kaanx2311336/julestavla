using System;
using System.Drawing;
using System.Windows.Forms;

namespace TavlaJules.App.Controls;

public partial class GameBoardControl : UserControl
{
    public Panel[] Points { get; private set; }
    public Panel[] BarAreas { get; private set; }
    public Panel DiceDisplay { get; private set; }
    
    public Panel[] BorneOffAreas { get; private set; }
    public Label CurrentPlayerLabel { get; private set; }
    public Button NewGameButton { get; private set; }
    public Button RollDiceButton { get; private set; }
    public Button ApplyMoveButton { get; private set; }
    
    private Panel boardArea;
    private Panel controlArea;

    public GameBoardControl()
    {
        InitializeComponent();
        InitializeLayout();
    }

    private void InitializeLayout()
    {
        this.BackColor = Color.FromArgb(13, 18, 32);
        this.Size = new Size(900, 700);
        this.Dock = DockStyle.Fill;
        this.Padding = new Padding(10);

        // Control Area (Top)
        controlArea = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(23, 31, 50),
            Padding = new Padding(10)
        };
        
        NewGameButton = CreateButton("Yeni Oyun", 10);
        RollDiceButton = CreateButton("Zar At", 120);
        ApplyMoveButton = CreateButton("Hamle Yap", 230);
        
        CurrentPlayerLabel = new Label
        {
            Text = "Sira: Bekleniyor...",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(360, 18)
        };

        controlArea.Controls.Add(NewGameButton);
        controlArea.Controls.Add(RollDiceButton);
        controlArea.Controls.Add(ApplyMoveButton);
        controlArea.Controls.Add(CurrentPlayerLabel);
        
        // Board Area (Fill)
        boardArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.SaddleBrown,
            Padding = new Padding(20)
        };

        Points = new Panel[24];
        BarAreas = new Panel[2];
        BorneOffAreas = new Panel[2];
        DiceDisplay = new Panel();

        // Calculate layout
        int boardWidth = 800;
        int boardHeight = 500;
        int startX = (this.Width - boardWidth) / 2; // Approximate centering
        if (startX < 0) startX = 20;
        
        int startY = 40;
        
        int pointWidth = 45;
        int pointHeight = 200;
        int gap = 5;
        int barGap = 60; // gap for the middle bar
        int borneOffWidth = 60;

        // Top points (13-24)
        for (int i = 0; i < 12; i++)
        {
            Points[i + 12] = CreatePointPanel(i, true, startX, startY, pointWidth, pointHeight, gap, barGap);
            boardArea.Controls.Add(Points[i + 12]);
        }

        // Bottom points (1-12)
        for (int i = 0; i < 12; i++)
        {
            Points[11 - i] = CreatePointPanel(i, false, startX, startY + boardHeight - pointHeight, pointWidth, pointHeight, gap, barGap);
            boardArea.Controls.Add(Points[11 - i]);
        }

        // Bar areas (one for each player)
        int barX = startX + (6 * (pointWidth + gap)) + (barGap / 2) - 25; // Centered in the bar gap
        BarAreas[0] = new Panel { Size = new Size(50, 200), Location = new Point(barX, startY), BackColor = Color.DarkGoldenrod };
        BarAreas[1] = new Panel { Size = new Size(50, 200), Location = new Point(barX, startY + boardHeight - 200), BackColor = Color.DarkGoldenrod };
        boardArea.Controls.Add(BarAreas[0]);
        boardArea.Controls.Add(BarAreas[1]);

        // Borne Off areas
        int borneOffX = startX + (12 * (pointWidth + gap)) + barGap + 20;
        BorneOffAreas[0] = new Panel { Size = new Size(borneOffWidth, 200), Location = new Point(borneOffX, startY), BackColor = Color.DarkKhaki };
        BorneOffAreas[1] = new Panel { Size = new Size(borneOffWidth, 200), Location = new Point(borneOffX, startY + boardHeight - 200), BackColor = Color.DarkKhaki };
        boardArea.Controls.Add(BorneOffAreas[0]);
        boardArea.Controls.Add(BorneOffAreas[1]);

        // Dice display
        DiceDisplay = new Panel { Size = new Size(120, 50), Location = new Point(barX - 35, startY + (boardHeight / 2) - 25), BackColor = Color.Khaki };
        Label diceLabel = new Label { Text = "Zarlar", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        DiceDisplay.Controls.Add(diceLabel);
        boardArea.Controls.Add(DiceDisplay);

        this.Controls.Add(boardArea);
        this.Controls.Add(controlArea);
    }
    
    private Button CreateButton(string text, int x)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, 15),
            Size = new Size(100, 30),
            BackColor = Color.FromArgb(38, 93, 221),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
    }

    private Panel CreatePointPanel(int index, bool isTop, int startX, int y, int width, int height, int gap, int barGap)
    {
        int x = startX + (index * (width + gap));
        if (index >= 6) x += barGap;

        Color color = (index % 2 == 0) ? Color.Wheat : Color.SaddleBrown;

        Panel p = new Panel
        {
            Size = new Size(width, height),
            Location = new Point(x, y),
            BackColor = color,
            BorderStyle = BorderStyle.FixedSingle
        };
        
        // Add a small label to show point index for debugging
        int pointIndex = isTop ? 13 + index : 12 - index;
        Label l = new Label
        {
            Text = pointIndex.ToString(),
            ForeColor = (index % 2 == 0) ? Color.Black : Color.White,
            AutoSize = true,
            Location = new Point(2, isTop ? 2 : height - 20)
        };
        p.Controls.Add(l);

        return p;
    }
}
