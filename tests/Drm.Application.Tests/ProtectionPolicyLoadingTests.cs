using Drm.Application;
using Drm.Policy;

namespace Drm.Application.Tests;

public sealed class ProtectionPolicyLoadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly System.Text.Json.JsonSerializerOptions UncheckedJsonOptions = CreateUncheckedJsonOptions();

    [Fact]
    public async Task ValidDevelopmentDraftCompilesToImmutableSnapshot()
    {
        ProtectionPolicyDraft draft = ValidDraft();
        string json = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(draft));
        ProtectionPolicyLoader loader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(json), PolicyTrustOptions.Development);

        ProtectionPolicyLoadResult result = await loader.LoadAsync("policy.json");

        Assert.True(result.IsLoaded);
        Assert.Equal(draft.PolicyId, result.Snapshot!.Policy.PolicyId);
        Assert.Contains(".pdf", result.Snapshot.Policy.IncludedExtensions);
        Assert.Equal(Now, result.Snapshot.LoadedAtUtc);
        Assert.Equal(ProtectionPolicyTrustState.UnsignedDevelopmentDraft, result.Snapshot.TrustState);
        Assert.Equal(draft.PolicyId, result.Snapshot.Identity.PolicyId);
        Assert.Equal(draft.PolicyVersion, result.Snapshot.Identity.PolicyVersion);
        Assert.Matches("^[0-9a-f]{64}$", result.Snapshot.Identity.ContentDigest);
    }

    [Fact]
    public async Task CanonicalPolicyDigestIsStableAndDistinguishesChangedPayload()
    {
        ProtectionPolicyDraft firstDraft = ValidDraft();
        ProtectionPolicyDocument firstDocument = PolicyNormalizer.Normalize(firstDraft);
        string firstJson = ProtectionPolicySerializer.Serialize(firstDocument);
        ProtectionPolicyLoader firstLoader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(firstJson), PolicyTrustOptions.Development);
        ProtectionPolicyLoader equivalentLoader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(firstJson.ReplaceLineEndings("\r\n")),
            PolicyTrustOptions.Development);

        ProtectionPolicyDraft changedDraft = ValidDraft();
        changedDraft.PolicyId = firstDraft.PolicyId;
        changedDraft.PolicyVersion = firstDraft.PolicyVersion;
        changedDraft.IncludedExtensions.Add(".docx");
        ProtectionPolicyLoader changedLoader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(
                ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(changedDraft))),
            PolicyTrustOptions.Development);

        ProtectionPolicyLoadResult first = await firstLoader.LoadAsync("first.json");
        ProtectionPolicyLoadResult equivalent = await equivalentLoader.LoadAsync("equivalent.json");
        ProtectionPolicyLoadResult changed = await changedLoader.LoadAsync("changed.json");

        Assert.Equal(first.Snapshot!.Identity.ContentDigest, equivalent.Snapshot!.Identity.ContentDigest);
        Assert.NotEqual(first.Snapshot.Identity.ContentDigest, changed.Snapshot!.Identity.ContentDigest);
    }

    [Fact]
    public void EnforceablePolicyCannotBeCreatedByAnUntrustedCaller()
    {
        Assert.Empty(typeof(EnforceableProtectionPolicy).GetConstructors());
        Assert.Empty(typeof(VerifiedPolicyIdentity).GetConstructors());
    }

    [Fact]
    public async Task ProductionModeRejectsUnsignedDraft()
    {
        string json = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(ValidDraft()));
        ProtectionPolicyLoader loader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(json), PolicyTrustOptions.Production);

        ProtectionPolicyLoadResult result = await loader.LoadAsync("policy.json");

        Assert.Equal(ProtectionPolicyLoadStatus.Untrusted, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task UnsupportedSchemaIsDistinctFromInvalidDocument()
    {
        ProtectionPolicyDocument document = PolicyNormalizer.Normalize(ValidDraft()) with
        {
            SchemaVersion = 99
        };
        string json = SerializeUnchecked(document);
        ProtectionPolicyLoader loader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(json), PolicyTrustOptions.Development);

        ProtectionPolicyLoadResult result = await loader.LoadAsync("policy.json");

        Assert.Equal(ProtectionPolicyLoadStatus.Unsupported, result.Status);
        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.InvalidSchemaVersion);
    }

    [Fact]
    public async Task MissingCapabilityIsInvalidRatherThanUnsupported()
    {
        ProtectionPolicyDocument document = PolicyNormalizer.Normalize(ValidDraft()) with
        {
            RequiredCapabilities = Array.Empty<string>()
        };
        string json = SerializeUnchecked(document);
        ProtectionPolicyLoader loader = CreateLoader(
            ProtectionPolicySourceReadResult.Success(json), PolicyTrustOptions.Development);

        ProtectionPolicyLoadResult result = await loader.LoadAsync("policy.json");

        Assert.Equal(ProtectionPolicyLoadStatus.InvalidDocument, result.Status);
        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.MissingCapability);
    }

    [Fact]
    public async Task FailedLoadDoesNotReplaceCurrentSnapshot()
    {
        string json = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(ValidDraft()));
        QueueSource source = new(
            ProtectionPolicySourceReadResult.Success(json),
            new ProtectionPolicySourceReadResult(PolicySourceReadStatus.NotFound));
        using ProtectionPolicyInspectionService service = new(
            new ProtectionPolicyLoader(source, new FixedClock(), PolicyTrustOptions.Development));

        ProtectionPolicyLoadResult first = await service.LoadAsync("first.json");
        ProtectionPolicyLoadResult second = await service.LoadAsync("missing.json");

        Assert.True(first.IsLoaded);
        Assert.Equal(ProtectionPolicyLoadStatus.NotFound, second.Status);
        Assert.Same(first.Snapshot, service.Current);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ProtectionPolicyLoader loader = new(new CancellingSource(), new FixedClock(), PolicyTrustOptions.Development);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => loader.LoadAsync("policy.json", cancellation.Token).AsTask());
    }

    private static ProtectionPolicyDraft ValidDraft()
    {
        ProtectionPolicyDraft draft = new() { DisplayName = "Policy" };
        draft.IncludedExtensions.Add(".pdf");
        return draft;
    }

    private static ProtectionPolicyLoader CreateLoader(
        ProtectionPolicySourceReadResult result,
        PolicyTrustOptions options) =>
        new(new QueueSource(result), new FixedClock(), options);

    private static string SerializeUnchecked(ProtectionPolicyDocument document) =>
        System.Text.Json.JsonSerializer.Serialize(document, UncheckedJsonOptions);

    private static System.Text.Json.JsonSerializerOptions CreateUncheckedJsonOptions()
    {
        System.Text.Json.JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class QueueSource(params ProtectionPolicySourceReadResult[] results) : IProtectionPolicySource
    {
        private readonly Queue<ProtectionPolicySourceReadResult> _results = new(results);

        public ValueTask<ProtectionPolicySourceReadResult> ReadAsync(
            string location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class CancellingSource : IProtectionPolicySource
    {
        public ValueTask<ProtectionPolicySourceReadResult> ReadAsync(
            string location,
            CancellationToken cancellationToken) =>
            ValueTask.FromCanceled<ProtectionPolicySourceReadResult>(cancellationToken);
    }
}
