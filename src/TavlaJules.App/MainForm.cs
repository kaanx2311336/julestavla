using System.Diagnostics;
using TavlaJules.App.Models;
using TavlaJules.App.Services;

namespace TavlaJules.App;

public sealed class MainForm : Form
{
    private readonly ProjectSettingsService settingsService = new();
    private readonly PhasePlanService phasePlanService = new();
    private readonly EnvFileService envFileService = new();
    private readonly OpenRouterClient openRouterClient = new();
    private readonly JulesCliService julesCliService = new();
    private readonly TavlaAgentService tavlaAgentService = new();
    private readonly DatabaseHealthService databaseHealthService = new();
    private readonly System.Windows.Forms.Timer agentTimer = new();

    private readonly TextBox folderTextBox = new();
    private readonly TextBox repoTextBox = new();
    private readonly TextBox julesUrlTextBox = new();
    private readonly TextBox modelTextBox = new();
    private readonly TextBox fallbackModelsTextBox = new();
    private readonly TextBox apiKeyTextBox = new();
    private readonly TextBox sessionIdTextBox = new();
    private readonly TextBox dbConnectionTextBox = new();
    private readonly CheckBox autoJulesCheckBox = new();
    private readonly TextBox goalTextBox = new();
    private readonly TextBox julesPromptTextBox = new();
    private readonly ListView phaseListView = new();
    private readonly TextBox logTextBox = new();
    private readonly Label statusLabel = new();
    private readonly Button agentToggleButton = new();

    private ProjectSettings settings = new();
    private bool agentIsRunning;
    private bool agentTickInProgress;

    public MainForm()
    {
        settings = settingsService.Load();
        InitializeWindow();
        BuildLayout();
        LoadSettingsToUi();
        ConfigureAgentTimer();
        RefreshPhaseList();
        AppendLog("TavlaJules kontrol paneli hazir.");
    }

    private void InitializeWindow()
    {
        Text = "TavlaJules";
        MinimumSize = new Size(980, 640);
        Size = new Size(1160, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 18, 32);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildStatusBar(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = CreatePanel(new Padding(0));
        panel.Padding = new Padding(18, 10, 18, 10);

        var title = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = 520,
            Text = "TavlaJules Kontrol Paneli",
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 20F, FontStyle.Bold)
        };

