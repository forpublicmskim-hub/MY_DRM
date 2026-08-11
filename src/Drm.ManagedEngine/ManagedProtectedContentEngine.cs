using Drm.Application;
using Drm.Domain;

namespace Drm.ManagedEngine;

// Development seam only. Production replaces this with a native adapter that never returns key bytes.
public sealed class ManagedProtectedContentEngine : IProtectedContentEngine
{
    private long _nextHandle;

    public ValueTask<IProtectedContentSession> OpenAsync(VerifiedLicense license, ContentDescriptor content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(license.ContentId, content.ContentId))
            throw new InvalidOperationException("License/content mismatch.");

        ulong value = checked((ulong)Interlocked.Increment(ref _nextHandle));
        return ValueTask.FromResult<IProtectedContentSession>(new ManagedSession(new SessionHandle(value)));
    }

    private sealed class ManagedSession(SessionHandle handle) : IProtectedContentSession
    {
        public SessionHandle Handle { get; } = handle;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
