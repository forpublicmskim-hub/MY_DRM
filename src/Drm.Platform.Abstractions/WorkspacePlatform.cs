using Drm.Domain;

namespace Drm.Platform.Abstractions;

public sealed record WorkspaceLocationResolution(WorkspaceLocation? Location, WorkspaceValidationResult Validation);

public interface IWorkspaceLocationResolver
{
    ValueTask<WorkspaceLocationResolution> ResolveAsync(string path, CancellationToken cancellationToken);
    bool AreSame(WorkspaceLocation first, WorkspaceLocation second);
    bool IsAncestorOf(WorkspaceLocation parent, WorkspaceLocation child);
}

public interface IWorkspacePathLauncher
{
    ValueTask OpenAsync(WorkspaceLocation location, CancellationToken cancellationToken);
}
