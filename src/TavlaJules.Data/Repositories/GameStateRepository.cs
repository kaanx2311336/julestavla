using System;
using System.Data;
using System.Text.Json;
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

    public async Task SaveSnapshotAsync(GameStateSnapshot snapshot)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        
        var query = @"
            INSERT INTO tavla_game_snapshots (
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
                @board_layout::jsonb,
                @white_checkers_on_bar,
                @black_checkers_on_bar,
                @white_checkers_borne_off,
                @black_checkers_borne_off,
                @current_turn,
                @remaining_dice::jsonb,
                @turn_number,
                @created_at
            );";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        string boardLayoutJson = JsonSerializer.Serialize(snapshot.Points);
        string remainingDiceJson = JsonSerializer.Serialize(snapshot.RemainingDice);

        AddParameter(command, "@board_layout", boardLayoutJson);
        AddParameter(command, "@white_checkers_on_bar", snapshot.WhiteCheckersOnBar);
        AddParameter(command, "@black_checkers_on_bar", snapshot.BlackCheckersOnBar);
        AddParameter(command, "@white_checkers_borne_off", snapshot.WhiteCheckersBorneOff);
        AddParameter(command, "@black_checkers_borne_off", snapshot.BlackCheckersBorneOff);
        AddParameter(command, "@current_turn", snapshot.CurrentTurn.ToString());
        AddParameter(command, "@remaining_dice", remainingDiceJson);
        AddParameter(command, "@turn_number", snapshot.TurnNumber);
        AddParameter(command, "@created_at", DateTime.UtcNow);

        if (command is System.Data.Common.DbCommand dbCommand)
        {
            await dbCommand.ExecuteNonQueryAsync();
        }
        else
        {
            command.ExecuteNonQuery();
        }
    }

    private void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
