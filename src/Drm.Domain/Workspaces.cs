namespace Drm.Domain;

public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid());
}

public enum WorkspaceRegistrationState { Registered, Unavailable, ValidationFailed }
public enum WorkspaceProtectionState { NotActivated, Activating, Active, Degraded, Failed }

public sealed record WorkspaceLocation(
    string DisplayPath,
    string CanonicalPath,
    string? PlatformIdentity = null,
    string? PersistentAccessReference = null);

public sealed record ProtectedWorkspace(
    WorkspaceId Id,
    string DisplayName,
    WorkspaceLocation Location,
    WorkspaceRegistrationState RegistrationState,
    WorkspaceProtectionState ProtectionState,
    DateTimeOffset RegisteredAt);

public enum WorkspaceValidationCode
{
    Allowed,
    InvalidPath,
    DoesNotExist,
    NotDirectory,
    AccessDenied,
    FileSystemRootNotAllowed,
    SystemLocationNotAllowed,
    ApplicationLocationNotAllowed,
    TemporaryLocationNotAllowed,
    DuplicateWorkspace,
    ExistingWorkspaceContainsCandidate,
    CandidateContainsExistingWorkspace,
    SymbolicLinkNotSupported,
    NetworkLocationNotSupported,
    RemovableLocationNotSupported,
    CloudLocationNotSupported,
    UnsupportedFileSystem,
    PersistenceFailed,
    RegistryCorrupted
}

public sealed record WorkspaceValidationResult(bool IsAllowed, WorkspaceValidationCode Code, string UserMessage)
{
    public static WorkspaceValidationResult Allowed() => new(true, WorkspaceValidationCode.Allowed, "등록할 수 있습니다.");
    public static WorkspaceValidationResult Denied(WorkspaceValidationCode code, string message) => new(false, code, message);
}
