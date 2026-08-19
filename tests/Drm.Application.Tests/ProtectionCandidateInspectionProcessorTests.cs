using System.Collections.Immutable;
using Drm.Application;
using Drm.Domain;
using Drm.Policy;

namespace Drm.Application.Tests;

public sealed class ProtectionCandidateInspectionProcessorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CollectedCandidateWithCurrentPolicyIsEvaluated()
    {
        TestContext test = CreateContext(Policy());

        ProtectionCandidateInspectionResult result = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, WorkspaceObservationKind.Created),
            default);

        Assert.True(result.IsEvaluated);
        Assert.Equal(ProtectionEvaluationOutcome.Eligible, result.Decision!.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.Eligible, result.Decision.ReasonCode);
        Assert.Equal(Now, result.Decision.EvaluatedAtUtc);
        Assert.Null(result.SkipReasonCode);
        Assert.Equal(1, test.Provider.ReadCount);
    }

    [Fact]
    public async Task EvaluatorOutcomeIsPreservedForExcludedExtension()
    {
        TestContext test = CreateContext(Policy(excluded: [".pdf"]));

        ProtectionCandidateInspectionResult result = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, WorkspaceObservationKind.Created),
            default);

        Assert.Equal(ProtectionEvaluationOutcome.Excluded, result.Decision!.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.ExtensionExcluded, result.Decision.ReasonCode);
    }

    [Fact]
    public async Task InactivePolicyIsNotReportedAsACollectionFailure()
    {
        TestContext test = CreateContext(Policy(enabled: false));

        ProtectionCandidateInspectionResult result = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, WorkspaceObservationKind.Created),
            default);

        Assert.True(result.IsEvaluated);
        Assert.Equal(ProtectionCandidateCollectionStatus.Collected, result.Collection.Status);
        Assert.Equal(ProtectionEvaluationOutcome.PolicyInactive, result.Decision!.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.PolicyDisabled, result.Decision.ReasonCode);
    }

    [Fact]
    public async Task CollectedCandidateWithoutPolicyIsMarkedForReevaluation()
    {
        TestContext test = CreateContext((EffectiveProtectionPolicy?)null);

        ProtectionCandidateInspectionResult result = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, WorkspaceObservationKind.Created),
            default);

        Assert.False(result.IsEvaluated);
        Assert.Equal(ProtectionCandidateCollectionStatus.Collected, result.Collection.Status);
        Assert.Null(result.Decision);
        Assert.Equal(ProtectionCandidateInspectionReasonCodes.PolicyNotLoaded, result.SkipReasonCode);
        Assert.Equal(1, test.Provider.ReadCount);
    }

    [Theory]
    [InlineData(WorkspaceObservationKind.Deleted, ProtectionCandidateCollectionStatus.Ignored)]
    [InlineData(WorkspaceObservationKind.Modified, ProtectionCandidateCollectionStatus.Deferred)]
    [InlineData(WorkspaceObservationKind.Renamed, ProtectionCandidateCollectionStatus.Deferred)]
    public async Task CandidateThatWasNotCollectedDoesNotReadCurrentPolicy(
        WorkspaceObservationKind kind,
        ProtectionCandidateCollectionStatus expectedStatus)
    {
        TestContext test = CreateContext(Policy());

        ProtectionCandidateInspectionResult result = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, kind),
            default);

        Assert.False(result.IsEvaluated);
        Assert.Equal(expectedStatus, result.Collection.Status);
        Assert.Equal(result.Collection.ReasonCode, result.SkipReasonCode);
        Assert.Null(result.Decision);
        Assert.Equal(0, test.Provider.ReadCount);
    }

    [Fact]
    public async Task CurrentPolicySnapshotIsReadExactlyOnce()
    {
        InspectedProtectionPolicy first = Inspected(Policy(policyVersion: 3), 'a');
        InspectedProtectionPolicy replacement = Inspected(Policy(policyVersion: 4), 'b');
        TestContext test = CreateContext(first, replacement);

        ProtectionCandidateInspectionResult result = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, WorkspaceObservationKind.Created),
            default);

        Assert.Equal(1, test.Provider.ReadCount);
        Assert.Equal(first.Identity, result.Decision!.Policy);
        Assert.NotEqual(replacement.Identity, result.Decision.Policy);
    }

    [Fact]
    public async Task CancellationFromCollectionIsPropagated()
    {
        TestContext test = CreateContext(Policy());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await test.Processor.ProcessAsync(
                test.Workspace,
                Observation(test.Workspace, WorkspaceObservationKind.Created),
                cancellation.Token));

        Assert.Equal(0, test.Provider.ReadCount);
    }

    [Fact]
    public async Task ResultFactoriesRejectContradictoryStatesAndCandidateDecisions()
    {
        TestContext test = CreateContext(Policy());
        ProtectionCandidateInspectionResult evaluated = await test.Processor.ProcessAsync(
            test.Workspace,
            Observation(test.Workspace, WorkspaceObservationKind.Created),
            default);
        ProtectionCandidateCollectionResult ignored =
            ProtectionCandidateCollectionResult.Ignored(ProtectionCandidateCollectionReasonCodes.Deleted);

        Assert.Throws<ArgumentException>(() =>
            ProtectionCandidateInspectionResult.NotCollected(evaluated.Collection));
        Assert.Throws<ArgumentException>(() =>
            ProtectionCandidateInspectionResult.NoPolicy(ignored));
        Assert.Throws<ArgumentException>(() =>
            ProtectionCandidateInspectionResult.Evaluated(
                evaluated.Collection,
                evaluated.Decision! with { RelativePath = "other.pdf" }));
    }

    private static TestContext CreateContext(
        EffectiveProtectionPolicy? policy,
        InspectedProtectionPolicy? replacement = null) =>
        CreateContext(policy is null ? null : Inspected(policy), replacement);

    private static TestContext CreateContext(
        InspectedProtectionPolicy? policy,
        InspectedProtectionPolicy? replacement = null)
    {
        ProtectedWorkspace workspace = new(
            new WorkspaceId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
            "Test",
            new WorkspaceLocation("C:\\Test", "C:\\Test"),
            WorkspaceRegistrationState.Registered,
            WorkspaceProtectionState.NotActivated,
            Now);
        StubMetadataReader reader = new();
        SequencePolicyProvider provider = new(policy, replacement);
        ProtectionCandidateInspectionProcessor processor = new(
            new ProtectionCandidateCollector(reader),
            provider,
            new FixedClock());
        return new(processor, workspace, provider);
    }

    private static WorkspaceMonitorEvent Observation(
        ProtectedWorkspace workspace,
        WorkspaceObservationKind kind)
    {
        WorkspaceObservation observation = new(
            workspace.Id,
            kind,
            "documents/report.pdf",
            null,
            Now);
        return new WorkspaceMonitorEvent(
            workspace.Id,
            WorkspaceMonitorEventKind.Observation,
            WorkspaceMonitorState.Watching,
            observation);
    }

    private static InspectedProtectionPolicy Inspected(
        EffectiveProtectionPolicy policy,
        char digestCharacter = 'a') =>
        new(
            policy,
            new PolicySnapshotIdentity(
                policy.PolicyId,
                policy.PolicyVersion,
                new string(digestCharacter, 64)),
            "policy.json",
            Now,
            ProtectionPolicyTrustState.UnsignedDevelopmentDraft);

    private static EffectiveProtectionPolicy Policy(
        bool enabled = true,
        int policyVersion = 3,
        IEnumerable<string>? excluded = null) =>
        new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            policyVersion,
            "Policy",
            enabled,
            true,
            true,
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".pdf"),
            (excluded ?? [".tmp", ".drm"]).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            10_000,
            null,
            null);

    private sealed class StubMetadataReader : IProtectionCandidateMetadataReader
    {
        public ValueTask<ProtectionCandidateMetadataResult> ReadAsync(
            ProtectedWorkspace workspace,
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileVersionStamp version = new(1_000, Now);
            ProtectionCandidateMetadata metadata = new(
                relativePath,
                ".pdf",
                false,
                1_000,
                version);
            return ValueTask.FromResult(ProtectionCandidateMetadataResult.Available(metadata));
        }
    }

    private sealed class SequencePolicyProvider(
        InspectedProtectionPolicy? first,
        InspectedProtectionPolicy? replacement) : ICurrentProtectionPolicyProvider
    {
        public int ReadCount { get; private set; }

        public InspectedProtectionPolicy? Current
        {
            get
            {
                ReadCount++;
                return ReadCount == 1 ? first : replacement ?? first;
            }
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed record TestContext(
        ProtectionCandidateInspectionProcessor Processor,
        ProtectedWorkspace Workspace,
        SequencePolicyProvider Provider);
}
