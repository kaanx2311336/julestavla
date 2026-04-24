using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using TavlaJules.Data.Repositories;
using TavlaJules.Engine.Models;
using Xunit;

namespace TavlaJules.Engine.Tests;

public class GameStateRepositoryTests
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

        var repo = new GameStateRepository(mockConnectionFactory.Object);
        
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
        
        // Verify parameters were added (9 parameters in total)
        mockParameters.Verify(p => p.Add(It.IsAny<object>()), Times.Exactly(9));
    }
}
