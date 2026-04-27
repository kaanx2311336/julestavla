using System.Collections;
using System.Data;
using TavlaJules.Data.Repositories;
using Xunit;

namespace TavlaJules.Engine.Tests.Services;

public class OnlineMatchRepositoryTests
{
    [Fact]
    public async Task CreateMatchAsync_ExecutesParameterizedInsertAndReturnsMatchId()
    {
        var connection = new RecordingConnection();
        var repository = new OnlineMatchRepository(new RecordingConnectionFactory(connection));

        var matchId = await repository.CreateMatchAsync();

        var command = Assert.Single(connection.Commands);
        Assert.False(string.IsNullOrWhiteSpace(matchId));
        Assert.Equal(1, command.ExecuteNonQueryCalls);
        Assert.Contains("INSERT INTO online_matches", command.CommandText);
        Assert.Equal(matchId, command.GetParameterValue("@id"));
        Assert.Equal("WaitingForPlayers", command.GetParameterValue("@status"));
    }

    [Fact]
    public async Task UpdateMatchStatusAsync_ExecutesParameterizedUpdate()
    {
        var connection = new RecordingConnection();
        var repository = new OnlineMatchRepository(new RecordingConnectionFactory(connection));

        await repository.UpdateMatchStatusAsync("m-id", "InProgress", "snap-id");

        var command = Assert.Single(connection.Commands);
        Assert.Equal(1, command.ExecuteNonQueryCalls);
        Assert.Contains("UPDATE online_matches", command.CommandText);
        Assert.Equal("m-id", command.GetParameterValue("@id"));
        Assert.Equal("InProgress", command.GetParameterValue("@status"));
        Assert.Equal("snap-id", command.GetParameterValue("@snapshotId"));
    }

    private sealed class RecordingConnectionFactory(RecordingConnection connection) : IDbConnectionFactory
    {
        public Task<IDbConnection> CreateConnectionAsync()
        {
            return Task.FromResult<IDbConnection>(connection);
        }
    }

    private sealed class RecordingConnection : IDbConnection
    {
        public List<RecordingCommand> Commands { get; } = [];

        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "";
        public ConnectionState State { get; private set; } = ConnectionState.Closed;

        public IDbTransaction BeginTransaction()
        {
            throw new NotSupportedException();
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            throw new NotSupportedException();
        }

        public void ChangeDatabase(string databaseName)
        {
        }

        public void Close()
        {
            State = ConnectionState.Closed;
        }

        public IDbCommand CreateCommand()
        {
            var command = new RecordingCommand(this);
            Commands.Add(command);
            return command;
        }

        public void Open()
        {
            State = ConnectionState.Open;
        }

        public void Dispose()
        {
            Close();
        }
    }

    private sealed class RecordingCommand(IDbConnection connection) : IDbCommand
    {
        private readonly RecordingParameterCollection parameters = new();

        public int ExecuteNonQueryCalls { get; private set; }

        public string CommandText { get; set; } = "";
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; } = CommandType.Text;
        public IDbConnection? Connection { get; set; } = connection;
        public IDataParameterCollection Parameters => parameters;
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }

        public object? GetParameterValue(string name)
        {
            return parameters[name] is IDataParameter parameter ? parameter.Value : null;
        }

        public void Cancel()
        {
        }

        public IDbDataParameter CreateParameter()
        {
            return new RecordingParameter();
        }

        public int ExecuteNonQuery()
        {
            ExecuteNonQueryCalls++;
            return 1;
        }

        public IDataReader ExecuteReader()
        {
            throw new NotSupportedException();
        }

        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        public object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public void Prepare()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public bool IsNullable => true;
        public string ParameterName { get; set; } = "";
        public string SourceColumn { get; set; } = "";
        public DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;
        public object? Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }

    private sealed class RecordingParameterCollection : IDataParameterCollection
    {
        private readonly List<object> items = [];

        public object? this[string parameterName]
        {
            get
            {
                var index = IndexOf(parameterName);
                return index >= 0 ? items[index] : null;
            }
            set
            {
                var index = IndexOf(parameterName);
                if (index >= 0)
                {
                    items[index] = value!;
                }
                else
                {
                    items.Add(value!);
                }
            }
        }

        public object? this[int index]
        {
            get => items[index];
            set => items[index] = value!;
        }

        public bool IsFixedSize => false;
        public bool IsReadOnly => false;
        public int Count => items.Count;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public int Add(object? value)
        {
            items.Add(value!);
            return items.Count - 1;
        }

        public void Clear()
        {
            items.Clear();
        }

        public bool Contains(string parameterName)
        {
            return IndexOf(parameterName) >= 0;
        }

        public bool Contains(object? value)
        {
            return items.Contains(value!);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)items).CopyTo(array, index);
        }

        public IEnumerator GetEnumerator()
        {
            return items.GetEnumerator();
        }

        public int IndexOf(string parameterName)
        {
            return items.FindIndex(item =>
                item is IDataParameter parameter
                && parameter.ParameterName.Equals(parameterName, StringComparison.Ordinal));
        }

        public int IndexOf(object? value)
        {
            return items.IndexOf(value!);
        }

        public void Insert(int index, object? value)
        {
            items.Insert(index, value!);
        }

        public void Remove(object? value)
        {
            items.Remove(value!);
        }

        public void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                items.RemoveAt(index);
            }
        }

        public void RemoveAt(int index)
        {
            items.RemoveAt(index);
        }
    }
}
