using Drm.Domain;
using Drm.Policy;

namespace Drm.Application;

public enum ProtectionCandidateAge
{
    Existing,
    New
}

public enum ProtectionDiscoveryKind
{
    InitialInventory,
    Created,
    MovedIntoWorkspace,
    Modified,
    Renamed,
    Reconciliation,
    Retry,
    PolicyReevaluation
}

public sealed record ProtectionCandidate(
    WorkspaceId WorkspaceId,
    string RelativePath,
    string NormalizedExtension,
    ProtectionCandidateAge Age,
    ProtectionDiscoveryKind DiscoveryKind,
    bool IsDirectory,
    long? FileSizeBytes);

public enum PolicyUsageMode
{
    Inspection,
    Enforcement
}

public sealed record ProtectionEvaluationContext(
    DateTimeOffset EvaluatedAtUtc,
    PolicyUsageMode UsageMode);

public enum ProtectionEvaluationOutcome
{
    Eligible,
    Excluded,
    Deferred,
    PolicyInactive,
    Indeterminate
}

public static class ProtectionCandidateReasonCodes
{
    public const string Eligible = "protection.eligible";
    public const string PolicyNotEnforceable = "policy.not-enforceable";
    public const string PolicyIdentityInvalid = "policy.identity-invalid";
    public const string PolicyDisabled = "policy.disabled";
    public const string PolicyNotYetValid = "policy.not-yet-valid";
    public const string PolicyExpired = "policy.expired";
    public const string Directory = "candidate.directory";
    public const string AgeDisabled = "candidate.age-disabled";
    public const string ExtensionExcluded = "candidate.extension-excluded";
    public const string ExtensionNotIncluded = "candidate.extension-not-included";
    public const string FileTooLarge = "candidate.file-too-large";
    public const string MetadataUnavailable = "candidate.metadata-unavailable";
    public const string MetadataInvalid = "candidate.metadata-invalid";
    public const string FileUnstable = "candidate.file-unstable";
}

public sealed record ProtectionCandidateDecision(
    ProtectionEvaluationOutcome Outcome,
    string ReasonCode,
    PolicySnapshotIdentity Policy,
    WorkspaceId WorkspaceId,
    string RelativePath,
    DateTimeOffset EvaluatedAtUtc);

public static class ProtectionCandidateEvaluator
{
    public static ProtectionCandidateDecision Evaluate(
        InspectedProtectionPolicy inspectedPolicy,
        ProtectionCandidate candidate,
        ProtectionEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(inspectedPolicy);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        if (context.UsageMode != PolicyUsageMode.Inspection)
            return Decision(
                ProtectionEvaluationOutcome.PolicyInactive,
                ProtectionCandidateReasonCodes.PolicyNotEnforceable,
                inspectedPolicy.Identity,
                candidate,
                context);

        return EvaluateCore(inspectedPolicy.Policy, inspectedPolicy.Identity, candidate, context);
    }

    public static ProtectionCandidateDecision Evaluate(
        EnforceableProtectionPolicy enforceablePolicy,
        ProtectionCandidate candidate,
        ProtectionEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(enforceablePolicy);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        return EvaluateCore(
            enforceablePolicy.Policy,
            enforceablePolicy.Identity.Snapshot,
            candidate,
            context);
    }

