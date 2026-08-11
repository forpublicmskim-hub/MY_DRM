namespace Drm.Domain;

public sealed record AuthenticationRequest(string UserId, ReadOnlyMemory<char> Secret);
public sealed record AuthenticatedPrincipal(string SubjectId, string AccessToken);
public sealed record ContentDescriptor(string ContentId, Uri Location);
public sealed record LicensePolicy(DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, bool OfflinePlaybackAllowed, TimeSpan? OfflineGracePeriod);
public readonly record struct LicenseHandle(Guid Value);
public readonly record struct KeyHandle(ulong Value);
public readonly record struct SessionHandle(ulong Value);
public sealed record VerifiedLicense(LicenseHandle Handle, string SubjectId, string ContentId, LicensePolicy Policy, string Issuer);

public sealed record PolicyDecision(bool IsAllowed, string Code, string Reason)
{
    public static PolicyDecision Allow() => new(true, "allowed", "Policy requirements are satisfied.");
    public static PolicyDecision Deny(string code, string reason) => new(false, code, reason);
}
