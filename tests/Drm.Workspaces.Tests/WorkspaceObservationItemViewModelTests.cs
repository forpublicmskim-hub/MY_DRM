using Drm.Application;
using Drm.Desktop.Localization;
using Drm.Desktop.ViewModels;
using Drm.Domain;

namespace Drm.Workspaces.Tests;

public sealed class WorkspaceObservationItemViewModelTests
{
    [Fact]
    public void EligibleInspectionIsShownAsCandidateAndNotAsProtected()
    {
        WorkspaceObservation observation = Observation();
        ProtectionCandidateCollectionResult collection = Collection();
        ProtectionCandidateDecision decision = new(
            ProtectionEvaluationOutcome.Eligible,
            ProtectionCandidateReasonCodes.Eligible,
            new PolicySnapshotIdentity(Guid.NewGuid(), 1, new string('a', 64)),
            observation.WorkspaceId,
            observation.RelativePath,
            observation.ObservedAt);
        ProtectionInspectionEvent inspectionEvent = ProtectionInspectionEvent.Inspected(
            MonitorEvent(observation),
            ProtectionCandidateInspectionResult.Evaluated(collection, decision));

        WorkspaceObservationItemViewModel viewModel = new(
            "Workspace", observation, inspectionEvent, new KeyLocalizationService());

        Assert.Equal("Inspection.Collection.Collected", viewModel.CollectionStatus);
        Assert.Equal("Inspection.Evaluation.Eligible", viewModel.EvaluationStatus);
        Assert.Equal("Inspection.Reason.Eligible", viewModel.Reason);
        Assert.DoesNotContain("Protected", viewModel.EvaluationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingPolicyIsShownAsNotEvaluated()
    {
        WorkspaceObservation observation = Observation();
        ProtectionInspectionEvent inspectionEvent = ProtectionInspectionEvent.Inspected(
            MonitorEvent(observation),
            ProtectionCandidateInspectionResult.NoPolicy(Collection()));

        WorkspaceObservationItemViewModel viewModel = new(
            "Workspace", observation, inspectionEvent, new KeyLocalizationService());

        Assert.Equal("Inspection.Evaluation.NotEvaluated", viewModel.EvaluationStatus);
        Assert.Equal("Inspection.Reason.PolicyNotLoaded", viewModel.Reason);
    }

    [Fact]
    public void PipelineFailureIsShownWithoutInspectionResult()
    {
        WorkspaceObservation observation = Observation();
        ProtectionInspectionEvent inspectionEvent =
            ProtectionInspectionEvent.ProcessingFailed(MonitorEvent(observation));

        WorkspaceObservationItemViewModel viewModel = new(
            "Workspace", observation, inspectionEvent, new KeyLocalizationService());

        Assert.Equal("Inspection.Collection.NotAvailable", viewModel.CollectionStatus);
        Assert.Equal("Inspection.Evaluation.NotEvaluated", viewModel.EvaluationStatus);
        Assert.Equal("Inspection.Reason.ProcessingFailed", viewModel.Reason);
    }

    private static WorkspaceObservation Observation() => new(
        new WorkspaceId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
        WorkspaceObservationKind.Created,
        "report.pdf",
        null,
        new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));

    private static WorkspaceMonitorEvent MonitorEvent(WorkspaceObservation observation) => new(
        observation.WorkspaceId,
        WorkspaceMonitorEventKind.Observation,
        WorkspaceMonitorState.Watching,
        observation);

    private static ProtectionCandidateCollectionResult Collection()
    {
        WorkspaceObservation observation = Observation();
        ProtectionCandidate candidate = new(
            observation.WorkspaceId,
            observation.RelativePath,
            ".pdf",
            ProtectionCandidateAge.New,
            ProtectionDiscoveryKind.Created,
            false,
            100);
        return ProtectionCandidateCollectionResult.Collected(
            candidate,
            new FileVersionStamp(100, observation.ObservedAt));
    }

    private sealed class KeyLocalizationService : ILocalizationService
    {
        public string GetString(string key) => key;
        public string GetStringForCulture(string key, System.Globalization.CultureInfo culture) => key;
        public string Format(string key, params object?[] arguments) => key;
    }
}
