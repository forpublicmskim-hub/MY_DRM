using Drm.Domain;

namespace Drm.Application;

public interface IProtectionCandidateMetadataReader
{
    ValueTask<ProtectionCandidateMetadataResult> ReadAsync(ProtectedWorkspace workspace, string relativePath, CancellationToken cancellationToken);
}

public enum ProtectionCandidateMetadataStatus { Available, NotFound, AccessDenied, Unstable, UnsafePath, SymbolicLinkNotSupported, Unavailable }

public sealed record FileVersionStamp(long? Length, DateTimeOffset LastWriteTimeUtc);

public sealed record ProtectionCandidateMetadata(
    string RelativePath, string NormalizedExtension, bool IsDirectory, long? FileSizeBytes, FileVersionStamp Version);

public sealed record ProtectionCandidateMetadataResult
{
    private ProtectionCandidateMetadataResult(ProtectionCandidateMetadataStatus status, ProtectionCandidateMetadata? metadata)
    {
        Status = status;
        Metadata = metadata;
    }

    public ProtectionCandidateMetadataStatus Status { get; }
    public ProtectionCandidateMetadata? Metadata { get; }

    public static ProtectionCandidateMetadataResult Available(ProtectionCandidateMetadata metadata) =>
        new(ProtectionCandidateMetadataStatus.Available, metadata ?? throw new ArgumentNullException(nameof(metadata)));

    public static ProtectionCandidateMetadataResult Failure(ProtectionCandidateMetadataStatus status)
    {
        if (status == ProtectionCandidateMetadataStatus.Available)
            throw new ArgumentException("Available metadata requires a value.", nameof(status));
        return new(status, null);
    }
}

public enum ProtectionCandidateCollectionStatus { Collected, Ignored, Deferred, Rejected }

public static class ProtectionCandidateCollectionReasonCodes
{
    public const string Collected = "candidate.collection.collected";
    public const string Deleted = "candidate.collection.deleted";
    public const string UnsupportedObservation = "candidate.collection.observation-unsupported";
    public const string WorkspaceMismatch = "candidate.collection.workspace-mismatch";
    public const string NotFound = "candidate.collection.not-found";
    public const string AccessDenied = "candidate.collection.access-denied";
    public const string FileUnstable = "candidate.collection.file-unstable";
    public const string UnsafePath = "candidate.collection.unsafe-path";
    public const string SymbolicLink = "candidate.collection.symbolic-link";
    public const string Unavailable = "candidate.collection.unavailable";
    public const string AgeUnknown = "candidate.collection.age-unknown";
}

public sealed record ProtectionCandidateCollectionResult
{
    private ProtectionCandidateCollectionResult(ProtectionCandidateCollectionStatus status, string reasonCode, ProtectionCandidate? candidate, FileVersionStamp? version)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A reason code is required.", nameof(reasonCode));
        Status = status;
        ReasonCode = reasonCode;
        Candidate = candidate;
        Version = version;
    }

    public ProtectionCandidateCollectionStatus Status { get; }
    public string ReasonCode { get; }
    public ProtectionCandidate? Candidate { get; }
    public FileVersionStamp? Version { get; }

    public static ProtectionCandidateCollectionResult Collected(ProtectionCandidate candidate, FileVersionStamp version) =>
        new(ProtectionCandidateCollectionStatus.Collected, ProtectionCandidateCollectionReasonCodes.Collected,
            candidate ?? throw new ArgumentNullException(nameof(candidate)), version ?? throw new ArgumentNullException(nameof(version)));
    public static ProtectionCandidateCollectionResult Ignored(string reasonCode) => new(ProtectionCandidateCollectionStatus.Ignored, reasonCode, null, null);
    public static ProtectionCandidateCollectionResult Deferred(string reasonCode) => new(ProtectionCandidateCollectionStatus.Deferred, reasonCode, null, null);
    public static ProtectionCandidateCollectionResult Rejected(string reasonCode) => new(ProtectionCandidateCollectionStatus.Rejected, reasonCode, null, null);
}

