using System;
using System.Drawing;
using System.Windows.Forms;

namespace TavlaJules.App.Views;

public partial class BoardControl : UserControl
{
    public Panel[]? Points { get; private set; }
    public Panel[]? BarAreas { get; private set; }
    public Panel? DiceDisplay { get; private set; }

    public BoardControl()
    {
        InitializeComponent();
        InitializeBoardLayout();
    }

    private void InitializeBoardLayout()
    {
        this.BackColor = Color.SaddleBrown;
        this.Size = new Size(800, 600);

        Points = new Panel[24];
        BarAreas = new Panel[2];
        DiceDisplay = new Panel();

        // Top points (13-24)
        for (int i = 0; i < 12; i++)
        {
            Points[i + 12] = CreatePointPanel(i, true);
            this.Controls.Add(Points[i + 12]);
        }

        // Bottom points (1-12)
        for (int i = 0; i < 12; i++)
        {
            Points[11 - i] = CreatePointPanel(i, false);
            this.Controls.Add(Points[11 - i]);
        }

        // Bar areas (one for each player)
        BarAreas[0] = new Panel { Size = new Size(40, 200), Location = new Point(380, 50), BackColor = Color.DarkGoldenrod };
        BarAreas[1] = new Panel { Size = new Size(40, 200), Location = new Point(380, 350), BackColor = Color.DarkGoldenrod };
        this.Controls.Add(BarAreas[0]);
        this.Controls.Add(BarAreas[1]);

        // Dice display
        DiceDisplay = new Panel { Size = new Size(100, 50), Location = new Point(350, 275), BackColor = Color.Khaki };
        this.Controls.Add(DiceDisplay);
    }

    private Panel CreatePointPanel(int index, bool isTop)
    {
        int width = 40;
        int height = 200;
        int startX = 50;
        int gap = 10;
        int barGap = 60; // gap for the middle bar

        int x = startX + (index * (width + gap));
        if (index >= 6) x += barGap;

        int y = isTop ? 50 : 350;

        Color color = (index % 2 == 0) ? Color.Wheat : Color.SaddleBrown;

        return new Panel
        {
            Size = new Size(width, height),
            Location = new Point(x, y),
            BackColor = color,
            BorderStyle = BorderStyle.FixedSingle
        };
    }
}
