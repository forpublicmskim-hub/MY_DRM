using Drm.Domain;

namespace Drm.Application;

public sealed record OpenSessionRequest(AuthenticationRequest Authentication, ContentDescriptor Content, ConnectivityState Connectivity, string IdempotencyKey);
public sealed record OpenSessionResult(VerifiedLicense License, IProtectedContentSession ProtectedSession);

public sealed class DrmAccessDeniedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class OpenPipeline(IEnvironmentValidator environment, IAuthenticator authenticator, ILicenseProvider licenses,
    IPolicyEvaluator policies, IProtectedContentEngine engine)
{
    public async ValueTask<OpenSessionResult> ExecuteAsync(OpenSessionRequest request, CancellationToken cancellationToken)
    {
        await environment.ValidateAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatedPrincipal principal = await authenticator.AuthenticateAsync(request.Authentication, cancellationToken).ConfigureAwait(false);
        VerifiedLicense license = await licenses.AcquireAsync(principal, request.Content, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        PolicyDecision decision = await policies.EvaluateAsync(license, request.Connectivity, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed) throw new DrmAccessDeniedException(decision.Code, decision.Reason);
        IProtectedContentSession session = await engine.OpenAsync(license, request.Content, cancellationToken).ConfigureAwait(false);
        return new OpenSessionResult(license, session);
    }
}
