using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TavlaJules.Engine.Models;

namespace TavlaJules.Data.Repositories;

public class GameStateRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GameStateRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task SaveSnapshotAsync(GameStateSnapshot snapshot, string? gameId = null)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        
        var query = @"
            INSERT INTO tavla_game_snapshots (
                id,
                board_layout,
                white_checkers_on_bar,
                black_checkers_on_bar,
                white_checkers_borne_off,
                black_checkers_borne_off,
                current_turn,
                remaining_dice,
                turn_number,
                created_at
            ) VALUES (
                @id,
                @board_layout,
                @white_checkers_on_bar,
                @black_checkers_on_bar,
                @white_checkers_borne_off,
                @black_checkers_borne_off,
                @current_turn,
                @remaining_dice,
                @turn_number,
                @created_at
            );";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        string boardLayoutJson = JsonSerializer.Serialize(snapshot.Points);
        string remainingDiceJson = JsonSerializer.Serialize(snapshot.RemainingDice);

        AddParameter(command, "@id", string.IsNullOrEmpty(gameId) ? DBNull.Value : gameId);
        AddParameter(command, "@board_layout", boardLayoutJson);
        AddParameter(command, "@white_checkers_on_bar", snapshot.WhiteCheckersOnBar);
        AddParameter(command, "@black_checkers_on_bar", snapshot.BlackCheckersOnBar);
        AddParameter(command, "@white_checkers_borne_off", snapshot.WhiteCheckersBorneOff);
        AddParameter(command, "@black_checkers_borne_off", snapshot.BlackCheckersBorneOff);
        AddParameter(command, "@current_turn", snapshot.CurrentTurn.ToString());
        AddParameter(command, "@remaining_dice", remainingDiceJson);
        AddParameter(command, "@turn_number", snapshot.TurnNumber);
        AddParameter(command, "@created_at", DateTime.UtcNow);

        if (command is DbCommand dbCommand)
        {
            await dbCommand.ExecuteNonQueryAsync();
        }
        else
        {
            command.ExecuteNonQuery();
        }
    }

    public async Task<GameStateSnapshot?> LoadSnapshotAsync(string gameId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        var query = @"
            SELECT 
                board_layout,
                white_checkers_on_bar,
                black_checkers_on_bar,
                white_checkers_borne_off,
                black_checkers_borne_off,
                current_turn,
                remaining_dice,
                turn_number
            FROM tavla_game_snapshots
            WHERE id = @id
            ORDER BY created_at DESC
            LIMIT 1;";

        using var command = connection.CreateCommand();
        command.CommandText = query;
        AddParameter(command, "@id", gameId);

        using var reader = command is DbCommand dbCmd
            ? await dbCmd.ExecuteReaderAsync(cancellationToken)
            : command.ExecuteReader();

        if (reader is DbDataReader dbReader)
        {
            if (!await dbReader.ReadAsync(cancellationToken))
            {
                return null;
            }
        }
        else
        {
            if (!reader.Read())
            {
                return null;
            }
        }

        var boardLayoutJson = GetString(reader, "board_layout");
        var whiteCheckersOnBar = GetInt32(reader, "white_checkers_on_bar");
        var blackCheckersOnBar = GetInt32(reader, "black_checkers_on_bar");
        var whiteCheckersBorneOff = GetInt32(reader, "white_checkers_borne_off");
        var blackCheckersBorneOff = GetInt32(reader, "black_checkers_borne_off");
        var currentTurnStr = GetString(reader, "current_turn");
        var remainingDiceJson = GetString(reader, "remaining_dice");
        var turnNumber = GetInt32(reader, "turn_number");

        var points = JsonSerializer.Deserialize<List<PointSnapshot>>(boardLayoutJson) ?? new List<PointSnapshot>();
        var remainingDice = JsonSerializer.Deserialize<List<int>>(remainingDiceJson) ?? new List<int>();
        var currentTurn = Enum.Parse<PlayerColor>(currentTurnStr);

        return new GameStateSnapshot(
            points.AsReadOnly(),
            whiteCheckersOnBar,
            blackCheckersOnBar,
            whiteCheckersBorneOff,
            blackCheckersBorneOff,
            currentTurn,
            remainingDice.AsReadOnly(),
            turnNumber
        );
    }

    private void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string GetString(IDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int GetInt32(IDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }
}
