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
                return WorkspaceValidationResult.Denied(WorkspaceValidationCode.DuplicateWorkspace, "이미 등록된 폴더입니다.");

            if (locations.IsAncestorOf(workspace.Location, candidate))
                return WorkspaceValidationResult.Denied(WorkspaceValidationCode.ExistingWorkspaceContainsCandidate,
                    "이미 등록된 작업공간의 하위 폴더는 등록할 수 없습니다.");

            if (locations.IsAncestorOf(candidate, workspace.Location))
                return WorkspaceValidationResult.Denied(WorkspaceValidationCode.CandidateContainsExistingWorkspace,
                    "기존 작업공간을 포함하는 상위 폴더는 등록할 수 없습니다.");
        }

        return WorkspaceValidationResult.Allowed();
    }
}
