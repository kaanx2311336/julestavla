using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using TavlaJules.Data.Repositories;
using TavlaJules.Engine.Models;
using Xunit;

namespace TavlaJules.Engine.Tests;

public class MySqlGameRepositoryTests
{
    [Fact]
    public async Task SaveSnapshotAsync_ShouldExecuteInsertQueryWithCorrectParameters()
    {
        // Arrange
        var mockConnectionFactory = new Mock<IDbConnectionFactory>();
        var mockConnection = new Mock<DbConnection>();
        var mockCommand = new Mock<DbCommand>();
        var mockParameters = new Mock<DbParameterCollection>();

        mockConnectionFactory
            .Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        mockConnection
            .Protected()
            .Setup<DbCommand>("CreateDbCommand")
            .Returns(mockCommand.Object);

        mockCommand
            .Protected()
            .Setup<DbParameter>("CreateDbParameter")
            .Returns(() => 
            {
                var p = new Mock<DbParameter>();
                p.SetupAllProperties();
                return p.Object;
            });

        mockCommand
            .Protected()
            .SetupGet<DbParameterCollection>("DbParameterCollection")
            .Returns(mockParameters.Object);

        mockCommand
            .Setup(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1); // Simulate 1 row inserted

        var repo = new MySqlGameRepository(mockConnectionFactory.Object);
        
        var points = new List<PointSnapshot> { new PointSnapshot(1, PlayerColor.White, 2) }.AsReadOnly();
        var snapshot = new GameStateSnapshot(
            points,
            WhiteCheckersOnBar: 1,
            BlackCheckersOnBar: 2,
            WhiteCheckersBorneOff: 3,
            BlackCheckersBorneOff: 4,
            CurrentTurn: PlayerColor.Black,
            RemainingDice: new List<int> { 5, 6 }.AsReadOnly(),
            TurnNumber: 10
        );

        // Act
        await repo.SaveSnapshotAsync(snapshot);

        // Assert
        // Verify connection creation
        mockConnectionFactory.Verify(f => f.CreateConnectionAsync(), Times.Once);
        
        // Verify command execution
        mockCommand.Verify(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()), Times.Once());
        
