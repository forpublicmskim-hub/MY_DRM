using Drm.Domain;

namespace Drm.Application;

public interface IEnvironmentValidator { ValueTask ValidateAsync(CancellationToken cancellationToken); }
public interface IAuthenticator { ValueTask<AuthenticatedPrincipal> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken); }
public interface ILicenseProvider
{
    ValueTask<VerifiedLicense> AcquireAsync(AuthenticatedPrincipal principal, ContentDescriptor content, string idempotencyKey, CancellationToken cancellationToken);
}
public interface IPolicyEvaluator
{
    ValueTask<PolicyDecision> EvaluateAsync(VerifiedLicense license, ConnectivityState connectivity, CancellationToken cancellationToken);
}
public interface IProtectedContentSession : IAsyncDisposable { SessionHandle Handle { get; } }
public interface IProtectedContentEngine
{
    ValueTask<IProtectedContentSession> OpenAsync(VerifiedLicense license, ContentDescriptor content, CancellationToken cancellationToken);
}
public interface IAuditSink { ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken); }
public sealed record AuditEvent(Guid SessionId, string Name, DateTimeOffset OccurredAt, string? Detail = null);
public interface IClock { DateTimeOffset UtcNow { get; } }
