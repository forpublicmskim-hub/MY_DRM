using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Drm.Domain;

namespace Drm.Application;

public enum ProtectionInspectionEventStatus
{
    MonitorOnly,
    Inspected,
    WorkspaceUnavailable,
    ProcessingFailed
}

public static class ProtectionInspectionPipelineReasonCodes
{
    public const string MonitorOnly = "inspection.pipeline.monitor-only";
    public const string WorkspaceUnavailable = "inspection.pipeline.workspace-unavailable";
    public const string ProcessingFailed = "inspection.pipeline.processing-failed";
}

public sealed record ProtectionInspectionEvent(
    WorkspaceMonitorEvent MonitorEvent,
    ProtectionInspectionEventStatus Status,
    ProtectionCandidateInspectionResult? Inspection,
    string? ReasonCode)
{
    public static ProtectionInspectionEvent MonitorOnly(WorkspaceMonitorEvent monitorEvent) =>
        new(monitorEvent, ProtectionInspectionEventStatus.MonitorOnly, null,
            ProtectionInspectionPipelineReasonCodes.MonitorOnly);

    public static ProtectionInspectionEvent Inspected(
        WorkspaceMonitorEvent monitorEvent,
        ProtectionCandidateInspectionResult inspection) =>
        new(monitorEvent, ProtectionInspectionEventStatus.Inspected,
            inspection ?? throw new ArgumentNullException(nameof(inspection)), null);

    public static ProtectionInspectionEvent WorkspaceUnavailable(WorkspaceMonitorEvent monitorEvent) =>
        new(monitorEvent, ProtectionInspectionEventStatus.WorkspaceUnavailable, null,
            ProtectionInspectionPipelineReasonCodes.WorkspaceUnavailable);

    public static ProtectionInspectionEvent ProcessingFailed(WorkspaceMonitorEvent monitorEvent) =>
        new(monitorEvent, ProtectionInspectionEventStatus.ProcessingFailed, null,
            ProtectionInspectionPipelineReasonCodes.ProcessingFailed);
}

public sealed class ProtectionInspectionPipeline : IAsyncDisposable
{
    private const int ResultCapacity = 1024;
    private readonly IWorkspaceMonitorCoordinator _monitors;
    private readonly ProtectionCandidateInspectionProcessor _processor;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<ProtectionInspectionEvent> _results =
        Channel.CreateBounded<ProtectionInspectionEvent>(new BoundedChannelOptions(ResultCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly Task _pump;
    private ImmutableDictionary<WorkspaceId, ProtectedWorkspace> _workspaces =
        ImmutableDictionary<WorkspaceId, ProtectedWorkspace>.Empty;
    private int _disposed;

    public ProtectionInspectionPipeline(
        IWorkspaceMonitorCoordinator monitors,
        ProtectionCandidateInspectionProcessor processor)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _pump = PumpAsync(_lifetime.Token);
    }

    public async ValueTask ReconcileAsync(
        IReadOnlyCollection<ProtectedWorkspace> workspaces,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(workspaces);

        ImmutableDictionary<WorkspaceId, ProtectedWorkspace> desired = workspaces
            .Where(workspace => workspace.RegistrationState == WorkspaceRegistrationState.Registered)
            .ToImmutableDictionary(workspace => workspace.Id);

        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ImmutableDictionary<WorkspaceId, ProtectedWorkspace> previous = Volatile.Read(ref _workspaces);
        try
        {
            ImmutableDictionary<WorkspaceId, ProtectedWorkspace> beforeMonitorChange =
                previous.SetItems(desired);
            Volatile.Write(ref _workspaces, beforeMonitorChange);
            try
            {
                await _monitors.ReconcileAsync(desired.Values.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref _workspaces, previous);
                throw;
            }

            Volatile.Write(ref _workspaces, desired);
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    public async IAsyncEnumerable<ProtectionInspectionEvent> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ProtectionInspectionEvent item in
                       _results.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (WorkspaceMonitorEvent monitorEvent in
                           _monitors.ObserveAsync(cancellationToken).ConfigureAwait(false))
            {
                ProtectionInspectionEvent result = await ProcessAsync(monitorEvent, cancellationToken)
                    .ConfigureAwait(false);
                await _results.Writer.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            }
            _results.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _results.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            _results.Writer.TryComplete(exception);
        }
    }

    private async ValueTask<ProtectionInspectionEvent> ProcessAsync(
        WorkspaceMonitorEvent monitorEvent,
        CancellationToken cancellationToken)
    {
        if (monitorEvent.Kind != WorkspaceMonitorEventKind.Observation ||
            monitorEvent.Observation is null)
            return ProtectionInspectionEvent.MonitorOnly(monitorEvent);

        ImmutableDictionary<WorkspaceId, ProtectedWorkspace> workspaces =
            Volatile.Read(ref _workspaces);
        if (!workspaces.TryGetValue(monitorEvent.WorkspaceId, out ProtectedWorkspace? workspace))
            return ProtectionInspectionEvent.WorkspaceUnavailable(monitorEvent);

        try
        {
            ProtectionCandidateInspectionResult inspection = await _processor
                .ProcessAsync(workspace, monitorEvent, cancellationToken)
                .ConfigureAwait(false);
            return ProtectionInspectionEvent.Inspected(monitorEvent, inspection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ProtectionInspectionEvent.ProcessingFailed(monitorEvent);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        await _monitors.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _results.Writer.TryComplete();
        _lifetime.Dispose();
        _reconcileGate.Dispose();
    }
}
