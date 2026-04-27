using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using TavlaJules.Data.Models;

namespace TavlaJules.Data.Repositories;

public class OnlineMatchRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OnlineMatchRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<string> CreateMatchAsync(CancellationToken cancellationToken = default)
    {
        var matchId = Guid.NewGuid().ToString();

        using var connection = await _connectionFactory.CreateConnectionAsync();

        if (connection.State != ConnectionState.Open)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        var query = @"
            INSERT INTO online_matches (id, status)
            VALUES (@id, @status)";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        var idParam = command.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = matchId;
        command.Parameters.Add(idParam);

        var statusParam = command.CreateParameter();
        statusParam.ParameterName = "@status";
        statusParam.Value = "WaitingForPlayers";
        command.Parameters.Add(statusParam);

        if (command is DbCommand dbCommand)
        {
            await dbCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            command.ExecuteNonQuery();
        }

        return matchId;
    }

    public async Task<IReadOnlyList<OnlineMatch>> ListOpenMatchesAsync(CancellationToken cancellationToken = default)
    {
        var matches = new List<OnlineMatch>();

        using var connection = await _connectionFactory.CreateConnectionAsync();

        if (connection.State != ConnectionState.Open)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        var query = @"
            SELECT id, status, current_snapshot_id, created_at, updated_at
            FROM online_matches
            WHERE status = 'WaitingForPlayers'
            ORDER BY created_at DESC
            LIMIT 50";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        using var reader = command is DbCommand dbCmd
            ? await dbCmd.ExecuteReaderAsync(cancellationToken)
            : command.ExecuteReader();

        if (reader is DbDataReader dbReader)
        {
            while (await dbReader.ReadAsync(cancellationToken))
            {
                matches.Add(new OnlineMatch
                {
                    Id = reader.GetString(reader.GetOrdinal("id")),
                    Status = reader.GetString(reader.GetOrdinal("status")),
                    CurrentSnapshotId = reader.IsDBNull(reader.GetOrdinal("current_snapshot_id")) ? null : reader.GetString(reader.GetOrdinal("current_snapshot_id")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                });
            }
        }
        else
        {
            while (reader.Read())
            {
                matches.Add(new OnlineMatch
                {
                    Id = reader.GetString(reader.GetOrdinal("id")),
                    Status = reader.GetString(reader.GetOrdinal("status")),
                    CurrentSnapshotId = reader.IsDBNull(reader.GetOrdinal("current_snapshot_id")) ? null : reader.GetString(reader.GetOrdinal("current_snapshot_id")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                });
            }
        }

        return matches;
    }

    public async Task<bool> JoinMatchAsync(string matchId, string playerId, string playerName, string colorAssignment, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        if (connection.State != ConnectionState.Open)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        using var transaction = connection is DbConnection dbConnTx 
            ? await dbConnTx.BeginTransactionAsync(cancellationToken) 
            : connection.BeginTransaction();

        try
        {
            // Check if match exists and is not full/completed
            var checkMatchQuery = "SELECT status FROM online_matches WHERE id = @id";
            using var checkCommand = connection.CreateCommand();
            checkCommand.Transaction = transaction;
            checkCommand.CommandText = checkMatchQuery;
            
            var idParam = checkCommand.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = matchId;
            checkCommand.Parameters.Add(idParam);

            var statusObj = checkCommand is DbCommand dbCheckCommand
                ? await dbCheckCommand.ExecuteScalarAsync(cancellationToken)
                : checkCommand.ExecuteScalar();

            var status = (string?)statusObj;

            if (status != "WaitingForPlayers")
            {
                // Match not found or already full/started
                if (transaction is DbTransaction dbTxRollback)
                    await dbTxRollback.RollbackAsync(cancellationToken);
                else
                    transaction.Rollback();
                return false;
            }

            // Check how many players are currently in the match
            var countQuery = "SELECT COUNT(*) FROM online_match_players WHERE match_id = @matchId";
            using var countCommand = connection.CreateCommand();
            countCommand.Transaction = transaction;
            countCommand.CommandText = countQuery;

            var matchIdParam = countCommand.CreateParameter();
            matchIdParam.ParameterName = "@matchId";
            matchIdParam.Value = matchId;
            countCommand.Parameters.Add(matchIdParam);

            var countObj = countCommand is DbCommand dbCountCommand
                ? await dbCountCommand.ExecuteScalarAsync(cancellationToken)
                : countCommand.ExecuteScalar();

            var playerCount = Convert.ToInt32(countObj);

            if (playerCount >= 2)
            {
                // Full
                if (transaction is DbTransaction dbTxRollback)
                    await dbTxRollback.RollbackAsync(cancellationToken);
                else
                    transaction.Rollback();
                return false;
            }

            // Insert player
            var joinQuery = @"
                INSERT INTO online_match_players (id, match_id, player_id, player_name, color_assignment)
                VALUES (@playerIdRef, @matchId, @playerId, @playerName, @colorAssignment)";

            using var joinCommand = connection.CreateCommand();
            joinCommand.Transaction = transaction;
            joinCommand.CommandText = joinQuery;

            var playerIdRefParam = joinCommand.CreateParameter();
            playerIdRefParam.ParameterName = "@playerIdRef";
            playerIdRefParam.Value = Guid.NewGuid().ToString();
            joinCommand.Parameters.Add(playerIdRefParam);

            var mIdParam = joinCommand.CreateParameter();
            mIdParam.ParameterName = "@matchId";
            mIdParam.Value = matchId;
            joinCommand.Parameters.Add(mIdParam);

            var pIdParam = joinCommand.CreateParameter();
            pIdParam.ParameterName = "@playerId";
            pIdParam.Value = playerId;
            joinCommand.Parameters.Add(pIdParam);

            var pNameParam = joinCommand.CreateParameter();
            pNameParam.ParameterName = "@playerName";
            pNameParam.Value = playerName;
            joinCommand.Parameters.Add(pNameParam);

            var colorParam = joinCommand.CreateParameter();
            colorParam.ParameterName = "@colorAssignment";
            colorParam.Value = colorAssignment;
            joinCommand.Parameters.Add(colorParam);

            if (joinCommand is DbCommand dbJoinCommand)
                await dbJoinCommand.ExecuteNonQueryAsync(cancellationToken);
            else
                joinCommand.ExecuteNonQuery();

            // Update status if full
            if (playerCount == 1)
            {
                var updateMatchQuery = "UPDATE online_matches SET status = 'InProgress' WHERE id = @id";
                using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = updateMatchQuery;

                var uIdParam = updateCommand.CreateParameter();
                uIdParam.ParameterName = "@id";
                uIdParam.Value = matchId;
                updateCommand.Parameters.Add(uIdParam);

                if (updateCommand is DbCommand dbUpdateCommand)
                    await dbUpdateCommand.ExecuteNonQueryAsync(cancellationToken);
                else
                    updateCommand.ExecuteNonQuery();
            }

            if (transaction is DbTransaction dbTxCommit)
                await dbTxCommit.CommitAsync(cancellationToken);
            else
                transaction.Commit();
                
            return true;
        }
        catch
        {
            if (transaction is DbTransaction dbTxRollback)
                await dbTxRollback.RollbackAsync(cancellationToken);
            else
                transaction.Rollback();
            throw;
        }
    }

    public async Task UpdateMatchStatusAsync(string matchId, string status, string? currentSnapshotId = null, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        if (connection.State != ConnectionState.Open)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        var query = @"
            UPDATE online_matches
            SET status = @status, current_snapshot_id = COALESCE(@snapshotId, current_snapshot_id)
            WHERE id = @id";

        using var command = connection.CreateCommand();
        command.CommandText = query;

        var statusParam = command.CreateParameter();
        statusParam.ParameterName = "@status";
        statusParam.Value = status;
        command.Parameters.Add(statusParam);

        var snapshotIdParam = command.CreateParameter();
        snapshotIdParam.ParameterName = "@snapshotId";
        snapshotIdParam.Value = string.IsNullOrWhiteSpace(currentSnapshotId) ? DBNull.Value : (object)currentSnapshotId;
        command.Parameters.Add(snapshotIdParam);

        var idParam = command.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = matchId;
        command.Parameters.Add(idParam);

        if (command is DbCommand dbCommand)
        {
            await dbCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            command.ExecuteNonQuery();
        }
    }
}
