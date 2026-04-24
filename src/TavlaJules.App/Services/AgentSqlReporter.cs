using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class AgentSqlReporter
{
    public async Task<string> WriteRunAsync(
        string? connectionString,
        ProjectSettings settings,
        AgentSqlRunReport report,
        IReadOnlyList<AgentEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "AJANLARIM_MYSQL .env icinde tanimli degil; SQL raporu atlandi.";
        }

        await using var connection = new MySqlConnection(ConnectionStringService.NormalizeMySql(connectionString));
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        var agentId = await UpsertAgentAsync(connection, settings, cancellationToken);
        var runId = await InsertRunAsync(connection, agentId, settings, report, cancellationToken);

        foreach (var agentEvent in events)
        {
            await InsertEventAsync(connection, agentId, runId, agentEvent, cancellationToken);
        }

        await UpsertJulesSessionsAsync(connection, agentId, report.JulesSessionsRaw, cancellationToken);
        return $"SQL raporu yazildi: agent_runs.id={runId}";
    }

    private static async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS agent_registry (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                agent_name VARCHAR(100) NOT NULL UNIQUE,
                display_name VARCHAR(160) NOT NULL,
                project_folder VARCHAR(512) NULL,
                github_repo VARCHAR(255) NULL,
                created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
                last_seen_at DATETIME(6) NULL,
                INDEX idx_agent_registry_last_seen (last_seen_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS agent_runs (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                run_uuid CHAR(36) NOT NULL UNIQUE,
                agent_id BIGINT NOT NULL,
                agent_name VARCHAR(100) NOT NULL,
                status VARCHAR(32) NOT NULL,
                trigger_source VARCHAR(64) NOT NULL,
                started_at DATETIME(6) NOT NULL,
                completed_at DATETIME(6) NOT NULL,
                duration_ms BIGINT NOT NULL,
                model VARCHAR(160) NULL,
                tracked_jules_session_id VARCHAR(64) NULL,
                github_repo VARCHAR(255) NULL,
                report_path VARCHAR(700) NULL,
                status_summary TEXT NULL,
                what_jules_did TEXT NULL,
                next_prompt MEDIUMTEXT NULL,
                should_start_new_jules_session TINYINT(1) NOT NULL DEFAULT 0,
                database_plan TEXT NULL,
                risk_notes_json LONGTEXT NULL,
                analysis_json LONGTEXT NULL,
                error_text MEDIUMTEXT NULL,
                created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_agent_runs_agent_started (agent_id, started_at),
                INDEX idx_agent_runs_status_started (status, started_at),
                INDEX idx_agent_runs_session (tracked_jules_session_id),
                CONSTRAINT fk_agent_runs_agent FOREIGN KEY (agent_id) REFERENCES agent_registry(id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS agent_events (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                run_id BIGINT NOT NULL,
                agent_id BIGINT NOT NULL,
                event_type VARCHAR(80) NOT NULL,
                severity VARCHAR(20) NOT NULL DEFAULT 'info',
                message MEDIUMTEXT NOT NULL,
                metadata_json LONGTEXT NULL,
                created_at DATETIME(6) NOT NULL,
                INDEX idx_agent_events_run_created (run_id, created_at),
                INDEX idx_agent_events_agent_type_created (agent_id, event_type, created_at),
                CONSTRAINT fk_agent_events_run FOREIGN KEY (run_id) REFERENCES agent_runs(id),
                CONSTRAINT fk_agent_events_agent FOREIGN KEY (agent_id) REFERENCES agent_registry(id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS agent_jules_sessions (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                agent_id BIGINT NOT NULL,
                session_id VARCHAR(64) NOT NULL,
                repo VARCHAR(255) NULL,
                status VARCHAR(80) NULL,
                description TEXT NULL,
                raw_line TEXT NULL,
                last_seen_at DATETIME(6) NOT NULL,
                UNIQUE KEY uq_agent_session (agent_id, session_id),
                INDEX idx_agent_jules_sessions_status (status, last_seen_at),
                CONSTRAINT fk_agent_jules_sessions_agent FOREIGN KEY (agent_id) REFERENCES agent_registry(id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """
        };

        foreach (var sql in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<long> UpsertAgentAsync(MySqlConnection connection, ProjectSettings settings, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_registry (agent_name, display_name, project_folder, github_repo, last_seen_at)
            VALUES (@agent_name, @display_name, @project_folder, @github_repo, CURRENT_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                display_name = VALUES(display_name),
                project_folder = VALUES(project_folder),
                github_repo = VALUES(github_repo),
                last_seen_at = CURRENT_TIMESTAMP(6);
            SELECT id FROM agent_registry WHERE agent_name = @agent_name;
            """;
        command.Parameters.AddWithValue("@agent_name", settings.AgentName);
        command.Parameters.AddWithValue("@display_name", "TavlaJules");
        command.Parameters.AddWithValue("@project_folder", settings.ProjectFolder);
        command.Parameters.AddWithValue("@github_repo", settings.GitHubRepo);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> InsertRunAsync(
        MySqlConnection connection,
        long agentId,
        ProjectSettings settings,
        AgentSqlRunReport report,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_runs (
                run_uuid, agent_id, agent_name, status, trigger_source, started_at, completed_at, duration_ms,
                model, tracked_jules_session_id, github_repo, report_path, status_summary, what_jules_did,
                next_prompt, should_start_new_jules_session, database_plan, risk_notes_json, analysis_json, error_text
            )
            VALUES (
                @run_uuid, @agent_id, @agent_name, @status, @trigger_source, @started_at, @completed_at, @duration_ms,
                @model, @tracked_jules_session_id, @github_repo, @report_path, @status_summary, @what_jules_did,
                @next_prompt, @should_start_new_jules_session, @database_plan, @risk_notes_json, @analysis_json, @error_text
            );
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@run_uuid", report.RunUuid.ToString());
        command.Parameters.AddWithValue("@agent_id", agentId);
        command.Parameters.AddWithValue("@agent_name", settings.AgentName);
        command.Parameters.AddWithValue("@status", report.Status);
        command.Parameters.AddWithValue("@trigger_source", report.TriggerSource);
        command.Parameters.AddWithValue("@started_at", report.StartedAt.LocalDateTime);
        command.Parameters.AddWithValue("@completed_at", report.CompletedAt.LocalDateTime);
        command.Parameters.AddWithValue("@duration_ms", report.DurationMs);
        command.Parameters.AddWithValue("@model", report.Model);
        command.Parameters.AddWithValue("@tracked_jules_session_id", report.TrackedJulesSessionId);
        command.Parameters.AddWithValue("@github_repo", report.GitHubRepo);
        command.Parameters.AddWithValue("@report_path", report.ReportPath);
        command.Parameters.AddWithValue("@status_summary", report.StatusSummary);
        command.Parameters.AddWithValue("@what_jules_did", report.WhatJulesDid);
        command.Parameters.AddWithValue("@next_prompt", report.NextPrompt);
        command.Parameters.AddWithValue("@should_start_new_jules_session", report.ShouldStartNewJulesSession);
        command.Parameters.AddWithValue("@database_plan", report.DatabasePlan);
        command.Parameters.AddWithValue("@risk_notes_json", report.RiskNotesJson);
        command.Parameters.AddWithValue("@analysis_json", report.AnalysisJson);
        command.Parameters.AddWithValue("@error_text", report.ErrorText);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertEventAsync(
        MySqlConnection connection,
        long agentId,
        long runId,
        AgentEvent agentEvent,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_events (run_id, agent_id, event_type, severity, message, metadata_json, created_at)
            VALUES (@run_id, @agent_id, @event_type, @severity, @message, @metadata_json, @created_at);
            """;
        command.Parameters.AddWithValue("@run_id", runId);
        command.Parameters.AddWithValue("@agent_id", agentId);
        command.Parameters.AddWithValue("@event_type", agentEvent.EventType);
        command.Parameters.AddWithValue("@severity", agentEvent.Severity);
        command.Parameters.AddWithValue("@message", agentEvent.Message);
        command.Parameters.AddWithValue("@metadata_json", agentEvent.MetadataJson);
        command.Parameters.AddWithValue("@created_at", agentEvent.CreatedAt.LocalDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertJulesSessionsAsync(
        MySqlConnection connection,
        long agentId,
        string sessionsRaw,
        CancellationToken cancellationToken)
    {
        foreach (var session in ParseSessions(sessionsRaw))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO agent_jules_sessions (agent_id, session_id, repo, status, description, raw_line, last_seen_at)
                VALUES (@agent_id, @session_id, @repo, @status, @description, @raw_line, CURRENT_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    repo = VALUES(repo),
                    status = VALUES(status),
                    description = VALUES(description),
                    raw_line = VALUES(raw_line),
                    last_seen_at = CURRENT_TIMESTAMP(6);
                """;
            command.Parameters.AddWithValue("@agent_id", agentId);
            command.Parameters.AddWithValue("@session_id", session.SessionId);
            command.Parameters.AddWithValue("@repo", session.Repo);
            command.Parameters.AddWithValue("@status", session.Status);
            command.Parameters.AddWithValue("@description", session.Description);
            command.Parameters.AddWithValue("@raw_line", session.RawLine);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<ParsedJulesSession> ParseSessions(string sessionsRaw)
    {
        foreach (var rawLine in sessionsRaw.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var match = Regex.Match(line, @"^(?<id>\d{10,})\s+(?<description>.*?)\s+(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)\s+.*?\s+(?<status>Planning|In Progress|Completed|Needs review|Failed)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            yield return new ParsedJulesSession
            {
                SessionId = match.Groups["id"].Value,
                Description = match.Groups["description"].Value.Trim(),
                Repo = match.Groups["repo"].Value.Trim(),
                Status = match.Groups["status"].Value.Trim(),
                RawLine = line
            };
        }
    }

    private sealed class ParsedJulesSession
    {
        public string SessionId { get; init; } = "";
        public string Description { get; init; } = "";
        public string Repo { get; init; } = "";
        public string Status { get; init; } = "";
        public string RawLine { get; init; } = "";
    }
}