        var subtitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Jules CLI, GitHub repo ve OpenRouter kontrolu tek ekranda.",
            ForeColor = Color.FromArgb(162, 176, 205),
            TextAlign = ContentAlignment.MiddleRight
        };

        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BackColor,
            Padding = new Padding(0, 12, 0, 8)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        body.Controls.Add(BuildSettingsPanel(), 0, 0);
        body.Controls.Add(BuildPlanPanel(), 1, 0);
        return body;
    }

    private Control BuildSettingsPanel()
    {
        var panel = CreatePanel(new Padding(0, 0, 14, 0));
        panel.Padding = new Padding(14);
        panel.AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 14,
            BackColor = panel.BackColor
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        layout.Controls.Add(CreateSectionTitle("Ayarlar"), 0, 0);
        layout.Controls.Add(CreateInputGroup("Proje klasoru", folderTextBox), 0, 1);
        layout.Controls.Add(CreateInputGroup("GitHub repo", repoTextBox), 0, 2);
        layout.Controls.Add(CreateInputGroup("Jules adresi", julesUrlTextBox), 0, 3);
        layout.Controls.Add(CreateInputGroup("OpenRouter model", modelTextBox), 0, 4);
        layout.Controls.Add(CreateInputGroup("Fallback model(ler)", fallbackModelsTextBox), 0, 5);
        layout.Controls.Add(CreateApiKeyGroup(), 0, 6);
        layout.Controls.Add(CreateGoalGroup(), 0, 7);
        layout.Controls.Add(CreateHintLabel("Anahtar .env icinde tutulur ve git'e eklenmez. Alan bos kalirsa mevcut anahtar korunur."), 0, 8);
        layout.Controls.Add(CreateButton("Ayarlari kaydet", SaveSettings), 0, 9);
        layout.Controls.Add(CreateButton("OpenRouter baglantisini test et", async (_, _) => await TestOpenRouterAsync()), 0, 10);
        layout.Controls.Add(CreateButton("Jules CLI test et", async (_, _) => await TestJulesCliAsync()), 0, 11);
        layout.Controls.Add(CreateButton("Proje klasorunu ac", OpenProjectFolder), 0, 12);
        layout.Controls.Add(CreateButton("Faz planini yenile", (_, _) => RefreshPhaseList()), 0, 13);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildPlanPanel()
    {
        var panel = CreatePanel(new Padding(0));
        panel.Padding = new Padding(14);

        phaseListView.Dock = DockStyle.Fill;
        phaseListView.View = View.Details;
        phaseListView.FullRowSelect = true;
        phaseListView.GridLines = false;
        phaseListView.BorderStyle = BorderStyle.None;
        phaseListView.BackColor = Color.FromArgb(23, 31, 50);
        phaseListView.ForeColor = Color.FromArgb(230, 236, 248);
        phaseListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        phaseListView.Columns.Add("Faz", 56);
        phaseListView.Columns.Add("Baslik", 190);
        phaseListView.Columns.Add("Aciklama", 520);

        StyleTextBox(julesPromptTextBox, true);
        julesPromptTextBox.Text = CreateDefaultJulesPrompt();

        var julesButtonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        julesButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        julesButtonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        julesButtonRow.Controls.Add(CreateButton("Jules gorevini baslat", async (_, _) => await StartJulesTaskAsync()), 0, 0);
        julesButtonRow.Controls.Add(CreateButton("GitHub reposunu ac", OpenGitHubRepo), 1, 0);

        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Multiline = true;
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.BorderStyle = BorderStyle.None;
        logTextBox.BackColor = Color.FromArgb(9, 13, 24);
        logTextBox.ForeColor = Color.FromArgb(184, 211, 255);
        logTextBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

        var tabs = CreateTabs();
        tabs.TabPages.Add(CreateTabPage("Yol Haritasi", phaseListView));
        tabs.TabPages.Add(CreateTabPage("Ajan", BuildAgentPanel()));
        tabs.TabPages.Add(CreateTabPage("Jules Prompt", BuildPromptTab(julesButtonRow)));
        tabs.TabPages.Add(CreateTabPage("Gunluk", logTextBox));

        panel.Controls.Add(tabs);
        return panel;
    }

    private TabControl CreateTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(120, 30),
            SizeMode = TabSizeMode.Fixed
        };
        tabs.DrawItem += (_, e) =>
        {
            var page = tabs.TabPages[e.Index];
            var selected = e.Index == tabs.SelectedIndex;
            using var backBrush = new SolidBrush(selected ? Color.FromArgb(38, 93, 221) : Color.FromArgb(23, 31, 50));
            using var textBrush = new SolidBrush(Color.White);
            e.Graphics.FillRectangle(backBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, page.Text, Font, e.Bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        return tabs;
    }

    private TabPage CreateTabPage(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.FromArgb(18, 25, 43),
            Padding = new Padding(8)
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private Control BuildPromptTab(Control buttonRow)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(julesPromptTextBox, 0, 0);
        layout.Controls.Add(buttonRow, 0, 1);
        return layout;
    }

    private Control BuildAgentPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        layout.Controls.Add(CreateInputGroup("tavlajules izlenen session", sessionIdTextBox), 0, 0);
        layout.Controls.Add(CreateDbConnectionGroup(), 1, 0);

        autoJulesCheckBox.Dock = DockStyle.Fill;
        autoJulesCheckBox.Text = "tavlajules onerirse yeni Jules gorevini otomatik baslat";
        autoJulesCheckBox.ForeColor = Color.FromArgb(162, 176, 205);
        autoJulesCheckBox.BackColor = Color.Transparent;
        layout.Controls.Add(autoJulesCheckBox, 0, 1);

        var intervalLabel = CreateHintLabel($"Ajan araligi: {settings.AgentIntervalSeconds} saniye");
        layout.Controls.Add(intervalLabel, 1, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));

        agentToggleButton.Dock = DockStyle.Fill;
        agentToggleButton.Text = "Ajan baslat";
        agentToggleButton.FlatStyle = FlatStyle.Flat;
        agentToggleButton.BackColor = Color.FromArgb(38, 93, 221);
        agentToggleButton.ForeColor = Color.White;
        agentToggleButton.Margin = new Padding(0, 3, 6, 3);
        agentToggleButton.Cursor = Cursors.Hand;
        agentToggleButton.FlatAppearance.BorderSize = 0;
        agentToggleButton.Click += (_, _) => ToggleAgent();

        buttons.Controls.Add(agentToggleButton, 0, 0);
        buttons.Controls.Add(CreateButton("Ajan tek tur", async (_, _) => await RunAgentOnceAsync()), 1, 0);
        buttons.Controls.Add(CreateButton("DB test", async (_, _) => await TestDatabaseAsync()), 2, 0);
        layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(buttons, 0, 2);

        return layout;
    }

    private Control BuildStatusBar()
    {
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.FromArgb(142, 157, 190);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Text = "Hazir";
        return statusLabel;
    }

    private Panel CreatePanel(Padding margin)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 25, 43),
            Margin = margin
        };
    }

    private Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private Control CreateInputGroup(string labelText, TextBox textBox)
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 5)
        };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = labelText,
            ForeColor = Color.FromArgb(162, 176, 205),
            TextAlign = ContentAlignment.MiddleLeft
        };

        StyleTextBox(textBox, false);
        group.Controls.Add(label, 0, 0);
        group.Controls.Add(textBox, 0, 1);
        return group;
    }

    private Control CreateApiKeyGroup()
    {
        var group = (TableLayoutPanel)CreateInputGroup("OpenRouter API key", apiKeyTextBox);
        apiKeyTextBox.UseSystemPasswordChar = true;
        return group;
    }

    private Control CreateDbConnectionGroup()
    {
        var group = (TableLayoutPanel)CreateInputGroup("Aiven ajanlarim MySQL", dbConnectionTextBox);
        dbConnectionTextBox.UseSystemPasswordChar = true;
        return group;
    }

    private Control CreateGoalGroup()
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 6)
        };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Genel hedef",
            ForeColor = Color.FromArgb(162, 176, 205),
            TextAlign = ContentAlignment.MiddleLeft
        };

        StyleTextBox(goalTextBox, true);
        group.Controls.Add(label, 0, 0);
        group.Controls.Add(goalTextBox, 0, 1);
        return group;
    }

    private static Label CreateHintLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Color.FromArgb(122, 139, 175),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 93, 221),
            ForeColor = Color.White,
            Margin = new Padding(0, 3, 6, 3),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += handler;
        return button;
    }

    private static void StyleTextBox(TextBox textBox, bool multiline)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Multiline = multiline;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.FromArgb(9, 13, 24);
        textBox.ForeColor = Color.FromArgb(235, 241, 255);
        textBox.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
    }

    private void LoadSettingsToUi()
    {
        folderTextBox.Text = settings.ProjectFolder;
        repoTextBox.Text = settings.GitHubRepo;
        julesUrlTextBox.Text = settings.JulesUrl;
        modelTextBox.Text = settings.OpenRouterModel;
        fallbackModelsTextBox.Text = settings.OpenRouterFallbackModels;
        sessionIdTextBox.Text = settings.TrackedJulesSessionId;
        autoJulesCheckBox.Checked = settings.AllowAutoJulesSessions;
        goalTextBox.Text = settings.Goal;
        UpdateApiKeyPlaceholder();
        UpdateDatabasePlaceholder();
    }

    private void CaptureSettingsFromUi()
    {
        settings.ProjectFolder = folderTextBox.Text.Trim();
        settings.GitHubRepo = repoTextBox.Text.Trim();
        settings.JulesUrl = julesUrlTextBox.Text.Trim();
        settings.OpenRouterModel = modelTextBox.Text.Trim();
        settings.OpenRouterFallbackModels = fallbackModelsTextBox.Text.Trim();
        settings.AgentModel = settings.OpenRouterModel;
        settings.TrackedJulesSessionId = sessionIdTextBox.Text.Trim();
        settings.AllowAutoJulesSessions = autoJulesCheckBox.Checked;
        settings.Goal = goalTextBox.Text.Trim();
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        CaptureSettingsFromUi();
        settingsService.Save(settings);
        SaveEnvValues();
        RefreshPhaseList();
        AppendLog($"Ayarlar kaydedildi: {settingsService.SettingsPath}");
        statusLabel.Text = "Ayarlar kaydedildi";
    }

    private void SaveEnvValues()
    {
        var values = new Dictionary<string, string>
        {
            ["AGENT_NAME"] = settings.AgentName,
            ["OPENROUTER_MODEL"] = settings.OpenRouterModel,
            ["OPENROUTER_AGENT_MODEL"] = settings.AgentModel,
            ["OPENROUTER_FALLBACK_MODELS"] = settings.OpenRouterFallbackModels,
            ["OPENROUTER_API_URL"] = settings.OpenRouterEndpoint,
            ["JULES_URL"] = settings.JulesUrl,
            ["GITHUB_REPO"] = settings.GitHubRepo
        };

        var typedApiKey = apiKeyTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(typedApiKey))
        {
            values["OPENROUTER_API_KEY"] = typedApiKey;
        }

        var typedDbConnection = dbConnectionTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(typedDbConnection))
        {
            values["AJANLARIM_MYSQL"] = typedDbConnection;
        }

        envFileService.UpsertValues(settings.ProjectFolder, values);
        apiKeyTextBox.Clear();
        dbConnectionTextBox.Clear();
        UpdateApiKeyPlaceholder();
        UpdateDatabasePlaceholder();
    }

    private async Task TestOpenRouterAsync()
    {
        try
        {
            CaptureSettingsFromUi();
            settingsService.Save(settings);
            SaveEnvValues();

            statusLabel.Text = "OpenRouter test ediliyor...";
            AppendLog("OpenRouter baglanti testi basladi.");
            var apiKey = envFileService.GetValue(settings.ProjectFolder, "OPENROUTER_API_KEY") ?? "";
            var result = await openRouterClient.TestConnectionAsync(settings, apiKey);
            AppendLog($"OpenRouter yaniti: {result}");
            statusLabel.Text = "OpenRouter baglantisi tamam";
        }
        catch (Exception exception)
        {
            AppendLog($"OpenRouter testi basarisiz: {exception.Message}");
            statusLabel.Text = "OpenRouter testi basarisiz";
        }
    }

    private async Task TestJulesCliAsync()
    {
        try
        {
            CaptureSettingsFromUi();
            settingsService.Save(settings);
            SaveEnvValues();

            statusLabel.Text = "Jules CLI test ediliyor...";
            AppendLog("Jules CLI version testi basladi.");
            var result = await julesCliService.VersionAsync(settings);
            AppendCommandResult("Jules CLI", result);
            statusLabel.Text = result.IsSuccess ? "Jules CLI hazir" : "Jules CLI hata verdi";
        }
        catch (Exception exception)
        {
            AppendLog($"Jules CLI testi basarisiz: {exception.Message}");
            statusLabel.Text = "Jules CLI testi basarisiz";
        }
    }

    private async Task StartJulesTaskAsync()
    {
        try
        {
            CaptureSettingsFromUi();
            settingsService.Save(settings);
            SaveEnvValues();

            var prompt = julesPromptTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                AppendLog("Jules promptu bos oldugu icin gorev baslatilmadi.");
                return;
            }

            var confirmation = MessageBox.Show(
                $"Jules icin yeni remote session baslatilsin mi?\n\nRepo: {settings.GitHubRepo}",
                "Jules gorevi",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
            {
                AppendLog("Jules gorevi kullanici tarafindan iptal edildi.");
                return;
            }

            statusLabel.Text = "Jules gorevi baslatiliyor...";
            AppendLog($"Jules gorevi baslatiliyor: {settings.GitHubRepo}");
            var result = await julesCliService.CreateSessionAsync(settings, prompt);
            AppendCommandResult("Jules gorevi", result);
            statusLabel.Text = result.IsSuccess ? "Jules gorevi baslatildi" : "Jules gorevi hata verdi";
        }
        catch (Exception exception)
        {
            AppendLog($"Jules gorevi basarisiz: {exception.Message}");
            statusLabel.Text = "Jules gorevi basarisiz";
        }
    }

    private void ConfigureAgentTimer()
    {
        agentTimer.Interval = Math.Max(15, settings.AgentIntervalSeconds) * 1000;
        agentTimer.Tick += async (_, _) =>
        {
            if (agentTickInProgress)
            {
                AppendLog("Ajan onceki turu bitirmedigi icin bu dakika atlandi.");
                return;
            }

            await RunAgentOnceAsync();
        };
    }

    private void ToggleAgent()
    {
        CaptureSettingsFromUi();
        settingsService.Save(settings);
        SaveEnvValues();

        agentIsRunning = !agentIsRunning;
        agentTimer.Interval = Math.Max(15, settings.AgentIntervalSeconds) * 1000;
        agentToggleButton.Text = agentIsRunning ? "Ajan durdur" : "Ajan baslat";

        if (agentIsRunning)
        {
            agentTimer.Start();
            AppendLog("Dakikalik TavlaJules ajani baslatildi.");
            statusLabel.Text = "Ajan izliyor";
        }
        else
        {
            agentTimer.Stop();
            AppendLog("Dakikalik TavlaJules ajani durduruldu.");
            statusLabel.Text = "Ajan durdu";
        }
    }

    private async Task RunAgentOnceAsync()
    {
        if (agentTickInProgress)
        {
            return;
        }

        try
        {
            agentTickInProgress = true;
            CaptureSettingsFromUi();
            settingsService.Save(settings);
            SaveEnvValues();

            var apiKey = envFileService.GetValue(settings.ProjectFolder, "OPENROUTER_API_KEY") ?? "";
            var dbConnection = GetAgentDatabaseConnectionString();

            AppendLog("TavlaJules ajani tek tur basladi.");
            statusLabel.Text = "Ajan Jules ve OpenRouter kontrol ediyor...";
            var result = await tavlaAgentService.RunOnceAsync(settings, apiKey, dbConnection);

            AppendLog($"Ajan modeli: {result.UsedModel}");
            AppendLog($"Ajan raporu: {result.ReportPath}");
            AppendLog(result.SqlReportMessage);
            AppendLog(TrimForLog(result.Analysis));

            if (!string.IsNullOrWhiteSpace(result.NextPrompt))
            {
                julesPromptTextBox.Text = result.NextPrompt;
            }

            if (result.AutoJulesSessionResult is not null)
            {
                AppendCommandResult("Ajan otomatik Jules gorevi", result.AutoJulesSessionResult);
            }

            statusLabel.Text = "Ajan turu tamamlandi";
        }
        catch (Exception exception)
        {
            AppendLog($"Ajan turu basarisiz: {exception.Message}");
            statusLabel.Text = "Ajan hatasi";
        }
        finally
        {
            agentTickInProgress = false;
        }
    }

    private async Task TestDatabaseAsync()
    {
        try
        {
            CaptureSettingsFromUi();
            settingsService.Save(settings);
            SaveEnvValues();

            AppendLog("ajanlarim DB testi basladi.");
            var connectionString = GetAgentDatabaseConnectionString();
            var result = await databaseHealthService.TestAsync(connectionString);
            AppendLog(result.Message);
            statusLabel.Text = result.IsSuccess ? "DB baglantisi tamam" : "DB baglantisi hazir degil";
        }
        catch (Exception exception)
        {
            AppendLog($"DB testi basarisiz: {exception.Message}");
            statusLabel.Text = "DB testi basarisiz";
        }
    }

    private void RefreshPhaseList()
    {
        phaseListView.Items.Clear();

        foreach (var phase in phasePlanService.CreateDefaultPlan(settings))
        {
            var item = new ListViewItem(phase.Order.ToString());
            item.SubItems.Add(phase.IsDone ? $"{phase.Title}  [tamam]" : phase.Title);
            item.SubItems.Add(phase.Description);
            item.BackColor = phase.IsDone ? Color.FromArgb(24, 58, 55) : Color.FromArgb(23, 31, 50);
            item.ForeColor = Color.FromArgb(230, 236, 248);
            phaseListView.Items.Add(item);
        }

        AppendLog("Faz plani yenilendi.");
        statusLabel.Text = "Faz plani hazir";
    }

    private void OpenProjectFolder(object? sender, EventArgs e)
    {
        var folder = folderTextBox.Text.Trim();

        if (!Directory.Exists(folder))
        {
            AppendLog($"Klasor bulunamadi: {folder}");
            statusLabel.Text = "Klasor bulunamadi";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
        AppendLog($"Klasor acildi: {folder}");
    }

    private void OpenGitHubRepo(object? sender, EventArgs e)
    {
        CaptureSettingsFromUi();
        var repoUrl = $"https://github.com/{settings.GitHubRepo}";
        Process.Start(new ProcessStartInfo
        {
            FileName = repoUrl,
            UseShellExecute = true
        });
        AppendLog($"GitHub acildi: {repoUrl}");
    }

    private void AppendCommandResult(string title, CommandResult result)
    {
        AppendLog($"{title} exit code: {result.ExitCode}");

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            AppendLog(TrimForLog(result.Output));
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AppendLog(TrimForLog(result.Error));
        }
    }

    private void UpdateApiKeyPlaceholder()
    {
        apiKeyTextBox.PlaceholderText = envFileService.HasValue(settings.ProjectFolder, "OPENROUTER_API_KEY")
            ? "Mevcut .env anahtari korunacak"
            : "OpenRouter API key gir";
    }

    private void UpdateDatabasePlaceholder()
    {
        dbConnectionTextBox.PlaceholderText = envFileService.HasValue(settings.ProjectFolder, "TAVLA_ONLINE_MYSQL")
            || envFileService.HasValue(settings.ProjectFolder, "AJANLARIM_MYSQL")
            ? "Mevcut .env ajanlarim baglantisi korunacak"
            : "mysql://USER:PASSWORD@HOST:PORT/ajanlarim?ssl-mode=REQUIRED";
    }

    private string? GetAgentDatabaseConnectionString()
    {
        return envFileService.GetValue(settings.ProjectFolder, "AJANLARIM_MYSQL")
            ?? envFileService.GetValue(settings.ProjectFolder, "TAVLA_ONLINE_MYSQL");
    }

    private static string CreateDefaultJulesPrompt()
    {
        return """
        Bu repo TavlaJules projesidir. C# WinForms kontrol paneliyle baslayan proje, telefon icin tavla uygulamasi gelistirme surecini Jules gorevleri ve OpenRouter kontrol raporlariyla yonetecek.

        Ilk Jules gorevi:
        - Repoyu incele.
        - Tavla oyunu icin saf C# kural motoru class library tasarimi oner.
        - Zar, pul hareketi, kirma, kapali hane, toplama ve kazanan kontrolu icin dosya/faz plani cikar.
        - Mevcut WinForms kontrol panelini bozmadan sonraki uygulanabilir adimi raporla.
        - Gizli anahtar veya .env icerigini asla commit etme.
        """;
    }

    private void AppendLog(string message)
    {
        logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static string TrimForLog(string value)
    {
        value = value.Trim();
        const int maxLength = 1800;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
