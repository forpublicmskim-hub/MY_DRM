using Drm.Application;
using Drm.Domain;

namespace Drm.Application.Tests;

public sealed class ProtectionCandidateCollectorTests
{
    [Fact]
    public async Task ExistingObservationBecomesExistingInitialInventoryCandidate()
    {
        StubMetadataReader reader = new(AvailableMetadata("Report.PDF", ".pdf", 42));
        ProtectionCandidateCollectionResult result = await CollectAsync(
            reader, WorkspaceObservationKind.Existing, WorkspaceMonitorState.Starting);

        Assert.Equal(ProtectionCandidateCollectionStatus.Collected, result.Status);
        Assert.Equal(ProtectionCandidateAge.Existing, result.Candidate!.Age);
        Assert.Equal(ProtectionDiscoveryKind.InitialInventory, result.Candidate.DiscoveryKind);
        Assert.Equal(".pdf", result.Candidate.NormalizedExtension);
        Assert.Equal(42, result.Version!.Length);
    }

    [Fact]
    public async Task CreatedDuringWatchingBecomesNewCreatedCandidate()
    {
        StubMetadataReader reader = new(AvailableMetadata("report.pdf", ".pdf", 10));
        ProtectionCandidateCollectionResult result = await CollectAsync(
            reader, WorkspaceObservationKind.Created, WorkspaceMonitorState.Watching);

        Assert.Equal(ProtectionCandidateAge.New, result.Candidate!.Age);
        Assert.Equal(ProtectionDiscoveryKind.Created, result.Candidate.DiscoveryKind);
    }

    [Fact]
    public async Task CreatedDuringRescanBecomesReconciliationCandidate()
    {
        StubMetadataReader reader = new(AvailableMetadata("report.pdf", ".pdf", 10));
        ProtectionCandidateCollectionResult result = await CollectAsync(
            reader, WorkspaceObservationKind.Created, WorkspaceMonitorState.Rescanning);

        Assert.Equal(ProtectionDiscoveryKind.Reconciliation, result.Candidate!.DiscoveryKind);
    }

