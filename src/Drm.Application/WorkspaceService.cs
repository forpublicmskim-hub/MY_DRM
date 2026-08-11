using Drm.Domain;
using Drm.Platform.Abstractions;

namespace Drm.Application;

public sealed class WorkspaceService(
    IWorkspaceRegistry registry,
    IWorkspaceLocationResolver locations,
    WorkspaceRegistrationPolicy policy,
    IClock clock) : IDisposable
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public async ValueTask<IReadOnlyList<ProtectedWorkspace>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProtectedWorkspace> persisted = await registry.GetAllAsync(cancellationToken).ConfigureAwait(false);
        List<ProtectedWorkspace> result = new(persisted.Count);
        foreach (ProtectedWorkspace workspace in persisted)
        {
            WorkspaceLocationResolution resolution = await locations
                .ResolveAsync(workspace.Location.DisplayPath, cancellationToken).ConfigureAwait(false);
            WorkspaceRegistrationState state = resolution.Validation.IsAllowed
                ? WorkspaceRegistrationState.Registered
                : WorkspaceRegistrationState.Unavailable;
            result.Add(workspace with { RegistrationState = state });
        }

        return result;
    }

    public async ValueTask<WorkspaceRegistrationResult> RegisterAsync(string path, CancellationToken cancellationToken = default)
    {
        WorkspaceLocationResolution resolution = await locations.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        if (!resolution.Validation.IsAllowed || resolution.Location is null)
            return WorkspaceRegistrationResult.Failure(resolution.Validation);

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                IReadOnlyList<ProtectedWorkspace> existing = await registry.GetAllAsync(cancellationToken).ConfigureAwait(false);
                WorkspaceValidationResult policyResult = policy.Evaluate(resolution.Location, existing);
                if (!policyResult.IsAllowed) return WorkspaceRegistrationResult.Failure(policyResult);

                string displayName = Path.GetFileName(resolution.Location.DisplayPath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(displayName)) displayName = resolution.Location.DisplayPath;

                ProtectedWorkspace workspace = new(
                    WorkspaceId.New(), displayName, resolution.Location,
                    WorkspaceRegistrationState.Registered, WorkspaceProtectionState.NotActivated, clock.UtcNow);

                await registry.AddAsync(workspace, cancellationToken).ConfigureAwait(false);
                return WorkspaceRegistrationResult.Success(workspace);
            }
            catch (WorkspaceRegistryCorruptedException)
            {
                return WorkspaceRegistrationResult.Failure(WorkspaceValidationResult.Denied(
                    WorkspaceValidationCode.RegistryCorrupted, "작업공간 설정이 손상되어 등록할 수 없습니다."));
            }
            catch (WorkspaceRegistryException)
            {
                return WorkspaceRegistrationResult.Failure(WorkspaceValidationResult.Denied(
                    WorkspaceValidationCode.PersistenceFailed, "작업공간 설정을 저장하지 못했습니다. 다시 시도해 주세요."));
            }
        }
        finally { _mutationGate.Release(); }
    }

    public async ValueTask<bool> UnregisterAsync(WorkspaceId id, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await registry.RemoveAsync(id, cancellationToken).ConfigureAwait(false); }
        finally { _mutationGate.Release(); }
    }

    public void Dispose()
    {
        _mutationGate.Dispose();
        registry.Dispose();
    }
}
