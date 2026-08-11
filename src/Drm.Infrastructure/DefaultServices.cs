using Drm.Application;
using Drm.Domain;

namespace Drm.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class DenyByDefaultPolicyEvaluator(IClock clock) : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(VerifiedLicense license, ConnectivityState connectivity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = clock.UtcNow;
        PolicyDecision result = now < license.Policy.NotBefore
            ? PolicyDecision.Deny("license.not-yet-valid", "The license is not valid yet.")
            : now >= license.Policy.ExpiresAt
                ? PolicyDecision.Deny("license.expired", "The license has expired.")
                : connectivity == ConnectivityState.Offline && !license.Policy.OfflinePlaybackAllowed
                    ? PolicyDecision.Deny("connectivity.offline-denied", "Offline playback is not allowed.")
                    : PolicyDecision.Allow();
        return ValueTask.FromResult(result);
    }
}

public sealed class NullAuditSink : IAuditSink
{
    public ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