public sealed class ProtectionCandidateCollector(IProtectionCandidateMetadataReader metadataReader)
{
    public async ValueTask<ProtectionCandidateCollectionResult> CollectAsync(
        ProtectedWorkspace workspace, WorkspaceMonitorEvent monitorEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(monitorEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (monitorEvent.Kind != WorkspaceMonitorEventKind.Observation || monitorEvent.Observation is null)
            return ProtectionCandidateCollectionResult.Ignored(ProtectionCandidateCollectionReasonCodes.UnsupportedObservation);

        WorkspaceObservation observation = monitorEvent.Observation;
        if (workspace.Id != monitorEvent.WorkspaceId || workspace.Id != observation.WorkspaceId)
            return ProtectionCandidateCollectionResult.Rejected(ProtectionCandidateCollectionReasonCodes.WorkspaceMismatch);

        if (observation.Kind == WorkspaceObservationKind.Deleted)
            return ProtectionCandidateCollectionResult.Ignored(ProtectionCandidateCollectionReasonCodes.Deleted);

        CandidateClassification? classification = Classify(monitorEvent.State, observation.Kind);
        if (classification is null)
            return ProtectionCandidateCollectionResult.Deferred(ProtectionCandidateCollectionReasonCodes.AgeUnknown);

        ProtectionCandidateMetadataResult metadataResult = await metadataReader
            .ReadAsync(workspace, observation.RelativePath, cancellationToken).ConfigureAwait(false);
        if (metadataResult.Status != ProtectionCandidateMetadataStatus.Available)
            return MapFailure(metadataResult.Status);

        ProtectionCandidateMetadata metadata = metadataResult.Metadata
            ?? throw new InvalidOperationException("Available metadata result has no metadata.");
        ProtectionCandidate candidate = new(workspace.Id, metadata.RelativePath, metadata.NormalizedExtension,
            classification.Value.Age, classification.Value.DiscoveryKind, metadata.IsDirectory, metadata.FileSizeBytes);
        return ProtectionCandidateCollectionResult.Collected(candidate, metadata.Version);
    }

    private static CandidateClassification? Classify(WorkspaceMonitorState state, WorkspaceObservationKind kind) => kind switch
    {
        WorkspaceObservationKind.Existing => new(ProtectionCandidateAge.Existing,
            state == WorkspaceMonitorState.Rescanning ? ProtectionDiscoveryKind.Reconciliation : ProtectionDiscoveryKind.InitialInventory),
        WorkspaceObservationKind.Created => new(ProtectionCandidateAge.New,
            state == WorkspaceMonitorState.Rescanning ? ProtectionDiscoveryKind.Reconciliation : ProtectionDiscoveryKind.Created),
        _ => null
    };

    private static ProtectionCandidateCollectionResult MapFailure(ProtectionCandidateMetadataStatus status) => status switch
    {
        ProtectionCandidateMetadataStatus.NotFound => ProtectionCandidateCollectionResult.Ignored(ProtectionCandidateCollectionReasonCodes.NotFound),
        ProtectionCandidateMetadataStatus.AccessDenied => ProtectionCandidateCollectionResult.Deferred(ProtectionCandidateCollectionReasonCodes.AccessDenied),
        ProtectionCandidateMetadataStatus.Unstable => ProtectionCandidateCollectionResult.Deferred(ProtectionCandidateCollectionReasonCodes.FileUnstable),
        ProtectionCandidateMetadataStatus.Unavailable => ProtectionCandidateCollectionResult.Deferred(ProtectionCandidateCollectionReasonCodes.Unavailable),
        ProtectionCandidateMetadataStatus.UnsafePath => ProtectionCandidateCollectionResult.Rejected(ProtectionCandidateCollectionReasonCodes.UnsafePath),
        ProtectionCandidateMetadataStatus.SymbolicLinkNotSupported => ProtectionCandidateCollectionResult.Rejected(ProtectionCandidateCollectionReasonCodes.SymbolicLink),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private readonly record struct CandidateClassification(ProtectionCandidateAge Age, ProtectionDiscoveryKind DiscoveryKind);
}
