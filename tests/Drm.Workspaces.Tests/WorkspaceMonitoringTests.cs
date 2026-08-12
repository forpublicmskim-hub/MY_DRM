using Drm.Application;
using Drm.Domain;
using Drm.Platform.Local;

namespace Drm.Workspaces.Tests;

public sealed class WorkspaceMonitoringTests
{
    [Fact]
    public async Task InitialScanReportsExistingEntry()
    {
        using TemporaryDirectory directory = new();
        string file = Path.Combine(directory.Path, nameof(InitialScanReportsExistingEntry));
        await File.WriteAllTextAsync(file, string.Empty);
        await using FileSystemWatcherWorkspaceMonitor monitor = CreateMonitor(directory.Path);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<WorkspaceMonitorEvent> observed = WaitForObservationAsync(
            monitor, WorkspaceObservationKind.Existing, timeout.Token);

        await monitor.StartAsync(timeout.Token);

        WorkspaceMonitorEvent result = await observed;
        Assert.Equal(nameof(InitialScanReportsExistingEntry), result.Observation!.RelativePath);
        Assert.Equal(WorkspaceMonitorState.Watching, monitor.State);
    }

    [Fact]
    public async Task ReportsFileCreatedAfterStart()
    {
        using TemporaryDirectory directory = new();
        await using FileSystemWatcherWorkspaceMonitor monitor = CreateMonitor(directory.Path);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await monitor.StartAsync(timeout.Token);
        Task<WorkspaceMonitorEvent> observed = WaitForObservationAsync(
            monitor, WorkspaceObservationKind.Created, timeout.Token);
        string file = Path.Combine(directory.Path, nameof(ReportsFileCreatedAfterStart));

        await File.WriteAllTextAsync(file, string.Empty, timeout.Token);

        WorkspaceMonitorEvent result = await observed;
        Assert.Equal(nameof(ReportsFileCreatedAfterStart), result.Observation!.RelativePath);
    }

    [Fact]
    public async Task ReportsRenameWithPreviousRelativePath()
    {
        using TemporaryDirectory directory = new();
        string originalName = nameof(InitialScanReportsExistingEntry);
        string renamedName = nameof(ReportsRenameWithPreviousRelativePath);
        string original = Path.Combine(directory.Path, originalName);
        await File.WriteAllTextAsync(original, string.Empty);
        await using FileSystemWatcherWorkspaceMonitor monitor = CreateMonitor(directory.Path);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await monitor.StartAsync(timeout.Token);
        Task<WorkspaceMonitorEvent> observed = WaitForObservationAsync(
            monitor, WorkspaceObservationKind.Renamed, timeout.Token);

        File.Move(original, Path.Combine(directory.Path, renamedName));

        WorkspaceObservation result = (await observed).Observation!;
        Assert.Equal(renamedName, result.RelativePath);
        Assert.Equal(originalName, result.PreviousRelativePath);
    }

    [Fact]
    public async Task StopPreventsFurtherObservations()
    {
        using TemporaryDirectory directory = new();
        await using FileSystemWatcherWorkspaceMonitor monitor = CreateMonitor(directory.Path);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await monitor.StartAsync(timeout.Token);
        await monitor.StopAsync(timeout.Token);

        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, nameof(StopPreventsFurtherObservations)), string.Empty, timeout.Token);

        Assert.Equal(WorkspaceMonitorState.Stopped, monitor.State);
    }

    private static FileSystemWatcherWorkspaceMonitor CreateMonitor(string path)
    {
        ProtectedWorkspace workspace = new(
            WorkspaceId.New(), nameof(WorkspaceMonitoringTests), new WorkspaceLocation(path, path),
            WorkspaceRegistrationState.Registered, WorkspaceProtectionState.NotActivated,
            DateTimeOffset.UtcNow);
        return new FileSystemWatcherWorkspaceMonitor(workspace, new LocalWorkspaceScanner(), TimeProvider.System);
    }

    private static async Task<WorkspaceMonitorEvent> WaitForObservationAsync(
        FileSystemWatcherWorkspaceMonitor monitor,
        WorkspaceObservationKind kind,
        CancellationToken cancellationToken)
    {
        await foreach (WorkspaceMonitorEvent item in monitor.ObserveAsync(cancellationToken))
            if (item.Observation?.Kind == kind) return item;
        throw new InvalidOperationException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