    private static ProtectionCandidateDecision EvaluateCore(
        EffectiveProtectionPolicy policy,
        PolicySnapshotIdentity identity,
        ProtectionCandidate candidate,
        ProtectionEvaluationContext context)
    {
        if (!IsValidIdentity(policy, identity))
            return Decision(ProtectionEvaluationOutcome.PolicyInactive,
                ProtectionCandidateReasonCodes.PolicyIdentityInvalid, identity, candidate, context);

        if (!policy.Enabled)
            return Decision(ProtectionEvaluationOutcome.PolicyInactive,
                ProtectionCandidateReasonCodes.PolicyDisabled, identity, candidate, context);

        DateTimeOffset evaluatedAtUtc = context.EvaluatedAtUtc.ToUniversalTime();
        if (policy.ValidFromUtc is { } validFrom && evaluatedAtUtc < validFrom)
            return Decision(ProtectionEvaluationOutcome.PolicyInactive,
                ProtectionCandidateReasonCodes.PolicyNotYetValid, identity, candidate, context);
        if (policy.ValidUntilUtc is { } validUntil && evaluatedAtUtc >= validUntil)
            return Decision(ProtectionEvaluationOutcome.PolicyInactive,
                ProtectionCandidateReasonCodes.PolicyExpired, identity, candidate, context);

        if (candidate.IsDirectory)
            return Decision(ProtectionEvaluationOutcome.Excluded,
                ProtectionCandidateReasonCodes.Directory, identity, candidate, context);

        if (!IsAgeEnabled(policy, candidate.Age))
            return Decision(ProtectionEvaluationOutcome.Excluded,
                ProtectionCandidateReasonCodes.AgeDisabled, identity, candidate, context);

        if (candidate.WorkspaceId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(candidate.RelativePath) ||
            !IsNormalizedExtension(candidate.NormalizedExtension) ||
            candidate.FileSizeBytes is < 0)
        {
            return Decision(ProtectionEvaluationOutcome.Indeterminate,
                ProtectionCandidateReasonCodes.MetadataInvalid, identity, candidate, context);
        }

        if (policy.ExcludedExtensions.Contains(candidate.NormalizedExtension))
            return Decision(ProtectionEvaluationOutcome.Excluded,
                ProtectionCandidateReasonCodes.ExtensionExcluded, identity, candidate, context);

        if (!policy.IncludedExtensions.Contains(candidate.NormalizedExtension))
            return Decision(ProtectionEvaluationOutcome.Excluded,
                ProtectionCandidateReasonCodes.ExtensionNotIncluded, identity, candidate, context);

        if (policy.MaximumFileSizeBytes is { } maximum)
        {
            if (candidate.FileSizeBytes is null)
                return Decision(ProtectionEvaluationOutcome.Indeterminate,
                    ProtectionCandidateReasonCodes.MetadataUnavailable, identity, candidate, context);
            if (candidate.FileSizeBytes > maximum)
                return Decision(ProtectionEvaluationOutcome.Excluded,
                    ProtectionCandidateReasonCodes.FileTooLarge, identity, candidate, context);
        }

        return Decision(ProtectionEvaluationOutcome.Eligible,
            ProtectionCandidateReasonCodes.Eligible, identity, candidate, context);
    }

    private static bool IsAgeEnabled(
        EffectiveProtectionPolicy policy,
        ProtectionCandidateAge age) =>
        age switch
        {
            ProtectionCandidateAge.New => policy.ProtectNewFiles,
            ProtectionCandidateAge.Existing => policy.ProtectExistingFiles,
            _ => false
        };

    private static bool IsValidIdentity(
        EffectiveProtectionPolicy policy,
        PolicySnapshotIdentity identity) =>
        identity.PolicyId == policy.PolicyId &&
        identity.PolicyVersion == policy.PolicyVersion &&
        identity.ContentDigest is not null &&
        identity.ContentDigest.Length == 64 &&
        identity.ContentDigest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsNormalizedExtension(string? extension) =>
        extension is not null &&
        (extension.Length == 0 ||
        extension[0] == '.' &&
        string.Equals(extension, extension.ToLowerInvariant(), StringComparison.Ordinal));

    private static ProtectionCandidateDecision Decision(
        ProtectionEvaluationOutcome outcome,
        string reasonCode,
        PolicySnapshotIdentity identity,
        ProtectionCandidate candidate,
        ProtectionEvaluationContext context) =>
        new(
            outcome,
            reasonCode,
            identity,
            candidate.WorkspaceId,
            candidate.RelativePath ?? string.Empty,
            context.EvaluatedAtUtc.ToUniversalTime());
}