    [Theory]
    [InlineData(WorkspaceObservationKind.Deleted, ProtectionCandidateCollectionStatus.Ignored, ProtectionCandidateCollectionReasonCodes.Deleted)]
    [InlineData(WorkspaceObservationKind.Modified, ProtectionCandidateCollectionStatus.Deferred, ProtectionCandidateCollectionReasonCodes.AgeUnknown)]
    [InlineData(WorkspaceObservationKind.Renamed, ProtectionCandidateCollectionStatus.Deferred, ProtectionCandidateCollectionReasonCodes.AgeUnknown)]
    public async Task EventsWithoutReliableCandidateAgeDoNotReadMetadata(
        WorkspaceObservationKind kind,
        ProtectionCandidateCollectionStatus expectedStatus,
        string expectedReason)
    {
        StubMetadataReader reader = new(AvailableMetadata("report.pdf", ".pdf", 10));

        ProtectionCandidateCollectionResult result = await CollectAsync(
            reader, kind, WorkspaceMonitorState.Watching);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReason, result.ReasonCode);
        Assert.Equal(0, reader.CallCount);
        Assert.Null(result.Candidate);
    }

    [Fact]
    public async Task StateEventDoesNotReadMetadata()
    {
        ProtectedWorkspace workspace = CreateWorkspace();
        StubMetadataReader reader = new(AvailableMetadata("report.pdf", ".pdf", 10));
        WorkspaceMonitorEvent monitorEvent = new(
            workspace.Id, WorkspaceMonitorEventKind.StateChanged, WorkspaceMonitorState.Watching);

        ProtectionCandidateCollectionResult result =
            await new ProtectionCandidateCollector(reader).CollectAsync(workspace, monitorEvent, default);

        Assert.Equal(ProtectionCandidateCollectionStatus.Ignored, result.Status);
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task WorkspaceMismatchIsRejectedWithoutReadingMetadata()
    {
        ProtectedWorkspace workspace = CreateWorkspace();
        StubMetadataReader reader = new(AvailableMetadata("report.pdf", ".pdf", 10));
        WorkspaceObservation observation = new(
            WorkspaceId.New(), WorkspaceObservationKind.Created, "report.pdf", null, DateTimeOffset.UtcNow);
        WorkspaceMonitorEvent monitorEvent = new(
            workspace.Id, WorkspaceMonitorEventKind.Observation, WorkspaceMonitorState.Watching, observation);

        ProtectionCandidateCollectionResult result =
            await new ProtectionCandidateCollector(reader).CollectAsync(workspace, monitorEvent, default);

        Assert.Equal(ProtectionCandidateCollectionStatus.Rejected, result.Status);
        Assert.Equal(ProtectionCandidateCollectionReasonCodes.WorkspaceMismatch, result.ReasonCode);
        Assert.Equal(0, reader.CallCount);
    }

    [Theory]
    [InlineData(ProtectionCandidateMetadataStatus.NotFound, ProtectionCandidateCollectionStatus.Ignored)]
    [InlineData(ProtectionCandidateMetadataStatus.AccessDenied, ProtectionCandidateCollectionStatus.Deferred)]
    [InlineData(ProtectionCandidateMetadataStatus.Unstable, ProtectionCandidateCollectionStatus.Deferred)]
    [InlineData(ProtectionCandidateMetadataStatus.Unavailable, ProtectionCandidateCollectionStatus.Deferred)]
    [InlineData(ProtectionCandidateMetadataStatus.UnsafePath, ProtectionCandidateCollectionStatus.Rejected)]
    [InlineData(ProtectionCandidateMetadataStatus.SymbolicLinkNotSupported, ProtectionCandidateCollectionStatus.Rejected)]
    public async Task MetadataFailuresAreMappedWithoutCreatingCandidate(
        ProtectionCandidateMetadataStatus metadataStatus,
        ProtectionCandidateCollectionStatus expectedStatus)
    {
        StubMetadataReader reader = new(ProtectionCandidateMetadataResult.Failure(metadataStatus));

        ProtectionCandidateCollectionResult result = await CollectAsync(
            reader, WorkspaceObservationKind.Created, WorkspaceMonitorState.Watching);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Candidate);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task CancellationIsPropagatedBeforeReaderCall()
    {
        StubMetadataReader reader = new(AvailableMetadata("report.pdf", ".pdf", 10));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CollectAsync(reader, WorkspaceObservationKind.Created,
                WorkspaceMonitorState.Watching, cancellation.Token));
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public void ResultFactoriesPreventContradictoryMetadataStates()
    {
        Assert.Throws<ArgumentException>(() =>
            ProtectionCandidateMetadataResult.Failure(ProtectionCandidateMetadataStatus.Available));
        Assert.Throws<ArgumentNullException>(() =>
            ProtectionCandidateMetadataResult.Available(null!));
    }

    private static async Task<ProtectionCandidateCollectionResult> CollectAsync(
        StubMetadataReader reader,
        WorkspaceObservationKind kind,
        WorkspaceMonitorState state,
        CancellationToken cancellationToken = default)
    {
        ProtectedWorkspace workspace = CreateWorkspace();
        WorkspaceObservation observation = new(
            workspace.Id, kind, "report.pdf", null, DateTimeOffset.UtcNow);
        WorkspaceMonitorEvent monitorEvent = new(
            workspace.Id, WorkspaceMonitorEventKind.Observation, state, observation);
        return await new ProtectionCandidateCollector(reader)
            .CollectAsync(workspace, monitorEvent, cancellationToken);
    }

    private static ProtectionCandidateMetadataResult AvailableMetadata(
        string relativePath,
        string extension,
        long length)
    {
        FileVersionStamp version = new(
            length,
            new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));
        return ProtectionCandidateMetadataResult.Available(
            new ProtectionCandidateMetadata(relativePath, extension, false, length, version));
    }

    private static ProtectedWorkspace CreateWorkspace() => new(
        WorkspaceId.New(),
        "Test",
        new WorkspaceLocation("C:\\Test", "C:\\Test"),
        WorkspaceRegistrationState.Registered,
        WorkspaceProtectionState.NotActivated,
        DateTimeOffset.UtcNow);

    private sealed class StubMetadataReader(ProtectionCandidateMetadataResult result)
        : IProtectionCandidateMetadataReader
    {
        public int CallCount { get; private set; }

        public ValueTask<ProtectionCandidateMetadataResult> ReadAsync(
            ProtectedWorkspace workspace,
            string relativePath,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }
}