        // Verify parameters were added (10 parameters in total)
        mockParameters.Verify(p => p.Add(It.IsAny<object>()), Times.Exactly(10));
    }

    [Fact]
    public async Task LoadSnapshotAsync_ShouldReturnNullWhenNoRows()
    {
        // Arrange
        var mockConnectionFactory = new Mock<IDbConnectionFactory>();
        var mockConnection = new Mock<DbConnection>();
        var mockCommand = new Mock<DbCommand>();
        var mockParameters = new Mock<DbParameterCollection>();
        var mockReader = new Mock<DbDataReader>();

        mockConnectionFactory
            .Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        mockConnection
            .Protected()
            .Setup<DbCommand>("CreateDbCommand")
            .Returns(mockCommand.Object);

        mockCommand
            .Protected()
            .Setup<DbParameter>("CreateDbParameter")
            .Returns(() => 
            {
                var p = new Mock<DbParameter>();
                p.SetupAllProperties();
                return p.Object;
            });

        mockCommand
            .Protected()
            .SetupGet<DbParameterCollection>("DbParameterCollection")
            .Returns(mockParameters.Object);

        mockCommand
            .Protected()
            .Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync", ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockReader.Object);

        mockReader
            .Setup(r => r.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var repo = new MySqlGameRepository(mockConnectionFactory.Object);

        // Act
        var result = await repo.LoadSnapshotAsync("game_123");

        // Assert
        Assert.Null(result);
        mockParameters.Verify(p => p.Add(It.IsAny<object>()), Times.Once());
    }

    [Fact]
    public async Task LoadSnapshotAsync_ShouldReturnMappedSnapshot()
    {
        // Arrange
        var mockConnectionFactory = new Mock<IDbConnectionFactory>();
        var mockConnection = new Mock<DbConnection>();
        var mockCommand = new Mock<DbCommand>();
        var mockParameters = new Mock<DbParameterCollection>();
        var mockReader = new Mock<DbDataReader>();

        mockConnectionFactory
            .Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        mockConnection
            .Protected()
            .Setup<DbCommand>("CreateDbCommand")
            .Returns(mockCommand.Object);

        mockCommand
            .Protected()
            .Setup<DbParameter>("CreateDbParameter")
            .Returns(() => 
            {
                var p = new Mock<DbParameter>();
                p.SetupAllProperties();
                return p.Object;
            });

        mockCommand
            .Protected()
            .SetupGet<DbParameterCollection>("DbParameterCollection")
            .Returns(mockParameters.Object);

        mockCommand
            .Protected()
            .Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync", ItExpr.IsAny<CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockReader.Object);

        mockReader
            .SetupSequence(r => r.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var pointsJson = "[{\"Index\": 1, \"Color\": 1, \"CheckerCount\": 2}]";
        var diceJson = "[5, 6]";

        mockReader.Setup(r => r.GetOrdinal("board_layout")).Returns(0);
        mockReader.Setup(r => r.IsDBNull(0)).Returns(false);
        mockReader.Setup(r => r.GetString(0)).Returns(pointsJson);

        mockReader.Setup(r => r.GetOrdinal("white_checkers_on_bar")).Returns(1);
        mockReader.Setup(r => r.IsDBNull(1)).Returns(false);
        mockReader.Setup(r => r.GetInt32(1)).Returns(1);

        mockReader.Setup(r => r.GetOrdinal("black_checkers_on_bar")).Returns(2);
        mockReader.Setup(r => r.IsDBNull(2)).Returns(false);
        mockReader.Setup(r => r.GetInt32(2)).Returns(2);

        mockReader.Setup(r => r.GetOrdinal("white_checkers_borne_off")).Returns(3);
        mockReader.Setup(r => r.IsDBNull(3)).Returns(false);
        mockReader.Setup(r => r.GetInt32(3)).Returns(3);

        mockReader.Setup(r => r.GetOrdinal("black_checkers_borne_off")).Returns(4);
        mockReader.Setup(r => r.IsDBNull(4)).Returns(false);
        mockReader.Setup(r => r.GetInt32(4)).Returns(4);

        mockReader.Setup(r => r.GetOrdinal("current_turn")).Returns(5);
        mockReader.Setup(r => r.IsDBNull(5)).Returns(false);
        mockReader.Setup(r => r.GetString(5)).Returns("Black");

        mockReader.Setup(r => r.GetOrdinal("remaining_dice")).Returns(6);
        mockReader.Setup(r => r.IsDBNull(6)).Returns(false);
        mockReader.Setup(r => r.GetString(6)).Returns(diceJson);

        mockReader.Setup(r => r.GetOrdinal("turn_number")).Returns(7);
        mockReader.Setup(r => r.IsDBNull(7)).Returns(false);
        mockReader.Setup(r => r.GetInt32(7)).Returns(10);

        var repo = new MySqlGameRepository(mockConnectionFactory.Object);

        // Act
        var result = await repo.LoadSnapshotAsync("game_123");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Points);
        Assert.Equal(1, result.Points[0].Index);
        Assert.Equal(PlayerColor.White, result.Points[0].Color); // White is 1
        Assert.Equal(2, result.Points[0].CheckerCount);
        
        Assert.Equal(1, result.WhiteCheckersOnBar);
        Assert.Equal(2, result.BlackCheckersOnBar);
        Assert.Equal(3, result.WhiteCheckersBorneOff);
        Assert.Equal(4, result.BlackCheckersBorneOff);
        Assert.Equal(PlayerColor.Black, result.CurrentTurn);
        Assert.Equal(new[] { 5, 6 }, result.RemainingDice);
        Assert.Equal(10, result.TurnNumber);
        
        mockParameters.Verify(p => p.Add(It.IsAny<object>()), Times.Once());
    }

    [Fact]
    public async Task SaveMoveSequenceAsync_ShouldExecuteInsertQueryWithCorrectParameters()
    {
        // Arrange
        var mockConnectionFactory = new Mock<IDbConnectionFactory>();
        var mockConnection = new Mock<DbConnection>();
        var mockCommand = new Mock<DbCommand>();
        var mockParameters = new Mock<DbParameterCollection>();

        mockConnectionFactory
            .Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        mockConnection
            .Protected()
            .Setup<DbCommand>("CreateDbCommand")
            .Returns(mockCommand.Object);

        mockCommand
            .Protected()
            .Setup<DbParameter>("CreateDbParameter")
            .Returns(() => 
            {
                var p = new Mock<DbParameter>();
                p.SetupAllProperties();
                return p.Object;
            });

        mockCommand
            .Protected()
            .SetupGet<DbParameterCollection>("DbParameterCollection")
            .Returns(mockParameters.Object);

        mockCommand
            .Setup(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var repo = new MySqlGameRepository(mockConnectionFactory.Object);
        
        var moves = new List<Move> 
        { 
            new Move(24, 20, 1, false, 4),
            new Move(20, 15, 1, false, 5)
        };

        // Act
        await repo.SaveMoveSequenceAsync("test_game_id", moves);

        // Assert
        mockConnectionFactory.Verify(f => f.CreateConnectionAsync(), Times.Once);
        mockCommand.Verify(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()), Times.Once());
        mockParameters.Verify(p => p.Add(It.IsAny<object>()), Times.Exactly(4));
    }

    [Fact]
    public async Task SaveDiceRollAsync_ShouldExecuteInsertQueryWithCorrectParameters()
    {
        // Arrange
        var mockConnectionFactory = new Mock<IDbConnectionFactory>();
        var mockConnection = new Mock<DbConnection>();
        var mockCommand = new Mock<DbCommand>();
        var mockParameters = new Mock<DbParameterCollection>();

        mockConnectionFactory
            .Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        mockConnection
            .Protected()
            .Setup<DbCommand>("CreateDbCommand")
            .Returns(mockCommand.Object);

        mockCommand
            .Protected()
            .Setup<DbParameter>("CreateDbParameter")
            .Returns(() => 
            {
                var p = new Mock<DbParameter>();
                p.SetupAllProperties();
                return p.Object;
            });

        mockCommand
            .Protected()
            .SetupGet<DbParameterCollection>("DbParameterCollection")
            .Returns(mockParameters.Object);

        mockCommand
            .Setup(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var repo = new MySqlGameRepository(mockConnectionFactory.Object);

        // Act
        await repo.SaveDiceRollAsync("test_game_id", 3, 4, PlayerColor.White);

        // Assert
        mockConnectionFactory.Verify(f => f.CreateConnectionAsync(), Times.Once);
        mockCommand.Verify(c => c.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()), Times.Once());
        mockParameters.Verify(p => p.Add(It.IsAny<object>()), Times.Exactly(6));
    }
}
