using TavlaJules.App.Services;
using TavlaJules.Data.Models;

namespace TavlaJules.App.Controls;

public sealed class MatchmakingControl : UserControl
{
    private readonly MatchmakingService? matchmakingService;
    private readonly string localPlayerId = Guid.NewGuid().ToString("N");
    private readonly string localPlayerName;
    private readonly HashSet<string> createdMatchIds = [];

    private readonly Button createMatchButton = new();
    private readonly Button refreshMatchesButton = new();
    private readonly Button joinMatchButton = new();
    private readonly ListView openMatchesListView = new();
    private readonly Label statusLabel = new();

    public MatchmakingControl(MatchmakingService? matchmakingService)
    {
        this.matchmakingService = matchmakingService;
        localPlayerName = $"Oyuncu-{localPlayerId[..4]}";

        InitializeLayout();
        UpdateConnectionState();
    }

    private void InitializeLayout()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(13, 18, 32);
        Padding = new Padding(12);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BackColor
        };

        ConfigureButton(createMatchButton, "Create Match", Color.FromArgb(38, 93, 221));
        ConfigureButton(refreshMatchesButton, "Refresh Matches", Color.FromArgb(46, 134, 89));
        ConfigureButton(joinMatchButton, "Join Match", Color.FromArgb(137, 91, 225));

        createMatchButton.Click += async (_, _) => await CreateMatchAsync();
        refreshMatchesButton.Click += async (_, _) => await RefreshMatchesAsync();
        joinMatchButton.Click += async (_, _) => await JoinSelectedMatchAsync();

        buttonRow.Controls.Add(createMatchButton);
        buttonRow.Controls.Add(refreshMatchesButton);
        buttonRow.Controls.Add(joinMatchButton);

        openMatchesListView.Dock = DockStyle.Fill;
        openMatchesListView.View = View.Details;
        openMatchesListView.FullRowSelect = true;
        openMatchesListView.BorderStyle = BorderStyle.None;
        openMatchesListView.BackColor = Color.FromArgb(23, 31, 50);
        openMatchesListView.ForeColor = Color.FromArgb(230, 236, 248);
        openMatchesListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        openMatchesListView.Columns.Add("Match ID", 260);
        openMatchesListView.Columns.Add("Status", 150);
        openMatchesListView.Columns.Add("Created", 180);
        openMatchesListView.DoubleClick += async (_, _) => await JoinSelectedMatchAsync();

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.FromArgb(184, 211, 255);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        root.Controls.Add(buttonRow, 0, 0);
        root.Controls.Add(openMatchesListView, 0, 1);
        root.Controls.Add(statusLabel, 0, 2);
        Controls.Add(root);
    }

    private void UpdateConnectionState()
    {
        var isReady = matchmakingService is not null;
        createMatchButton.Enabled = isReady;
        refreshMatchesButton.Enabled = isReady;
        joinMatchButton.Enabled = isReady;
        statusLabel.Text = isReady
            ? $"OnlineMatch hazir. Yerel oyuncu: {localPlayerName}"
            : "TAVLA_ONLINE_MYSQL baglantisi yok; online match islemleri devre disi.";
    }

    private async Task CreateMatchAsync()
    {
        if (matchmakingService is null)
        {
            return;
        }

        await RunUiActionAsync("Mac olusturuluyor...", async () =>
        {
            var matchId = await matchmakingService.CreateMatchAsync(localPlayerId, localPlayerName);
            createdMatchIds.Add(matchId);
            statusLabel.Text = $"Create Match tamamlandi: {matchId}";
            await RefreshMatchesAsync();
        });
    }

    private async Task RefreshMatchesAsync()
    {
        if (matchmakingService is null)
        {
            return;
        }

        await RunUiActionAsync("Open Matches yenileniyor...", async () =>
        {
            var matches = await matchmakingService.ListOpenMatchesAsync();
            openMatchesListView.Items.Clear();

            foreach (var match in matches)
            {
                var item = new ListViewItem(match.Id);
                item.SubItems.Add(match.Status);
                item.SubItems.Add(match.CreatedAt == default ? "-" : match.CreatedAt.ToLocalTime().ToString("g"));
                item.Tag = match;
                openMatchesListView.Items.Add(item);
            }

            statusLabel.Text = $"Refresh Matches tamamlandi: {matches.Count} acik mac.";
        });
    }

    private async Task JoinSelectedMatchAsync()
    {
        if (matchmakingService is null || openMatchesListView.SelectedItems.Count == 0)
        {
            return;
        }

        if (openMatchesListView.SelectedItems[0].Tag is not OnlineMatch match)
        {
            return;
        }

        if (createdMatchIds.Contains(match.Id))
        {
            statusLabel.Text = "Join Match reddedildi: kendi actigin maca katilamazsin.";
            return;
        }

        await RunUiActionAsync("Maca katiliniyor...", async () =>
        {
            var joined = await matchmakingService.JoinMatchAsync(match.Id, localPlayerId, localPlayerName);
            statusLabel.Text = joined
                ? $"Join Match tamamlandi: {match.Id}"
                : "Join Match basarisiz: mac dolu veya tamamlanmis olabilir.";
            await RefreshMatchesAsync();
        });
    }

    private async Task RunUiActionAsync(string status, Func<Task> action)
    {
        SetBusy(true, status);
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"Hata: {exception.Message}";
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        if (matchmakingService is not null)
        {
            createMatchButton.Enabled = !busy;
            refreshMatchesButton.Enabled = !busy;
            joinMatchButton.Enabled = !busy;
        }

        statusLabel.Text = status;
    }

    private static void ConfigureButton(Button button, string text, Color backColor)
    {
        button.Text = text;
        button.Width = 150;
        button.Height = 32;
        button.Margin = new Padding(0, 4, 8, 4);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
    }
}
