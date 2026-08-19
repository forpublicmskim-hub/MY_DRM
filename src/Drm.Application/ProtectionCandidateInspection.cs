using Drm.Domain;

namespace Drm.Application;

public static class ProtectionCandidateInspectionReasonCodes
{
    public const string PolicyNotLoaded = "policy.not-loaded";
}

public sealed record ProtectionCandidateInspectionResult
{
    private ProtectionCandidateInspectionResult(
        ProtectionCandidateCollectionResult collection,
        ProtectionCandidateDecision? decision,
        string? skipReasonCode)
    {
        Collection = collection;
        Decision = decision;
        SkipReasonCode = skipReasonCode;
    }

    public ProtectionCandidateCollectionResult Collection { get; }
    public ProtectionCandidateDecision? Decision { get; }
    public string? SkipReasonCode { get; }
    public bool IsEvaluated => Decision is not null;

    public static ProtectionCandidateInspectionResult Evaluated(
        ProtectionCandidateCollectionResult collection,
        ProtectionCandidateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(decision);
        EnsureCollected(collection);

        ProtectionCandidate candidate = collection.Candidate!;
        if (decision.WorkspaceId != candidate.WorkspaceId ||
            !string.Equals(decision.RelativePath, candidate.RelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The decision does not belong to the collected candidate.",
                nameof(decision));
        }

        return new(collection, decision, null);
    }

    public static ProtectionCandidateInspectionResult NotCollected(
        ProtectionCandidateCollectionResult collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (collection.Status == ProtectionCandidateCollectionStatus.Collected)
            throw new ArgumentException("A collected candidate requires policy evaluation or a skip reason.", nameof(collection));
        return new(collection, null, collection.ReasonCode);
    }

    public static ProtectionCandidateInspectionResult NoPolicy(
        ProtectionCandidateCollectionResult collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        EnsureCollected(collection);
        return new(collection, null, ProtectionCandidateInspectionReasonCodes.PolicyNotLoaded);
    }

    private static void EnsureCollected(ProtectionCandidateCollectionResult collection)
    {
        if (collection.Status != ProtectionCandidateCollectionStatus.Collected ||
            collection.Candidate is null || collection.Version is null)
        {
            throw new ArgumentException("A complete collected candidate is required.", nameof(collection));
        }
    }
}

public sealed class ProtectionCandidateInspectionProcessor
{
    private readonly ProtectionCandidateCollector _collector;
    private readonly ICurrentProtectionPolicyProvider _policies;
    private readonly IClock _clock;

    public ProtectionCandidateInspectionProcessor(
        ProtectionCandidateCollector collector,
        ICurrentProtectionPolicyProvider policies,
        IClock clock)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask<ProtectionCandidateInspectionResult> ProcessAsync(
        ProtectedWorkspace workspace,
        WorkspaceMonitorEvent monitorEvent,
        CancellationToken cancellationToken = default)
    {
        ProtectionCandidateCollectionResult collection = await _collector
            .CollectAsync(workspace, monitorEvent, cancellationToken)
            .ConfigureAwait(false);

        if (collection.Status != ProtectionCandidateCollectionStatus.Collected)
            return ProtectionCandidateInspectionResult.NotCollected(collection);

        InspectedProtectionPolicy? policy = _policies.Current;
        if (policy is null)
            return ProtectionCandidateInspectionResult.NoPolicy(collection);

        ProtectionEvaluationContext context = new(
            _clock.UtcNow,
            PolicyUsageMode.Inspection);
        ProtectionCandidateDecision decision = ProtectionCandidateEvaluator.Evaluate(
            policy,
            collection.Candidate!,
            context);

        return ProtectionCandidateInspectionResult.Evaluated(collection, decision);
    }
}
