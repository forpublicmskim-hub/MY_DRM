using Drm.Desktop.Views;

namespace Drm.Workspaces.Tests;

public sealed class AsyncShutdownCoordinatorTests
{
    [Fact]
    public async Task ConcurrentShutdownRequestsShareOneAsynchronousCleanup()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        AsyncShutdownCoordinator coordinator = new(async () =>
        {
            Interlocked.Increment(ref calls);
            await completion.Task;
        });

        Task first = coordinator.BeginAsync();
        Task second = coordinator.BeginAsync();

        Assert.Same(first, second);
        Assert.False(coordinator.IsComplete);
        Assert.Equal(1, calls);

        completion.SetResult();
        await first;

        Assert.True(coordinator.IsComplete);
    }

    [Fact]
    public async Task CleanupFailureStillAllowsWindowToFinishClosing()
    {
        AsyncShutdownCoordinator coordinator = new(
            () => ValueTask.FromException(new IOException("cleanup failed")));

        await Assert.ThrowsAsync<IOException>(coordinator.BeginAsync);

        Assert.True(coordinator.IsComplete);
        Assert.Same(coordinator.BeginAsync(), coordinator.BeginAsync());
    }
}
