using System.Collections.Immutable;
using Drm.Application;
using Drm.Domain;
using Drm.Policy;

namespace Drm.Application.Tests;

public sealed class ProtectionCandidateEvaluatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public void IncludedNewFileWithinLimitIsEligible()
    {
        ProtectionCandidateDecision decision = Evaluate();

        Assert.Equal(ProtectionEvaluationOutcome.Eligible, decision.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.Eligible, decision.ReasonCode);
        Assert.Equal(Identity(), decision.Policy);
        Assert.Equal(Now, decision.EvaluatedAtUtc);
    }

    [Theory]
    [InlineData(false, null, null, ProtectionCandidateReasonCodes.PolicyDisabled)]
    [InlineData(true, "2026-08-19T00:00:00Z", null, ProtectionCandidateReasonCodes.PolicyNotYetValid)]
    [InlineData(true, null, "2026-08-18T12:00:00Z", ProtectionCandidateReasonCodes.PolicyExpired)]
    public void UnavailablePolicyIsNotReportedAsAFileExclusion(
        bool enabled,
        string? validFrom,
        string? validUntil,
        string expectedReason)
    {
        EffectiveProtectionPolicy policy = Policy(
            enabled: enabled,
            validFrom: Parse(validFrom),
            validUntil: Parse(validUntil));

        ProtectionCandidateDecision decision = Evaluate(policy: policy);

        Assert.Equal(ProtectionEvaluationOutcome.PolicyInactive, decision.Outcome);
        Assert.Equal(expectedReason, decision.ReasonCode);
    }

    [Fact]
    public void ValidUntilBoundaryIsExclusive()
    {
        ProtectionCandidateDecision decision = Evaluate(
            policy: Policy(validUntil: Now));

        Assert.Equal(ProtectionCandidateReasonCodes.PolicyExpired, decision.ReasonCode);
    }

    [Theory]
    [InlineData(true, ProtectionCandidateReasonCodes.Directory)]
    [InlineData(false, ProtectionCandidateReasonCodes.AgeDisabled)]
    public void CandidateKindAndDisabledAgeAreExcluded(bool directory, string expectedReason)
    {
        EffectiveProtectionPolicy policy = Policy(protectNew: directory);
        ProtectionCandidate candidate = Candidate(isDirectory: directory);

        ProtectionCandidateDecision decision = Evaluate(policy, candidate);

        Assert.Equal(ProtectionEvaluationOutcome.Excluded, decision.Outcome);
        Assert.Equal(expectedReason, decision.ReasonCode);
    }

    [Fact]
    public void ExistingAndNewAgeUseDifferentPolicyFlags()
    {
        Assert.Equal(ProtectionEvaluationOutcome.Eligible,
            Evaluate(Policy(protectNew: false, protectExisting: true),
                Candidate(age: ProtectionCandidateAge.Existing)).Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.AgeDisabled,
            Evaluate(Policy(protectNew: false, protectExisting: true),
                Candidate(age: ProtectionCandidateAge.New)).ReasonCode);
    }

    [Fact]
    public void ExclusionTakesPriorityOverInclusion()
    {
        EffectiveProtectionPolicy policy = Policy(
            included: [".pdf"],
            excluded: [".pdf"]);

        ProtectionCandidateDecision decision = Evaluate(policy: policy);

        Assert.Equal(ProtectionEvaluationOutcome.Excluded, decision.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.ExtensionExcluded, decision.ReasonCode);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData("")]
    public void ExtensionOutsideAListedOnlyPolicyIsExcluded(string extension)
    {
        ProtectionCandidateDecision decision = Evaluate(candidate: Candidate(extension: extension));

        Assert.Equal(ProtectionCandidateReasonCodes.ExtensionNotIncluded, decision.ReasonCode);
    }

    [Fact]
    public void EmptyIncludedSetSelectsNoFiles()
    {
        ProtectionCandidateDecision decision = Evaluate(policy: Policy(included: []));

        Assert.Equal(ProtectionEvaluationOutcome.Excluded, decision.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.ExtensionNotIncluded, decision.ReasonCode);
    }

    [Theory]
    [InlineData(".PDF")]
    [InlineData("pdf")]
    [InlineData(null)]
    public void NonNormalizedExtensionIsIndeterminate(string? extension)
    {
        ProtectionCandidateDecision decision = Evaluate(
            candidate: Candidate(extension: extension!));

        Assert.Equal(ProtectionEvaluationOutcome.Indeterminate, decision.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.MetadataInvalid, decision.ReasonCode);
    }

    [Fact]
    public void MaximumSizeBoundaryIsInclusive()
    {
        Assert.Equal(ProtectionEvaluationOutcome.Eligible,
            Evaluate(candidate: Candidate(size: 10_000), policy: Policy(maximum: 10_000)).Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.FileTooLarge,
            Evaluate(candidate: Candidate(size: 10_001), policy: Policy(maximum: 10_000)).ReasonCode);
    }

    [Fact]
    public void MissingSizeIsRequiredOnlyWhenPolicyHasAMaximum()
    {
        Assert.Equal(ProtectionEvaluationOutcome.Eligible,
            Evaluate(candidate: Candidate(size: null), policy: Policy(maximum: null)).Outcome);

        ProtectionCandidateDecision limited =
            Evaluate(candidate: Candidate(size: null), policy: Policy(maximum: 10_000));
        Assert.Equal(ProtectionEvaluationOutcome.Indeterminate, limited.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.MetadataUnavailable, limited.ReasonCode);
    }

    [Fact]
    public void InvalidIdentityPathOrNegativeSizeIsIndeterminate()
    {
        Assert.Equal(ProtectionCandidateReasonCodes.MetadataInvalid,
            Evaluate(candidate: Candidate(workspaceId: new WorkspaceId(Guid.Empty))).ReasonCode);
        Assert.Equal(ProtectionCandidateReasonCodes.MetadataInvalid,
            Evaluate(candidate: Candidate(relativePath: " ")).ReasonCode);
        Assert.Equal(string.Empty,
            Evaluate(candidate: Candidate(relativePath: null!)).RelativePath);
        Assert.Equal(ProtectionCandidateReasonCodes.MetadataInvalid,
            Evaluate(candidate: Candidate(size: -1)).ReasonCode);
    }

    [Fact]
    public void UnsignedInspectedPolicyCannotBeUsedForEnforcement()
    {
        ProtectionCandidate candidate = Candidate();
        InspectedProtectionPolicy policy = Inspected(Policy());
        ProtectionEvaluationContext context = new(Now, PolicyUsageMode.Enforcement);

        ProtectionCandidateDecision decision = ProtectionCandidateEvaluator.Evaluate(policy, candidate, context);

        Assert.Equal(ProtectionEvaluationOutcome.PolicyInactive, decision.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.PolicyNotEnforceable, decision.ReasonCode);
    }

    [Fact]
    public void MismatchedOrMalformedPolicyIdentityFailsClosed()
    {
        EffectiveProtectionPolicy effective = Policy();
        InspectedProtectionPolicy mismatched = Inspected(effective) with
        {
            Identity = Identity() with { PolicyVersion = 4 }
        };
        InspectedProtectionPolicy malformed = Inspected(effective) with
        {
            Identity = Identity() with { ContentDigest = "not-a-sha256-digest" }
        };
        ProtectionEvaluationContext context = new(Now, PolicyUsageMode.Inspection);

        ProtectionCandidateDecision mismatchDecision =
            ProtectionCandidateEvaluator.Evaluate(mismatched, Candidate(), context);
        ProtectionCandidateDecision malformedDecision =
            ProtectionCandidateEvaluator.Evaluate(malformed, Candidate(), context);

        Assert.Equal(ProtectionEvaluationOutcome.PolicyInactive, mismatchDecision.Outcome);
        Assert.Equal(ProtectionCandidateReasonCodes.PolicyIdentityInvalid, mismatchDecision.ReasonCode);
        Assert.Equal(ProtectionCandidateReasonCodes.PolicyIdentityInvalid, malformedDecision.ReasonCode);
    }

    [Fact]
    public void EvaluationDoesNotMutateInputSnapshots()
    {
        ProtectionCandidate candidate = Candidate();
        InspectedProtectionPolicy inspected = Inspected(Policy());

        _ = ProtectionCandidateEvaluator.Evaluate(
            inspected,
            candidate,
            new ProtectionEvaluationContext(Now, PolicyUsageMode.Inspection));

        Assert.Equal(".pdf", candidate.NormalizedExtension);
        Assert.Single(inspected.Policy.IncludedExtensions);
        Assert.Equal(new string('a', 64), inspected.Identity.ContentDigest);
    }

    private static ProtectionCandidateDecision Evaluate(
        EffectiveProtectionPolicy? policy = null,
        ProtectionCandidate? candidate = null) =>
        ProtectionCandidateEvaluator.Evaluate(
            Inspected(policy ?? Policy()),
            candidate ?? Candidate(),
            new ProtectionEvaluationContext(Now, PolicyUsageMode.Inspection));

    private static InspectedProtectionPolicy Inspected(EffectiveProtectionPolicy policy) =>
        new(policy, Identity(), "policy.json", Now,
            ProtectionPolicyTrustState.UnsignedDevelopmentDraft);

    private static PolicySnapshotIdentity Identity() => new(
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 3, new string('a', 64));

    private static EffectiveProtectionPolicy Policy(
        bool enabled = true,
        bool protectNew = true,
        bool protectExisting = false,
        IEnumerable<string>? included = null,
        IEnumerable<string>? excluded = null,
        long? maximum = 10_000,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null) =>
        new(
            Identity().PolicyId,
            Identity().PolicyVersion,
            "Policy",
            enabled,
            protectNew,
            protectExisting,
            (included ?? [".pdf"]).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            (excluded ?? [".tmp", ".drm"]).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            maximum,
            validFrom,
            validUntil);

    private static ProtectionCandidate Candidate(
        WorkspaceId? workspaceId = null,
        string relativePath = "documents/report.pdf",
        string extension = ".pdf",
        ProtectionCandidateAge age = ProtectionCandidateAge.New,
        bool isDirectory = false,
        long? size = 1_000) =>
        new(
            workspaceId ?? new WorkspaceId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
            relativePath,
            extension,
            age,
            ProtectionDiscoveryKind.Created,
            isDirectory,
            size);

    private static DateTimeOffset? Parse(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
