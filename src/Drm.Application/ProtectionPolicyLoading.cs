using Drm.Policy;

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

public sealed record ProtectionPolicySnapshot(
    EffectiveProtectionPolicy Policy,
    string SourceLocation,
    DateTimeOffset LoadedAtUtc,
    ProtectionPolicyTrustState TrustState);

public sealed record ProtectionPolicyLoadResult(
    ProtectionPolicyLoadStatus Status,
    ProtectionPolicySnapshot? Snapshot,
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
            EffectiveProtectionPolicy policy = PolicyNormalizer.Compile(loaded.Document!);
            ProtectionPolicySnapshot snapshot = new(
                policy, location, clock.UtcNow, ProtectionPolicyTrustState.UnsignedDevelopmentDraft);
            return new ProtectionPolicyLoadResult(
                ProtectionPolicyLoadStatus.Loaded, snapshot, Array.Empty<PolicyValidationError>());
        }
        catch (InvalidPolicyException exception)
        {
            return ValidationFailure(exception.Validation.Errors);
        }
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

public sealed class ProtectionPolicyInspectionService(ProtectionPolicyLoader loader) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public ProtectionPolicySnapshot? Current { get; private set; }

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
                Current = result.Snapshot;
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
