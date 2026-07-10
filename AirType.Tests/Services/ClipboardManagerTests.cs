using AirType.Services;
using Xunit;

namespace AirType.Tests.Services;

public sealed class ClipboardManagerTests
{
    [Fact]
    public async Task SetTextWithRetryAsync_WhenTransientFailuresOccur_RetriesWithBackoff()
    {
        var outcomes = new Queue<bool>(new[] { false, false, true });
        var delays = new List<TimeSpan>();
        int attempts = 0;

        bool result = await ClipboardManager.SetTextWithRetryAsync(
            "hello",
            _ =>
            {
                attempts++;
                return Task.FromResult(outcomes.Dequeue());
            },
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.True(result);
        Assert.Equal(3, attempts);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(75) },
            delays);
    }

    [Fact]
    public async Task SetTextWithRetryAsync_WhenAllAttemptsFail_StopsAfterBoundedRetryWindow()
    {
        var delays = new List<TimeSpan>();
        int attempts = 0;

        bool result = await ClipboardManager.SetTextWithRetryAsync(
            "hello",
            _ =>
            {
                attempts++;
                return Task.FromResult(false);
            },
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.False(result);
        Assert.Equal(7, attempts);
        Assert.Equal(6, delays.Count);
        Assert.True(delays.Sum(delay => delay.TotalMilliseconds) <= 1000);
    }
}
