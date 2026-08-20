using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Drm.Domain;

namespace Drm.Application;

public enum WorkspaceMonitorState
{
    Stopped,
    Starting,
    Watching,
    Rescanning,
    Degraded,
    Stopping,
    Faulted
}

public enum WorkspaceObservationKind
{
    Existing,
    Created,
    Modified,
    Deleted,
    Renamed
}

public enum WorkspaceMonitorEventKind
{
    Observation,
    StateChanged,
    ReconciliationRequired
}

public sealed record WorkspaceObservation(
    WorkspaceId WorkspaceId,
    WorkspaceObservationKind Kind,
    string RelativePath,
    string? PreviousRelativePath,
    DateTimeOffset ObservedAt);

public sealed record WorkspaceMonitorEvent(
    WorkspaceId WorkspaceId,
    WorkspaceMonitorEventKind Kind,
    WorkspaceMonitorState State,
    WorkspaceObservation? Observation = null);

public interface IWorkspaceScanner
{
    IAsyncEnumerable<string> ScanAsync(ProtectedWorkspace workspace, CancellationToken cancellationToken);
}

public interface IWorkspaceMonitor : IAsyncDisposable
{
    WorkspaceId WorkspaceId { get; }
    WorkspaceMonitorState State { get; }
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<WorkspaceMonitorEvent> ObserveAsync(CancellationToken cancellationToken);
}

public interface IWorkspaceMonitorFactory
{
    IWorkspaceMonitor Create(ProtectedWorkspace workspace);
}

public interface IWorkspaceMonitorCoordinator : IAsyncDisposable
{
    ValueTask ReconcileAsync(
        IReadOnlyCollection<ProtectedWorkspace> workspaces,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkspaceMonitorEvent> ObserveAsync(
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceMonitorManager(IWorkspaceMonitorFactory factory)
    : IWorkspaceMonitorCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<WorkspaceId, MonitorRegistration> _registrations = [];
    private readonly Channel<WorkspaceMonitorEvent> _events = Channel.CreateBounded<WorkspaceMonitorEvent>(
        new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private bool _disposed;

    public async ValueTask ReconcileAsync(
        IReadOnlyCollection<ProtectedWorkspace> workspaces,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Dictionary<WorkspaceId, ProtectedWorkspace> desired = workspaces
            .Where(workspace => workspace.RegistrationState == WorkspaceRegistrationState.Registered)
            .ToDictionary(workspace => workspace.Id);
        List<MonitorRegistration> stop = [];
        List<MonitorRegistration> start = [];

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach ((WorkspaceId id, MonitorRegistration registration) in _registrations.ToArray())
            {
                if (desired.ContainsKey(id)) continue;
                _registrations.Remove(id);
                stop.Add(registration);
            }

            foreach (ProtectedWorkspace workspace in desired.Values)
            {
                if (_registrations.ContainsKey(workspace.Id)) continue;
                IWorkspaceMonitor monitor = factory.Create(workspace);
                CancellationTokenSource lifetime = new();
                MonitorRegistration registration = new(monitor, lifetime);
                _registrations.Add(workspace.Id, registration);
                start.Add(registration);
            }
        }
        finally { _gate.Release(); }

        foreach (MonitorRegistration registration in stop)
            await StopRegistrationAsync(registration, cancellationToken).ConfigureAwait(false);

        foreach (MonitorRegistration registration in start)
        {
            registration.PumpTask = PumpAsync(registration.Monitor, registration.Lifetime.Token);
            try { await registration.Monitor.StartAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await RemoveFailedRegistrationAsync(registration).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await RemoveFailedRegistrationAsync(registration).ConfigureAwait(false);
            }
        }
    }

    public async IAsyncEnumerable<WorkspaceMonitorEvent> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (WorkspaceMonitorEvent item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    private async Task PumpAsync(IWorkspaceMonitor monitor, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (WorkspaceMonitorEvent item in monitor.ObserveAsync(cancellationToken).ConfigureAwait(false))
                await _events.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async ValueTask RemoveFailedRegistrationAsync(MonitorRegistration failed)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try { _registrations.Remove(failed.Monitor.WorkspaceId); }
        finally { _gate.Release(); }
        await StopRegistrationAsync(failed, CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask StopRegistrationAsync(
        MonitorRegistration registration,
        CancellationToken cancellationToken)
    {
        try { await registration.Monitor.StopAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            registration.Lifetime.Cancel();
            if (registration.PumpTask is not null)
            {
                try { await registration.PumpTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            await registration.Monitor.DisposeAsync().ConfigureAwait(false);
            registration.Lifetime.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        MonitorRegistration[] registrations;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            registrations = _registrations.Values.ToArray();
            _registrations.Clear();
        }
        finally { _gate.Release(); }

        foreach (MonitorRegistration registration in registrations)
            await StopRegistrationAsync(registration, CancellationToken.None).ConfigureAwait(false);
        _events.Writer.TryComplete();
        _gate.Dispose();
    }

    private sealed class MonitorRegistration(IWorkspaceMonitor monitor, CancellationTokenSource lifetime)
    {
        public IWorkspaceMonitor Monitor { get; } = monitor;
        public CancellationTokenSource Lifetime { get; } = lifetime;
        public Task? PumpTask { get; set; }
    }
}
