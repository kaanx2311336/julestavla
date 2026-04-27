using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TavlaJules.Engine.Engine;
using TavlaJules.Engine.Models;

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
    public Button SaveGameButton { get; private set; }
    public Button LoadGameButton { get; private set; }
    public Button AiMoveButton { get; private set; }
    
    public Label LogLabel { get; private set; }
    
    private Panel boardArea;
    private Panel controlArea;

    private GameEngine _gameEngine;
    private List<Move> _legalMoves = new List<Move>();
    private int? _selectedSourcePoint;
    private Move _selectedMove;
    private TavlaJules.App.Services.GamePersistenceService? _persistenceService;
    private TavlaJules.Engine.Services.AiOpponentService _aiService = new TavlaJules.Engine.Services.AiOpponentService();

    public GameBoardControl()
    {
        InitializeComponent();
        InitializeLayout();
    }

    public void SetPersistenceService(TavlaJules.App.Services.GamePersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
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
        RollDiceButton = CreateButton("Zar At", 110);
        ApplyMoveButton = CreateButton("Hamle Yap", 210);
        SaveGameButton = CreateButton("Save Game", 310);
        LoadGameButton = CreateButton("Load Last Snapshot", 410);
        
        LoadGameButton.Width = 140;

        AiMoveButton = CreateButton("AI Hamlesi", 560);
        AiMoveButton.BackColor = Color.Indigo;

        CurrentPlayerLabel = new Label
        {
            Text = "Sira: Bekleniyor...",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new System.Drawing.Point(670, 18)
        };
        
        LogLabel = new Label
        {
            Text = "Durum: Oyun baslatilmadi.",
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            AutoSize = true,
            Location = new System.Drawing.Point(810, 20)
        };

        controlArea.Controls.Add(NewGameButton);
        controlArea.Controls.Add(RollDiceButton);
        controlArea.Controls.Add(ApplyMoveButton);
        controlArea.Controls.Add(SaveGameButton);
        controlArea.Controls.Add(LoadGameButton);
        controlArea.Controls.Add(AiMoveButton);
        controlArea.Controls.Add(CurrentPlayerLabel);
        controlArea.Controls.Add(LogLabel);
        
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
        BarAreas[0] = new Panel { Size = new Size(50, 200), Location = new System.Drawing.Point(barX, startY), BackColor = Color.DarkGoldenrod };
        BarAreas[1] = new Panel { Size = new Size(50, 200), Location = new System.Drawing.Point(barX, startY + boardHeight - 200), BackColor = Color.DarkGoldenrod };
        boardArea.Controls.Add(BarAreas[0]);
        boardArea.Controls.Add(BarAreas[1]);

        // Borne Off areas
        int borneOffX = startX + (12 * (pointWidth + gap)) + barGap + 20;
        BorneOffAreas[0] = new Panel { Size = new Size(borneOffWidth, 200), Location = new System.Drawing.Point(borneOffX, startY), BackColor = Color.DarkKhaki };
        BorneOffAreas[1] = new Panel { Size = new Size(borneOffWidth, 200), Location = new System.Drawing.Point(borneOffX, startY + boardHeight - 200), BackColor = Color.DarkKhaki };
        boardArea.Controls.Add(BorneOffAreas[0]);
        boardArea.Controls.Add(BorneOffAreas[1]);

        // Dice display
        DiceDisplay = new Panel { Size = new Size(120, 50), Location = new System.Drawing.Point(barX - 35, startY + (boardHeight / 2) - 25), BackColor = Color.Khaki };
        Label diceLabel = new Label { Name = "DiceLabel", Text = "Zarlar", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        DiceDisplay.Controls.Add(diceLabel);
        boardArea.Controls.Add(DiceDisplay);

        this.Controls.Add(boardArea);
        this.Controls.Add(controlArea);
        
        WireEvents();
        UpdateUiState();
    }
    
    private void WireEvents()
    {
        NewGameButton.Click += (s, e) => StartNewGame();
        RollDiceButton.Click += (s, e) => RollDice();
        ApplyMoveButton.Click += (s, e) => ApplySelectedMove();
        SaveGameButton.Click += OnSaveGameClick;
        LoadGameButton.Click += OnLoadGameClick;
        AiMoveButton.Click += (s, e) => PlayAiTurn();
        
        for (int i = 0; i < 24; i++)
        {
            int pointIndex = i + 1;
            Points[i].Click += (s, e) => HandlePointClick(pointIndex);
            foreach (Control c in Points[i].Controls)
            {
                c.Click += (s, e) => HandlePointClick(pointIndex);
            }
        }
        
        BarAreas[0].Click += (s, e) => HandleBarClick(PlayerColor.White);
        foreach (Control c in BarAreas[0].Controls) c.Click += (s, e) => HandleBarClick(PlayerColor.White);
        
        BarAreas[1].Click += (s, e) => HandleBarClick(PlayerColor.Black);
        foreach (Control c in BarAreas[1].Controls) c.Click += (s, e) => HandleBarClick(PlayerColor.Black);
        
        BorneOffAreas[0].Click += (s, e) => HandleBorneOffClick(PlayerColor.White);
        foreach (Control c in BorneOffAreas[0].Controls) c.Click += (s, e) => HandleBorneOffClick(PlayerColor.White);
        
        BorneOffAreas[1].Click += (s, e) => HandleBorneOffClick(PlayerColor.Black);
        foreach (Control c in BorneOffAreas[1].Controls) c.Click += (s, e) => HandleBorneOffClick(PlayerColor.Black);
    }
    
    private void StartNewGame()
    {
        _gameEngine = new GameEngine();
        _selectedSourcePoint = null;
        _selectedMove = null;
        _legalMoves.Clear();
        LogLabel.Text = "Durum: Yeni oyun basladi.";
        UpdateUiState();
    }
    
    private void RollDice()
    {
        if (_gameEngine == null) return;
        
        var (die1, die2) = _gameEngine.RollDice();
        _gameEngine.StartTurn(_gameEngine.CurrentTurn, die1, die2);
        
        _selectedSourcePoint = null;
        _selectedMove = null;
        _legalMoves = _gameEngine.GenerateLegalMoves(_gameEngine.CurrentTurn).ToList();
        
        if (_legalMoves.Count == 0)
        {
            LogLabel.Text = $"Durum: {_gameEngine.CurrentTurn} icin gecerli hamle yok. Tur geciliyor.";
            _gameEngine.AdvanceTurn();
        }
        else
        {
            LogLabel.Text = $"Durum: Zar atildi. Gecerli hamle bekleniyor.";
        }
        
        UpdateUiState();
    }
    
    private void HandlePointClick(int pointIndex)
    {
        if (_gameEngine == null || _gameEngine.IsTurnComplete || _legalMoves.Count == 0) return;

        // If a source is already selected, try to select destination
        if (_selectedSourcePoint.HasValue)
        {
            var move = _legalMoves.FirstOrDefault(m => m.SourcePoint == _selectedSourcePoint.Value && m.DestinationPoint == pointIndex);
            if (move != null)
            {
                _selectedMove = move;
                LogLabel.Text = $"Durum: Hamle secildi: {move.SourcePoint} -> {move.DestinationPoint}. Uygula'ya basin.";
            }
            else
            {
                // Not a valid destination for the selected source, select the new point as source if it has valid moves
                SelectSourcePoint(pointIndex);
            }
        }
        else
        {
            SelectSourcePoint(pointIndex);
        }
        
        UpdateUiState();
    }
    
    private void HandleBarClick(PlayerColor color)
    {
        if (_gameEngine == null || _gameEngine.IsTurnComplete || _gameEngine.CurrentTurn != color || _legalMoves.Count == 0) return;
        
        int barPointIndex = color == PlayerColor.White ? 0 : 25;
        SelectSourcePoint(barPointIndex);
        UpdateUiState();
    }
    
    private void HandleBorneOffClick(PlayerColor color)
    {
        if (_gameEngine == null || _gameEngine.IsTurnComplete || _legalMoves.Count == 0) return;
        
        if (_selectedSourcePoint.HasValue)
        {
            int offPointIndex = color == PlayerColor.White ? 25 : 0;
            var move = _legalMoves.FirstOrDefault(m => m.SourcePoint == _selectedSourcePoint.Value && m.DestinationPoint == offPointIndex);
            if (move != null)
            {
                _selectedMove = move;
                LogLabel.Text = $"Durum: Hamle secildi: {move.SourcePoint} -> {move.DestinationPoint} (Toplama). Uygula'ya basin.";
                UpdateUiState();
            }
        }
    }
    
    private void SelectSourcePoint(int pointIndex)
    {
        if (_legalMoves.Any(m => m.SourcePoint == pointIndex))
        {
            _selectedSourcePoint = pointIndex;
            _selectedMove = null;
            LogLabel.Text = $"Durum: {pointIndex}. hane secildi. Hedef secin.";
        }
        else
        {
            _selectedSourcePoint = null;
            _selectedMove = null;
        }
    }
    
    private void ApplySelectedMove()
    {
        if (_gameEngine == null || _selectedMove == null) return;
        
        bool success = _gameEngine.ApplyMove(_selectedMove);
        if (success)
        {
            LogLabel.Text = $"Durum: Hamle uygulandi.";
            _selectedSourcePoint = null;
            _selectedMove = null;
            
            if (_gameEngine.IsTurnComplete)
            {
                _legalMoves.Clear();
                LogLabel.Text = $"Durum: Tur tamamlandi.";
            }
            else
            {
                _legalMoves = _gameEngine.GenerateLegalMoves(_gameEngine.CurrentTurn).ToList();
                if (_legalMoves.Count == 0)
                {
                    LogLabel.Text = $"Durum: Diger zarlar icin gecerli hamle yok. Tur geciliyor.";
                    _gameEngine.AdvanceTurn();
                }
            }
        }
        else
        {
            LogLabel.Text = $"Durum: Hamle uygulanamadi!";
        }
        
        UpdateUiState();
    }
    

    private void PlayAiTurn()
    {
        if (_gameEngine == null) return;
        
        // If turn hasn't started or dice not rolled for the active player, roll dice
        if (_gameEngine.RemainingDice.Count == 0 && !_gameEngine.IsTurnComplete)
        {
            var (die1, die2) = _gameEngine.RollDice();
            _gameEngine.StartTurn(_gameEngine.CurrentTurn, die1, die2);
        }

        if (_gameEngine.RemainingDice.Count == 0 || _gameEngine.IsTurnComplete)
        {
            return;
        }

        var sequence = _aiService.GetBestMoveSequence(_gameEngine);
        
        if (sequence.Count == 0)
        {
            LogLabel.Text = $"Durum: AI {_gameEngine.CurrentTurn} icin gecerli hamle yok. Tur geciliyor.";
            _gameEngine.AdvanceTurn();
        }
        else
        {
            LogLabel.Text = $"Durum: AI Hamlesi: {sequence.Count} hamle yapildi.";
            foreach (var move in sequence)
            {
                _gameEngine.ApplyMove(move);
            }
        }
        
        _selectedSourcePoint = null;
        _selectedMove = null;
        _legalMoves.Clear();
        if (_gameEngine.RemainingDice.Count > 0 && !_gameEngine.IsTurnComplete)
        {
            _legalMoves = _gameEngine.GenerateLegalMoves(_gameEngine.CurrentTurn).ToList();
        }
        UpdateUiState();
        RenderBoard(_gameEngine);
    }

    private void UpdateUiState()
    {
        if (_gameEngine == null)
        {
            RollDiceButton.Enabled = false;
            ApplyMoveButton.Enabled = false;
            CurrentPlayerLabel.Text = "Sira: Bekleniyor...";
            RenderBoard(null);
            return;
        }

        RollDiceButton.Enabled = _gameEngine.IsTurnComplete;
        AiMoveButton.Enabled = true;
        ApplyMoveButton.Enabled = _selectedMove != null;
        CurrentPlayerLabel.Text = $"Sira: {_gameEngine.CurrentTurn} (Tur {_gameEngine.TurnNumber})";
        
        var diceLabel = (Label)DiceDisplay.Controls["DiceLabel"];
        if (_gameEngine.RemainingDice.Count > 0)
        {
            diceLabel.Text = string.Join(", ", _gameEngine.RemainingDice);
        }
        else
        {
            diceLabel.Text = "Zarlar";
        }
        
        RenderBoard(_gameEngine);
    }
    
    private void RenderBoard(GameEngine engine)
    {
        // Reset colors
        for (int i = 0; i < 24; i++)
        {
            Points[i].BackColor = (i % 2 == 0) ? Color.Wheat : Color.SaddleBrown;
            
            // Remove existing checker labels except the index label
            var controlsToRemove = Points[i].Controls.OfType<Label>().Where(l => l.Name == "CheckerCount").ToList();
            foreach (var c in controlsToRemove) Points[i].Controls.Remove(c);
        }
        
        BarAreas[0].BackColor = Color.DarkGoldenrod;
        BarAreas[1].BackColor = Color.DarkGoldenrod;
        BorneOffAreas[0].BackColor = Color.DarkKhaki;
        BorneOffAreas[1].BackColor = Color.DarkKhaki;
        
        var barControlsToRemove0 = BarAreas[0].Controls.OfType<Label>().Where(l => l.Name == "CheckerCount").ToList();
        foreach (var c in barControlsToRemove0) BarAreas[0].Controls.Remove(c);
        var barControlsToRemove1 = BarAreas[1].Controls.OfType<Label>().Where(l => l.Name == "CheckerCount").ToList();
        foreach (var c in barControlsToRemove1) BarAreas[1].Controls.Remove(c);
        
        var offControlsToRemove0 = BorneOffAreas[0].Controls.OfType<Label>().Where(l => l.Name == "CheckerCount").ToList();
        foreach (var c in offControlsToRemove0) BorneOffAreas[0].Controls.Remove(c);
        var offControlsToRemove1 = BorneOffAreas[1].Controls.OfType<Label>().Where(l => l.Name == "CheckerCount").ToList();
        foreach (var c in offControlsToRemove1) BorneOffAreas[1].Controls.Remove(c);
        
        if (engine == null) return;
        
        var snapshot = engine.CaptureGameStateSnapshot();
        
        // Render Points
        foreach (var p in snapshot.Points)
        {
            if (p.CheckerCount > 0)
            {
                int pIndex = p.Index - 1;
                AddCheckerLabel(Points[pIndex], p.Color, p.CheckerCount);
            }
        }
        
        // Render Bars
        if (snapshot.WhiteCheckersOnBar > 0)
            AddCheckerLabel(BarAreas[0], PlayerColor.White, snapshot.WhiteCheckersOnBar);
        if (snapshot.BlackCheckersOnBar > 0)
            AddCheckerLabel(BarAreas[1], PlayerColor.Black, snapshot.BlackCheckersOnBar);
            
        // Render Borne Off
        if (snapshot.WhiteCheckersBorneOff > 0)
            AddCheckerLabel(BorneOffAreas[0], PlayerColor.White, snapshot.WhiteCheckersBorneOff);
        if (snapshot.BlackCheckersBorneOff > 0)
            AddCheckerLabel(BorneOffAreas[1], PlayerColor.Black, snapshot.BlackCheckersBorneOff);
            
        // Highlight selection
        if (_selectedSourcePoint.HasValue)
        {
            int source = _selectedSourcePoint.Value;
            if (source >= 1 && source <= 24)
            {
                Points[source - 1].BackColor = Color.LightYellow;
            }
            else if (source == 0)
            {
                BarAreas[0].BackColor = Color.LightYellow;
            }
            else if (source == 25)
            {
                BarAreas[1].BackColor = Color.LightYellow;
            }
            
            // Highlight possible destinations
            foreach (var m in _legalMoves.Where(m => m.SourcePoint == source))
            {
                int dest = m.DestinationPoint;
                if (dest >= 1 && dest <= 24)
                {
                    Points[dest - 1].BackColor = Color.LightGreen;
                }
                else if (dest == 25)
                {
                    BorneOffAreas[0].BackColor = Color.LightGreen;
                }
                else if (dest == 0)
                {
                    BorneOffAreas[1].BackColor = Color.LightGreen;
                }
            }
            
            // Highlight selected move destination in blue
            if (_selectedMove != null)
            {
                int selectedDest = _selectedMove.DestinationPoint;
                if (selectedDest >= 1 && selectedDest <= 24)
                {
                    Points[selectedDest - 1].BackColor = Color.LightBlue;
                }
                else if (selectedDest == 25)
                {
                    BorneOffAreas[0].BackColor = Color.LightBlue;
                }
                else if (selectedDest == 0)
                {
                    BorneOffAreas[1].BackColor = Color.LightBlue;
                }
            }
        }
    }
    
    private void AddCheckerLabel(Panel panel, PlayerColor color, int count)
    {
        Label l = new Label
        {
            Name = "CheckerCount",
            Text = $"{count}",
            ForeColor = color == PlayerColor.White ? Color.Black : Color.White,
            BackColor = color == PlayerColor.White ? Color.White : Color.Black,
            AutoSize = false,
            Size = new Size(30, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Location = new System.Drawing.Point((panel.Width - 30) / 2, (panel.Height - 30) / 2)
        };
        
        // Pass click to parent panel
        l.Click += (s, e) => 
        {
            // Simulate click on the parent panel
            typeof(Panel).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(panel, new object[] { EventArgs.Empty });
        };
        
        panel.Controls.Add(l);
        l.BringToFront();
    }
    
    private Button CreateButton(string text, int x)
    {
        return new Button
        {
            Text = text,
            Location = new System.Drawing.Point(x, 15),
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
            Location = new System.Drawing.Point(x, y),
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
            Location = new System.Drawing.Point(2, isTop ? 2 : height - 20)
        };
        p.Controls.Add(l);

        return p;
    }

    private async void OnSaveGameClick(object? sender, EventArgs e)
    {
        if (_persistenceService == null)
        {
            LogMessage("DB baglantisi yok (TAVLA_ONLINE_MYSQL bulunamadi).");
            return;
        }

        if (_gameEngine == null)
        {
            LogMessage("Kaydedilecek aktif bir oyun yok.");
            return;
        }

        try
        {
            var snapshot = _gameEngine.CaptureGameStateSnapshot();
            await _persistenceService.SaveSnapshotAsync(snapshot, "smoke-test-game");
            LogMessage("Oyun basariyla kaydedildi.");
        }
        catch (Exception ex)
        {
            LogMessage($"Kaydetme hatasi: {ex.Message}");
        }
    }

    private async void OnLoadGameClick(object? sender, EventArgs e)
    {
        if (_persistenceService == null)
        {
            LogMessage("DB baglantisi yok (TAVLA_ONLINE_MYSQL bulunamadi).");
            return;
        }

        try
        {
            var snapshot = await _persistenceService.LoadSnapshotAsync("smoke-test-game");
            if (snapshot == null)
            {
                LogMessage("Kayitli oyun bulunamadi.");
                return;
            }

            var board = new Board();
            foreach (var point in snapshot.Points)
            {
                if (point.CheckerCount > 0)
                {
                    board.Points[point.Index] = new TavlaJules.Engine.Models.Point(point.Index) { Color = point.Color, CheckerCount = point.CheckerCount };
                }
            }
            board.WhiteCheckersOnBar = snapshot.WhiteCheckersOnBar;
            board.BlackCheckersOnBar = snapshot.BlackCheckersOnBar;
            board.WhiteCheckersBorneOff = snapshot.WhiteCheckersBorneOff;
            board.BlackCheckersBorneOff = snapshot.BlackCheckersBorneOff;

            _gameEngine = new GameEngine(board);
            _gameEngine.SetTurn(snapshot.CurrentTurn);
            
            if (snapshot.RemainingDice.Count > 0)
            {
                _gameEngine.StartTurn(snapshot.CurrentTurn, snapshot.RemainingDice[0], snapshot.RemainingDice.Count > 1 ? snapshot.RemainingDice[1] : 0);
                // Adjust if there were more than 2 dice originally or if the array represents current state exactly
                // We clear and refill remaining dice manually to match snapshot
                var remainingDiceField = typeof(GameEngine).GetField("_remainingDice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (remainingDiceField != null)
                {
                    remainingDiceField.SetValue(_gameEngine, snapshot.RemainingDice.ToList());
                }
            }
            else
            {
                // Clear remaining dice if empty
                 var remainingDiceField = typeof(GameEngine).GetField("_remainingDice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                 if (remainingDiceField != null)
                 {
                     remainingDiceField.SetValue(_gameEngine, new List<int>());
                 }
            }

            _legalMoves.Clear();
            _selectedSourcePoint = null;
            _selectedMove = null!;

            UpdateUiState();
            RenderBoard(_gameEngine);
            LogMessage("Oyun basariyla yuklendi.");
        }
        catch (Exception ex)
        {
            LogMessage($"Yukleme hatasi: {ex.Message}");
        }
    }

    private void LogMessage(string message)
    {
        LogLabel.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
    }
}
