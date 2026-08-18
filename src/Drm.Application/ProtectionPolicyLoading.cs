using Drm.Policy;
using System.Security.Cryptography;
using System.Text;

namespace Drm.Application;

public enum PolicySourceReadStatus
{
    Read,
    NotFound,
    AccessDenied,
    TooLarge,
    InvalidEncoding,
    Unavailable
}

public sealed record ProtectionPolicySourceReadResult(
    PolicySourceReadStatus Status,
    string? Content = null)
{
    public static ProtectionPolicySourceReadResult Success(string content) =>
        new(PolicySourceReadStatus.Read, content);
}

public interface IProtectionPolicySource
{
    ValueTask<ProtectionPolicySourceReadResult> ReadAsync(
        string location,
        CancellationToken cancellationToken);
}

public enum ProtectionPolicyLoadStatus
{
    Loaded,
    NotFound,
    AccessDenied,
    InvalidDocument,
    Unsupported,
    TooLarge,
    Untrusted,
    Unavailable
}

public enum ProtectionPolicyTrustState
{
    UnsignedDevelopmentDraft
}

public sealed record PolicyTrustOptions(bool AllowUnsignedDevelopmentPolicies)
{
    public static PolicyTrustOptions Production { get; } = new(false);
    public static PolicyTrustOptions Development { get; } = new(true);
}

public sealed record PolicySnapshotIdentity(
    Guid PolicyId,
    int PolicyVersion,
    string ContentDigest);

public sealed record InspectedProtectionPolicy(
    EffectiveProtectionPolicy Policy,
    PolicySnapshotIdentity Identity,
    string SourceLocation,
    DateTimeOffset LoadedAtUtc,
    ProtectionPolicyTrustState TrustState);

public sealed record VerifiedPolicyIdentity
{
    internal VerifiedPolicyIdentity(PolicySnapshotIdentity snapshot, string issuer)
    {
        Snapshot = snapshot;
        Issuer = issuer;
    }

    public PolicySnapshotIdentity Snapshot { get; }
    public string Issuer { get; }
}

public sealed record EnforceableProtectionPolicy
{
    internal EnforceableProtectionPolicy(
        EffectiveProtectionPolicy policy,
        VerifiedPolicyIdentity identity)
    {
        Policy = policy;
        Identity = identity;
    }

    public EffectiveProtectionPolicy Policy { get; }
    public VerifiedPolicyIdentity Identity { get; }
}

public sealed record ProtectionPolicyLoadResult(
    ProtectionPolicyLoadStatus Status,
    InspectedProtectionPolicy? Snapshot,
    IReadOnlyList<PolicyValidationError> Errors)
{
    public bool IsLoaded => Status == ProtectionPolicyLoadStatus.Loaded && Snapshot is not null;
}

public sealed class ProtectionPolicyLoader(
    IProtectionPolicySource source,
    IClock clock,
    PolicyTrustOptions trustOptions)
{
    public async ValueTask<ProtectionPolicyLoadResult> LoadAsync(
        string location,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        cancellationToken.ThrowIfCancellationRequested();

        ProtectionPolicySourceReadResult sourceResult =
            await source.ReadAsync(location, cancellationToken).ConfigureAwait(false);
        if (sourceResult.Status != PolicySourceReadStatus.Read)
            return SourceFailure(sourceResult.Status);
        if (sourceResult.Content is null)
            return new ProtectionPolicyLoadResult(
                ProtectionPolicyLoadStatus.Unavailable, null, Array.Empty<PolicyValidationError>());

        PolicyLoadResult loaded = ProtectionPolicySerializer.Deserialize(sourceResult.Content);
        if (!loaded.IsSuccess)
            return ValidationFailure(loaded.Validation.Errors);

        if (!trustOptions.AllowUnsignedDevelopmentPolicies)
            return new ProtectionPolicyLoadResult(
                ProtectionPolicyLoadStatus.Untrusted, null, Array.Empty<PolicyValidationError>());

        try
        {
            ProtectionPolicyDocument document = loaded.Document!;
            EffectiveProtectionPolicy policy = PolicyNormalizer.Compile(document);
            PolicySnapshotIdentity identity = CreateIdentity(document);
            InspectedProtectionPolicy snapshot = new(
                policy, identity, location, clock.UtcNow, ProtectionPolicyTrustState.UnsignedDevelopmentDraft);
            return new ProtectionPolicyLoadResult(
                ProtectionPolicyLoadStatus.Loaded, snapshot, Array.Empty<PolicyValidationError>());
        }
        catch (InvalidPolicyException exception)
        {
            return ValidationFailure(exception.Validation.Errors);
        }
    }

    private static PolicySnapshotIdentity CreateIdentity(ProtectionPolicyDocument document)
    {
        string canonicalPayload = ProtectionPolicySerializer.Serialize(document);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
        return new PolicySnapshotIdentity(
            document.PolicyId,
            document.PolicyVersion,
            Convert.ToHexStringLower(digest));
    }

    private static ProtectionPolicyLoadResult SourceFailure(PolicySourceReadStatus status) => new(
        status switch
        {
            PolicySourceReadStatus.NotFound => ProtectionPolicyLoadStatus.NotFound,
            PolicySourceReadStatus.AccessDenied => ProtectionPolicyLoadStatus.AccessDenied,
            PolicySourceReadStatus.TooLarge => ProtectionPolicyLoadStatus.TooLarge,
            PolicySourceReadStatus.InvalidEncoding => ProtectionPolicyLoadStatus.InvalidDocument,
            _ => ProtectionPolicyLoadStatus.Unavailable
        }, null, Array.Empty<PolicyValidationError>());

    private static ProtectionPolicyLoadResult ValidationFailure(IReadOnlyList<PolicyValidationError> errors) => new(
        errors.Any(error => error.Code is PolicyValidationCodes.InvalidSchemaVersion
            or PolicyValidationCodes.UnsupportedCapability)
            ? ProtectionPolicyLoadStatus.Unsupported
            : errors.Any(error => error.Code == PolicyValidationCodes.DocumentTooLarge)
                ? ProtectionPolicyLoadStatus.TooLarge
                : ProtectionPolicyLoadStatus.InvalidDocument,
        null,
        errors);
}

public interface ICurrentProtectionPolicyProvider
{
    InspectedProtectionPolicy? Current { get; }
}

public sealed class ProtectionPolicyInspectionService(ProtectionPolicyLoader loader)
    : ICurrentProtectionPolicyProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InspectedProtectionPolicy? _current;
    private bool _disposed;

    public InspectedProtectionPolicy? Current => Volatile.Read(ref _current);

    public async ValueTask<ProtectionPolicyLoadResult> LoadAsync(
        string location,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProtectionPolicyLoadResult result = await loader.LoadAsync(location, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsLoaded)
                Volatile.Write(ref _current, result.Snapshot);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
