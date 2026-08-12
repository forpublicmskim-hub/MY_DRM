using Drm.Domain;
using Drm.Platform.Abstractions;

namespace Drm.Application;

public sealed class WorkspaceRegistrationPolicy(IWorkspaceLocationResolver locations)
{
    public WorkspaceValidationResult Evaluate(WorkspaceLocation candidate, IEnumerable<ProtectedWorkspace> existing)
    {
        foreach (ProtectedWorkspace workspace in existing)
        {
            if (locations.AreSame(candidate, workspace.Location))
                return WorkspaceValidationResult.Denied(WorkspaceValidationCode.DuplicateWorkspace);

            if (locations.IsAncestorOf(workspace.Location, candidate))
                return WorkspaceValidationResult.Denied(WorkspaceValidationCode.ExistingWorkspaceContainsCandidate);

            if (locations.IsAncestorOf(candidate, workspace.Location))
                return WorkspaceValidationResult.Denied(WorkspaceValidationCode.CandidateContainsExistingWorkspace);
        }

        return WorkspaceValidationResult.Allowed();
    }
}
