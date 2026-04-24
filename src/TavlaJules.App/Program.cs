namespace TavlaJules.App;

static class Program
{
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        if (args.Contains("--db-setup", StringComparer.OrdinalIgnoreCase))
        {
            return await RunDatabaseSetupAsync();
        }

        if (args.Contains("--agent-once", StringComparer.OrdinalIgnoreCase))
        {
            return await RunAgentOnceAsync();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }    

    private static async Task<int> RunDatabaseSetupAsync()
    {
        var settingsService = new Services.ProjectSettingsService();
        var settings = settingsService.Load();
        var envFileService = new Services.EnvFileService();
        var connectionString = envFileService.GetValue(settings.ProjectFolder, "AJANLARIM_MYSQL")
            ?? envFileService.GetValue(settings.ProjectFolder, "TAVLA_ONLINE_MYSQL");

        var reporter = new Services.AgentSqlReporter();
        var now = DateTimeOffset.Now;
        var report = new Models.AgentSqlRunReport
        {
            Status = "completed",
            TriggerSource = "db_setup",
            StartedAt = now,
            CompletedAt = now,
            DurationMs = 0,
            Model = settings.AgentModel,
            TrackedJulesSessionId = settings.TrackedJulesSessionId,
            GitHubRepo = settings.GitHubRepo,
            StatusSummary = "tavlajules SQL rapor semasi hazirlandi.",
            WhatJulesDid = "Bu kayit Jules gorevi degil, DB setup dogrulamasidir.",
            NextPrompt = "",
            ShouldStartNewJulesSession = false,
            DatabasePlan = "agent_registry, agent_runs, agent_events ve agent_jules_sessions tablolarini kullan.",
            RiskNotesJson = "[]",
            AnalysisJson = "{\"statusSummary\":\"tavlajules SQL rapor semasi hazirlandi.\"}",
            ErrorText = "",
            JulesSessionsRaw = ""
        };
        var events = new[]
        {
            new Models.AgentEvent
            {
                EventType = "db_setup",
                Severity = "info",
                Message = "tavlajules SQL rapor semasi dogrulandi.",
                MetadataJson = "{\"source\":\"--db-setup\"}"
            }
        };

        try
        {
            var message = await reporter.WriteRunAsync(connectionString, settings, report, events);
            Console.WriteLine(message);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> RunAgentOnceAsync()
    {
        var settingsService = new Services.ProjectSettingsService();
        var settings = settingsService.Load();
        var envFileService = new Services.EnvFileService();
        var apiKey = envFileService.GetValue(settings.ProjectFolder, "OPENROUTER_API_KEY") ?? "";
        var connectionString = envFileService.GetValue(settings.ProjectFolder, "AJANLARIM_MYSQL")
            ?? envFileService.GetValue(settings.ProjectFolder, "TAVLA_ONLINE_MYSQL");

        try
        {
            var result = await new Services.TavlaAgentService().RunOnceAsync(settings, apiKey, connectionString);
            if (!string.IsNullOrWhiteSpace(result.NewJulesSessionId))
            {
                settings.TrackedJulesSessionId = result.NewJulesSessionId;
                settingsService.Save(settings);
                Console.WriteLine($"Yeni Jules session: {result.NewJulesSessionId}");
            }

            Console.WriteLine(result.SqlReportMessage);
            Console.WriteLine(result.ReportPath);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
