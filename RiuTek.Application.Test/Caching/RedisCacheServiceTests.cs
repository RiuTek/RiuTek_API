using System.Net;
using System.Text.Json;
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

    public record TestModel(int Id, string Name);

    #region Success Paths, Namespace & TTL

    [Fact]
    public async Task SetAsync_WithCustomExpiration_SerializesAndCallsStringSetWithFullKeyAndTtl()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);
        var model = new TestModel(42, "Custom Item");
        var customTtl = TimeSpan.FromMinutes(25);

        // Act
        await service.SetAsync("item_key", model, customTtl);

        // Assert
        dbMock.Verify(d => d.StringSetAsync(
            "test:item_key",
            It.Is<RedisValue>(v => ((string)v!).Contains("\"Id\":42") && ((string)v!).Contains("\"Name\":\"Custom Item\"")),
            customTtl,
            false,
            When.Always,
            CommandFlags.None
        ), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WithNullExpiration_UsesDefaultExpirationMinutesFromSettings()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);
        var model = new TestModel(100, "Default TTL Item");

        // Act
        await service.SetAsync("default_item", model, null);

        // Assert
        dbMock.Verify(d => d.StringSetAsync(
            "test:default_item",
            It.IsAny<RedisValue>(),
            TimeSpan.FromMinutes(_settings.DefaultExpirationMinutes),
            false,
            When.Always,
            CommandFlags.None
        ), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WithValidJson_ReadsFullKeyAndDeserializesSuccessfully()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        var json = JsonSerializer.Serialize(new TestModel(7, "Retrieved Model"));
        dbMock.Setup(d => d.StringGetAsync("test:valid_key", CommandFlags.None))
            .ReturnsAsync(json);

        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        var result = await service.GetAsync<TestModel>("valid_key");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(7);
        result.Name.Should().Be("Retrieved Model");
    }

    [Fact]
    public async Task RemoveAsync_CallsKeyDeleteWithFullKey()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        await service.RemoveAsync("to_delete");

        // Assert
        dbMock.Verify(d => d.KeyDeleteAsync("test:to_delete", CommandFlags.None), Times.Once);
    }

    #endregion

    #region Serialization & Corrupted Data Resilience

    [Fact]
    public async Task GetAsync_WhenCorruptedJson_LogsWarningAttemptsKeyDeleteAndReturnsDefault()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(d => d.StringGetAsync("test:corrupt_key", CommandFlags.None))
            .ReturnsAsync("{ corrupt json content");

        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        var result = await service.GetAsync<TestModel>("corrupt_key");

        // Assert
        result.Should().BeNull();
        dbMock.Verify(d => d.KeyDeleteAsync("test:corrupt_key", CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetAsync_WhenUnsupportedTypeSerialization_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act & Assert - Action (Delegate) cannot be serialized by System.Text.Json
        var act = () => service.SetAsync<Action>("unsupported_key", () => { });
        await act.Should().NotThrowAsync();

        // Verify StringSet was never called due to serialization error
        dbMock.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    #endregion

    #region Prefix Scan & Batch Deletion

    [Fact]
    public async Task RemoveByPrefixAsync_WithOver250Keys_DeletesInMultipleBatches()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var endPoint = new DnsEndPoint("localhost", 6379);
        multiplexerMock.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
            .Returns([endPoint]);

        var serverMock = new Mock<IServer>();
        serverMock.Setup(s => s.IsConnected).Returns(true);

        // Generate 300 keys
        var generatedKeys = Enumerable.Range(1, 300)
            .Select(i => (RedisKey)$"test:posts:list:key_{i}")
            .ToList();

        serverMock.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.Is<RedisValue>(v => (string)v! == "test:posts:list:*"),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(CreateAsyncEnumerable(generatedKeys));

        multiplexerMock.Setup(m => m.GetServer(endPoint, It.IsAny<object>()))
            .Returns(serverMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        await service.RemoveByPrefixAsync("posts:list:");

        // Assert
        // First batch: 250 keys, Second batch: 50 keys
        dbMock.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(batch => batch.Length == 250),
            CommandFlags.None
        ), Times.Once);

        dbMock.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(batch => batch.Length == 50),
            CommandFlags.None
        ), Times.Once);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WhenServerNotConnected_SkipsServer()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var endPoint = new DnsEndPoint("localhost", 6379);
        multiplexerMock.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
            .Returns([endPoint]);

        var serverMock = new Mock<IServer>();
        serverMock.Setup(s => s.IsConnected).Returns(false);

        multiplexerMock.Setup(m => m.GetServer(endPoint, It.IsAny<object>()))
            .Returns(serverMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act
        await service.RemoveByPrefixAsync("posts:list:");

        // Assert
        dbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    private static async IAsyncEnumerable<RedisKey> CreateAsyncEnumerable(IEnumerable<RedisKey> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    #endregion

    #region Redis Error & Resilience

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
    public async Task RemoveAsync_WhenRedisExceptionOccurs_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Redis delete error"));

        multiplexerMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act & Assert
        var act = () => service.RemoveAsync("key");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WhenRedisExceptionOccurs_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(m => m.IsConnected).Returns(true);

        multiplexerMock.Setup(m => m.GetEndPoints(It.IsAny<bool>()))
            .Throws(new RedisException("Cannot get endpoints"));

        var service = new RedisCacheService(multiplexerMock.Object, _settings, NullLogger<RedisCacheService>.Instance);

        // Act & Assert
        var act = () => service.RemoveByPrefixAsync("prefix_");
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Cancellation

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

    #endregion
}
