using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RiuTek.Infrastructure.Caching;
using StackExchange.Redis;

namespace RiuTek.Application.Test.Caching;

public class RedisCacheServiceTests
{
    private readonly RedisSettings _settings = new()
    {
        Enabled = true,
        ConnectionString = "localhost:6379",
        InstanceName = "test:",
        DefaultExpirationMinutes = 10
    };

    [Fact]
    public async Task GetAsync_WhenMultiplexerNotConnected_ReturnsDefaultWithoutThrowing()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(false);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        var result = await service.GetAsync<string>("any_key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenRedisExceptionOccurs_ReturnsDefaultWithoutThrowing()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("Redis timed out", CommandStatus.WaitingToBeSent));

        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        var result = await service.GetAsync<string>("timed_out_key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenRedisExceptionOccurs_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Cannot connect"));

        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act & Assert
        var act = () => service.SetAsync("key", "val", TimeSpan.FromMinutes(1));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AllMethods_WhenCancelled_ShouldRethrowOperationCanceledException()
    {
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var getAct = () => service.GetAsync<string>("key", cts.Token);
        await getAct.Should().ThrowAsync<OperationCanceledException>();

        var setAct = () => service.SetAsync("key", "val", TimeSpan.FromMinutes(1), cts.Token);
        await setAct.Should().ThrowAsync<OperationCanceledException>();

        var removeAct = () => service.RemoveAsync("key", cts.Token);
        await removeAct.Should().ThrowAsync<OperationCanceledException>();

        var removePrefixAct = () => service.RemoveByPrefixAsync("prefix_", cts.Token);
        await removePrefixAct.Should().ThrowAsync<OperationCanceledException>();
    }
}
