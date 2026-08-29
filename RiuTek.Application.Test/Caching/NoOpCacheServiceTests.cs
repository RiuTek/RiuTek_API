using FluentAssertions;
using RiuTek.Infrastructure.Caching;

namespace RiuTek.Application.Test.Caching;

public class NoOpCacheServiceTests
{
    private readonly NoOpCacheService _service = new();

    [Fact]
    public async Task GetAsync_ShouldReturnDefault()
    {
        var result = await _service.GetAsync<string>("any_key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ShouldCompleteSuccessfully()
    {
        var act = () => _service.SetAsync("any_key", "value", TimeSpan.FromMinutes(5));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_ShouldCompleteSuccessfully()
    {
        var act = () => _service.RemoveAsync("any_key");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldCompleteSuccessfully()
    {
        var act = () => _service.RemoveByPrefixAsync("prefix_");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AllMethods_WhenCancellationTokenCancelled_ShouldThrowOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var getAct = () => _service.GetAsync<string>("key", cts.Token);
        await getAct.Should().ThrowAsync<OperationCanceledException>();

        var setAct = () => _service.SetAsync("key", "val", TimeSpan.FromMinutes(1), cts.Token);
        await setAct.Should().ThrowAsync<OperationCanceledException>();

        var removeAct = () => _service.RemoveAsync("key", cts.Token);
        await removeAct.Should().ThrowAsync<OperationCanceledException>();

        var removePrefixAct = () => _service.RemoveByPrefixAsync("prefix_", cts.Token);
        await removePrefixAct.Should().ThrowAsync<OperationCanceledException>();
    }
}
