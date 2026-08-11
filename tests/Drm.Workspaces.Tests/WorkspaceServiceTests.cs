using Drm.Application;
using Drm.Domain;
using Drm.Platform.Abstractions;

namespace Drm.Workspaces.Tests;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task RegistersWorkspaceWithoutActivatingProtection()
    {
        InMemoryRegistry registry = new();
        using WorkspaceService service = CreateService(registry);

        WorkspaceRegistrationResult result = await service.RegisterAsync(Path.Combine(Path.GetTempPath(), "workspace-a"));

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkspaceRegistrationState.Registered, result.Workspace!.RegistrationState);
        Assert.Equal(WorkspaceProtectionState.NotActivated, result.Workspace.ProtectionState);
        Assert.Single(registry.Items);
    }

    [Fact]
    public async Task RejectsDuplicateCanonicalPath()
    {
        InMemoryRegistry registry = new();
        using WorkspaceService service = CreateService(registry, StringComparison.OrdinalIgnoreCase);
        await service.RegisterAsync(Path.Combine(Path.GetTempPath(), "Workspace-A"));

        WorkspaceRegistrationResult result = await service.RegisterAsync(Path.Combine(Path.GetTempPath(), "workspace-a"));

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceValidationCode.DuplicateWorkspace, result.Validation.Code);
        Assert.Single(registry.Items);
    }

    [Theory]
    [InlineData("existing", "existing/child", WorkspaceValidationCode.ExistingWorkspaceContainsCandidate)]
    [InlineData("existing/child", "existing", WorkspaceValidationCode.CandidateContainsExistingWorkspace)]
    public async Task RejectsOverlappingWorkspace(string first, string second, WorkspaceValidationCode expected)
    {
        InMemoryRegistry registry = new();
        using WorkspaceService service = CreateService(registry);
        string root = Path.Combine(Path.GetTempPath(), "overlap-root");
        await service.RegisterAsync(Path.Combine(root, first.Replace('/', Path.DirectorySeparatorChar)));

        WorkspaceRegistrationResult result = await service.RegisterAsync(
            Path.Combine(root, second.Replace('/', Path.DirectorySeparatorChar)));

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Validation.Code);
    }

    [Theory]
    [InlineData(WorkspaceValidationCode.DoesNotExist)]
    [InlineData(WorkspaceValidationCode.AccessDenied)]
    [InlineData(WorkspaceValidationCode.FileSystemRootNotAllowed)]
    public async Task ReturnsStructuredLocationFailure(WorkspaceValidationCode code)
    {
        InMemoryRegistry registry = new();
        using WorkspaceService service = CreateService(registry, failureCode: code);

        WorkspaceRegistrationResult result = await service.RegisterAsync("not-disclosed");

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Validation.Code);
        Assert.Empty(registry.Items);
    }

    [Fact]
    public async Task PersistenceFailureDoesNotPublishWorkspace()
    {
        InMemoryRegistry registry = new() { FailAdd = true };
        using WorkspaceService service = CreateService(registry);

        WorkspaceRegistrationResult result = await service.RegisterAsync(Path.Combine(Path.GetTempPath(), "workspace-a"));

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkspaceValidationCode.PersistenceFailed, result.Validation.Code);
        Assert.Empty(registry.Items);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        InMemoryRegistry registry = new();
        using WorkspaceService service = CreateService(registry);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.RegisterAsync("cancelled", cancellation.Token));
    }

    private static WorkspaceService CreateService(InMemoryRegistry registry,
        StringComparison comparison = StringComparison.Ordinal, WorkspaceValidationCode? failureCode = null)
    {
        FakeResolver resolver = new(comparison, failureCode);
        return new WorkspaceService(registry, resolver, new WorkspaceRegistrationPolicy(resolver), new FixedClock());
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeResolver(StringComparison comparison, WorkspaceValidationCode? failureCode) : IWorkspaceLocationResolver
    {
        public ValueTask<WorkspaceLocationResolution> ResolveAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failureCode is { } code)
                return ValueTask.FromResult(new WorkspaceLocationResolution(null,
                    WorkspaceValidationResult.Denied(code, "등록할 수 없습니다.")));
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return ValueTask.FromResult(new WorkspaceLocationResolution(
                new WorkspaceLocation(canonical, canonical), WorkspaceValidationResult.Allowed()));
        }

        public bool AreSame(WorkspaceLocation first, WorkspaceLocation second) =>
            string.Equals(first.CanonicalPath, second.CanonicalPath, comparison);

        public bool IsAncestorOf(WorkspaceLocation parent, WorkspaceLocation child)
        {
            if (AreSame(parent, child)) return false;
            string prefix = parent.CanonicalPath + Path.DirectorySeparatorChar;
            return child.CanonicalPath.StartsWith(prefix, comparison);
        }
    }

    private sealed class InMemoryRegistry : IWorkspaceRegistry
    {
        public List<ProtectedWorkspace> Items { get; } = [];
        public bool FailAdd { get; init; }
        public ValueTask<IReadOnlyList<ProtectedWorkspace>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ProtectedWorkspace>>(Items.ToArray());
        public ValueTask AddAsync(ProtectedWorkspace workspace, CancellationToken cancellationToken)
        {
            if (FailAdd) throw new WorkspaceRegistryException("test failure");
            Items.Add(workspace);
            return ValueTask.CompletedTask;
        }
        public ValueTask<bool> RemoveAsync(WorkspaceId id, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Items.RemoveAll(item => item.Id == id) > 0);
        public void Dispose() { }
    }
}
