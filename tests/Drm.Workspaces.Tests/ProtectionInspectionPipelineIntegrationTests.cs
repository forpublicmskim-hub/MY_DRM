using System.Collections.Immutable;
using Drm.Application;
using Drm.Domain;
using Drm.Infrastructure;
using Drm.Platform.Local;
using Drm.Policy;

namespace Drm.Workspaces.Tests;

public sealed class ProtectionInspectionPipelineIntegrationTests
{
    [Fact]
    public async Task ExistingLocalFileIsInspectedWithoutChangingItsContents()
    {
        using TemporaryDirectory directory = new();
        string filePath = Path.Combine(directory.Path, "report.pdf");
        byte[] original = [1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(filePath, original);
        ProtectedWorkspace workspace = new(
            WorkspaceId.New(),
            "Integration",
            new WorkspaceLocation(directory.Path, directory.Path),
            WorkspaceRegistrationState.Registered,
            WorkspaceProtectionState.NotActivated,
            DateTimeOffset.UtcNow);
        WorkspaceMonitorManager manager = new(
            new FileSystemWatcherWorkspaceMonitorFactory(new LocalWorkspaceScanner()));
        ProtectionCandidateInspectionProcessor processor = new(
            new ProtectionCandidateCollector(new LocalProtectionCandidateMetadataReader()),
            new FixedPolicyProvider(),
            new SystemClock());
        await using ProtectionInspectionPipeline pipeline = new(manager, processor);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<ProtectionInspectionEvent> observed = WaitForExistingAsync(pipeline, timeout.Token);

        await pipeline.ReconcileAsync([workspace], timeout.Token);

        ProtectionInspectionEvent result = await observed;
        Assert.Equal(ProtectionInspectionEventStatus.Inspected, result.Status);
        Assert.Equal(ProtectionCandidateCollectionStatus.Collected, result.Inspection!.Collection.Status);
        Assert.Equal(ProtectionEvaluationOutcome.Eligible, result.Inspection.Decision!.Outcome);
        Assert.Equal(original, await File.ReadAllBytesAsync(filePath, timeout.Token));
    }

    private static async Task<ProtectionInspectionEvent> WaitForExistingAsync(
        ProtectionInspectionPipeline pipeline,
        CancellationToken cancellationToken)
    {
        await foreach (ProtectionInspectionEvent item in pipeline.ObserveAsync(cancellationToken))
            if (item.MonitorEvent.Observation?.Kind == WorkspaceObservationKind.Existing)
                return item;
        throw new InvalidOperationException("The pipeline completed before reporting the existing file.");
    }

    private sealed class FixedPolicyProvider : ICurrentProtectionPolicyProvider
    {
        public InspectedProtectionPolicy? Current { get; } = CreatePolicy();

        private static InspectedProtectionPolicy CreatePolicy()
        {
            Guid id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            EffectiveProtectionPolicy effective = new(
                id, 1, "Integration Policy", true, true, true,
                ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".pdf"),
                ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".tmp", ".drm"),
                10_000, null, null);
            return new InspectedProtectionPolicy(
                effective,
                new PolicySnapshotIdentity(id, 1, new string('a', 64)),
                "integration-policy.json",
                DateTimeOffset.UtcNow,
                ProtectionPolicyTrustState.UnsignedDevelopmentDraft);
        }
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
