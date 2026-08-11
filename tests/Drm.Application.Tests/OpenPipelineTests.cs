using Drm.Application;
using Drm.Domain;

namespace Drm.Application.Tests;

public sealed class OpenPipelineTests
{
    [Fact]
    public async Task DeniedPolicyNeverActivatesContent()
    {
        EngineSpy engine = new();
        OpenPipeline pipeline = new(new EnvironmentStub(), new AuthenticatorStub(), new LicenseStub(), new DenyPolicy(), engine);
        OpenSessionRequest request = new(new AuthenticationRequest("user", "secret".AsMemory()),
            new ContentDescriptor("content", new Uri("file:///content")), ConnectivityState.Online, "request-1");

        await Assert.ThrowsAsync<DrmAccessDeniedException>(async () => await pipeline.ExecuteAsync(request, default));

        Assert.False(engine.WasCalled);
    }

    private sealed class EnvironmentStub : IEnvironmentValidator
    {
        public ValueTask ValidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class AuthenticatorStub : IAuthenticator
    {
        public ValueTask<AuthenticatedPrincipal> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AuthenticatedPrincipal(request.UserId, "token"));
    }

    private sealed class LicenseStub : ILicenseProvider
    {
        public ValueTask<VerifiedLicense> AcquireAsync(AuthenticatedPrincipal principal, ContentDescriptor content,
            string idempotencyKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new VerifiedLicense(new LicenseHandle(Guid.NewGuid()), principal.SubjectId,
                content.ContentId, new LicensePolicy(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, false, null), "test"));
    }

    private sealed class DenyPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(VerifiedLicense license, ConnectivityState connectivity,
            CancellationToken cancellationToken) => ValueTask.FromResult(PolicyDecision.Deny("denied", "Denied for test."));
    }

    private sealed class EngineSpy : IProtectedContentEngine
    {
        public bool WasCalled { get; private set; }
        public ValueTask<IProtectedContentSession> OpenAsync(VerifiedLicense license, ContentDescriptor content,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Engine must not be called.");
        }
    }
}
