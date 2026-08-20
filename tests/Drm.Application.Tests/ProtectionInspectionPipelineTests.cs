using System.Collections.Immutable;
using System.Threading.Channels;
using Drm.Application;
using Drm.Domain;
using Drm.Policy;

namespace Drm.Application.Tests;

public sealed class ProtectionInspectionPipelineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WorkspaceSnapshotExistsBeforeInitialMonitorEvent()
    {
        ProtectedWorkspace workspace = Workspace();
        FakeMonitorCoordinator monitors = new();
        monitors.OnReconcile = workspaces => monitors.Publish(
            Observation(workspaces.Single(), WorkspaceObservationKind.Existing));
        await using ProtectionInspectionPipeline pipeline = Pipeline(monitors);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        Task<ProtectionInspectionEvent> observed = ReadOneAsync(pipeline, timeout.Token);

        await pipeline.ReconcileAsync([workspace], timeout.Token);

        ProtectionInspectionEvent result = await observed;
        Assert.Equal(ProtectionInspectionEventStatus.Inspected, result.Status);
        Assert.Equal(ProtectionEvaluationOutcome.Eligible, result.Inspection!.Decision!.Outcome);
        Assert.Equal(ProtectionCandidateAge.Existing, result.Inspection.Collection.Candidate!.Age);
    }

    [Fact]
    public async Task StateEventIsForwardedWithoutInspection()
    {
        ProtectedWorkspace workspace = Workspace();
        FakeMonitorCoordinator monitors = new();
        await using ProtectionInspectionPipeline pipeline = Pipeline(monitors);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await pipeline.ReconcileAsync([workspace], timeout.Token);
        Task<ProtectionInspectionEvent> observed = ReadOneAsync(pipeline, timeout.Token);

        monitors.Publish(new WorkspaceMonitorEvent(
            workspace.Id, WorkspaceMonitorEventKind.StateChanged, WorkspaceMonitorState.Watching));

        ProtectionInspectionEvent result = await observed;
        Assert.Equal(ProtectionInspectionEventStatus.MonitorOnly, result.Status);
        Assert.Null(result.Inspection);
    }

    [Fact]
    public async Task UnknownWorkspaceIsReportedWithoutStoppingPipeline()
    {
        ProtectedWorkspace workspace = Workspace();
        FakeMonitorCoordinator monitors = new();
        await using ProtectionInspectionPipeline pipeline = Pipeline(monitors);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        Task<ProtectionInspectionEvent> first = ReadOneAsync(pipeline, timeout.Token);
        monitors.Publish(Observation(workspace, WorkspaceObservationKind.Created));

        ProtectionInspectionEvent unavailable = await first;
        Assert.Equal(ProtectionInspectionEventStatus.WorkspaceUnavailable, unavailable.Status);

        await pipeline.ReconcileAsync([workspace], timeout.Token);
        Task<ProtectionInspectionEvent> second = ReadOneAsync(pipeline, timeout.Token);
        monitors.Publish(Observation(workspace, WorkspaceObservationKind.Created));
        Assert.Equal(ProtectionInspectionEventStatus.Inspected, (await second).Status);
    }

    [Fact]
    public async Task RemovedWorkspaceIsUnavailableAfterMonitorReconcileCompletes()
    {
        ProtectedWorkspace workspace = Workspace();
        FakeMonitorCoordinator monitors = new();
        await using ProtectionInspectionPipeline pipeline = Pipeline(monitors);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await pipeline.ReconcileAsync([workspace], timeout.Token);
        await pipeline.ReconcileAsync([], timeout.Token);
        Task<ProtectionInspectionEvent> observed = ReadOneAsync(pipeline, timeout.Token);

        monitors.Publish(Observation(workspace, WorkspaceObservationKind.Created));

        Assert.Equal(ProtectionInspectionEventStatus.WorkspaceUnavailable, (await observed).Status);
    }

    [Fact]
    public async Task FileProcessingFailureDoesNotStopFollowingEvents()
    {
        ProtectedWorkspace workspace = Workspace();
        FakeMonitorCoordinator monitors = new();
        ThrowOnceMetadataReader reader = new();
        await using ProtectionInspectionPipeline pipeline = Pipeline(monitors, reader);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await pipeline.ReconcileAsync([workspace], timeout.Token);
        Task<ProtectionInspectionEvent[]> observed = ReadManyAsync(pipeline, 2, timeout.Token);

        monitors.Publish(Observation(workspace, WorkspaceObservationKind.Created, "first.pdf"));
        monitors.Publish(Observation(workspace, WorkspaceObservationKind.Created, "second.pdf"));

        ProtectionInspectionEvent[] results = await observed;
        Assert.Equal(ProtectionInspectionEventStatus.ProcessingFailed, results[0].Status);
        Assert.Equal(ProtectionInspectionEventStatus.Inspected, results[1].Status);
    }

    [Fact]
    public async Task DisposeStopsPumpAndDisposesMonitorCoordinator()
    {
        FakeMonitorCoordinator monitors = new();
        ProtectionInspectionPipeline pipeline = Pipeline(monitors);

        await pipeline.DisposeAsync();

        Assert.True(monitors.IsDisposed);
        await pipeline.DisposeAsync();
    }

    private static ProtectionInspectionPipeline Pipeline(
        FakeMonitorCoordinator monitors,
        IProtectionCandidateMetadataReader? reader = null)
    {
        ProtectionCandidateCollector collector = new(reader ?? new StubMetadataReader());
        ProtectionCandidateInspectionProcessor processor = new(
            collector,
            new FixedPolicyProvider(),
            new FixedClock());
        return new ProtectionInspectionPipeline(monitors, processor);
    }

    private static async Task<ProtectionInspectionEvent> ReadOneAsync(
        ProtectionInspectionPipeline pipeline,
        CancellationToken cancellationToken)
    {
        await foreach (ProtectionInspectionEvent item in pipeline.ObserveAsync(cancellationToken))
            return item;
        throw new InvalidOperationException("The pipeline completed before producing an event.");
    }

    private static async Task<ProtectionInspectionEvent[]> ReadManyAsync(
        ProtectionInspectionPipeline pipeline,
        int count,
        CancellationToken cancellationToken)
    {
        List<ProtectionInspectionEvent> results = [];
        await foreach (ProtectionInspectionEvent item in pipeline.ObserveAsync(cancellationToken))
        {
            results.Add(item);
            if (results.Count == count) return results.ToArray();
        }
        throw new InvalidOperationException("The pipeline completed before producing all events.");
    }

    private static WorkspaceMonitorEvent Observation(
        ProtectedWorkspace workspace,
        WorkspaceObservationKind kind,
        string relativePath = "report.pdf")
    {
        WorkspaceObservation observation = new(
            workspace.Id, kind, relativePath, null, Now);
        return new WorkspaceMonitorEvent(
            workspace.Id, WorkspaceMonitorEventKind.Observation,
            WorkspaceMonitorState.Watching, observation);
    }

    private static ProtectedWorkspace Workspace() => new(
        new WorkspaceId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
        "Test",
        new WorkspaceLocation("C:\\Test", "C:\\Test"),
        WorkspaceRegistrationState.Registered,
        WorkspaceProtectionState.NotActivated,
        Now);

    private static InspectedProtectionPolicy Policy()
    {
        Guid id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        EffectiveProtectionPolicy effective = new(
            id, 1, "Policy", true, true, true,
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".pdf"),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".tmp", ".drm"),
            10_000, null, null);
        return new InspectedProtectionPolicy(
            effective,
            new PolicySnapshotIdentity(id, 1, new string('a', 64)),
            "policy.json",
            Now,
            ProtectionPolicyTrustState.UnsignedDevelopmentDraft);
    }

    private sealed class FakeMonitorCoordinator : IWorkspaceMonitorCoordinator
    {
        private readonly Channel<WorkspaceMonitorEvent> _events =
            Channel.CreateUnbounded<WorkspaceMonitorEvent>();

        public Action<IReadOnlyCollection<ProtectedWorkspace>>? OnReconcile { get; set; }
        public bool IsDisposed { get; private set; }

        public ValueTask ReconcileAsync(
            IReadOnlyCollection<ProtectedWorkspace> workspaces,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnReconcile?.Invoke(workspaces);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<WorkspaceMonitorEvent> ObserveAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (WorkspaceMonitorEvent item in
                           _events.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }

        public void Publish(WorkspaceMonitorEvent monitorEvent) =>
            Assert.True(_events.Writer.TryWrite(monitorEvent));

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private class StubMetadataReader : IProtectionCandidateMetadataReader
    {
        public virtual ValueTask<ProtectionCandidateMetadataResult> ReadAsync(
            ProtectedWorkspace workspace,
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileVersionStamp stamp = new(100, Now);
            return ValueTask.FromResult(ProtectionCandidateMetadataResult.Available(
                new ProtectionCandidateMetadata(relativePath, ".pdf", false, 100, stamp)));
        }
    }

    private sealed class ThrowOnceMetadataReader : StubMetadataReader
    {
        private int _calls;

        public override ValueTask<ProtectionCandidateMetadataResult> ReadAsync(
            ProtectedWorkspace workspace,
            string relativePath,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new IOException("Simulated metadata failure.");
            return base.ReadAsync(workspace, relativePath, cancellationToken);
        }
    }

    private sealed class FixedPolicyProvider : ICurrentProtectionPolicyProvider
    {
        public InspectedProtectionPolicy? Current => Policy();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
