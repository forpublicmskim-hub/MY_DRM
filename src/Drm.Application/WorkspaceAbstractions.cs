using Drm.Domain;

namespace Drm.Application;

public interface IWorkspaceRegistry : IDisposable
{
    ValueTask<IReadOnlyList<ProtectedWorkspace>> GetAllAsync(CancellationToken cancellationToken);
    ValueTask AddAsync(ProtectedWorkspace workspace, CancellationToken cancellationToken);
    ValueTask<bool> RemoveAsync(WorkspaceId id, CancellationToken cancellationToken);
}

public class WorkspaceRegistryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class WorkspaceRegistryCorruptedException(string message, Exception? innerException = null)
    : WorkspaceRegistryException(message, innerException);

public sealed record WorkspaceRegistrationResult(
    bool IsSuccess,
    ProtectedWorkspace? Workspace,
    WorkspaceValidationResult Validation)
{
    public static WorkspaceRegistrationResult Success(ProtectedWorkspace workspace) =>
        new(true, workspace, WorkspaceValidationResult.Allowed());

    public static WorkspaceRegistrationResult Failure(WorkspaceValidationResult validation) =>
        new(false, null, validation);
}
