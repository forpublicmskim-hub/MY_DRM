using Drm.Application;
using Drm.Domain;
using Drm.Infrastructure;
using Drm.Platform.Abstractions;
using Drm.Platform.Local;

namespace Drm.Workspaces.Tests;

public sealed class WorkspacePersistenceTests
{
    [Fact]
    public async Task RegistryRestoresAfterRestartAndUnregisterKeepsFolder()
    {
        using TemporaryDirectory temporary = new();
        string folder = Directory.CreateDirectory(Path.Combine(temporary.Path, "workspace")).FullName;
        string registryPath = Path.Combine(temporary.Path, "settings", "workspaces.json");
        WorkspaceId id;

        using (WorkspaceService first = CreateService(new JsonWorkspaceRegistry(registryPath)))
        {
            WorkspaceRegistrationResult registered = await first.RegisterAsync(folder);
            Assert.True(registered.IsSuccess);
            id = registered.Workspace!.Id;
        }

        using (WorkspaceService restarted = CreateService(new JsonWorkspaceRegistry(registryPath)))
        {
            IReadOnlyList<ProtectedWorkspace> restored = await restarted.GetAllAsync();
            ProtectedWorkspace workspace = Assert.Single(restored);
            Assert.Equal(id, workspace.Id);

            Assert.True(await restarted.UnregisterAsync(id));
        }

        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(registryPath)!, "*.tmp"));
        using JsonWorkspaceRegistry finalRegistry = new(registryPath);
        Assert.Empty(await finalRegistry.GetAllAsync(default));
    }

    [Fact]
    public async Task CorruptedRegistryIsNotSilentlyTreatedAsEmpty()
    {
        using TemporaryDirectory temporary = new();
        string registryPath = Path.Combine(temporary.Path, "workspaces.json");
        await File.WriteAllTextAsync(registryPath, "{ not-json }");
        using JsonWorkspaceRegistry registry = new(registryPath);

        await Assert.ThrowsAsync<WorkspaceRegistryCorruptedException>(async () =>
            await registry.GetAllAsync(default));
    }

    [Fact]
    public async Task LocalResolverRejectsFileSystemRoot()
    {
        LocalWorkspaceLocationResolver resolver = new();
        string root = Path.GetPathRoot(Environment.CurrentDirectory)!;

        WorkspaceLocationResolution result = await resolver.ResolveAsync(root, default);

        Assert.False(result.Validation.IsAllowed);
        Assert.Equal(WorkspaceValidationCode.FileSystemRootNotAllowed, result.Validation.Code);
    }

    [Fact]
    public async Task LocalResolverRejectsTemporaryLocationWithSpecificCode()
    {
        LocalWorkspaceLocationResolver resolver = new();

        WorkspaceLocationResolution result = await resolver.ResolveAsync(Path.GetTempPath(), default);

        Assert.False(result.Validation.IsAllowed);
        Assert.Equal(WorkspaceValidationCode.TemporaryLocationNotAllowed, result.Validation.Code);
    }

    [Fact]
    public void LocalResolverUsesPlatformCaseRules()
    {
        LocalWorkspaceLocationResolver resolver = new();
        WorkspaceLocation lower = new("display", Path.Combine(Path.GetTempPath(), "case-sensitive"));
        WorkspaceLocation upper = lower with { CanonicalPath = lower.CanonicalPath.ToUpperInvariant() };

        Assert.Equal(OperatingSystem.IsWindows(), resolver.AreSame(lower, upper));
    }

    private static WorkspaceService CreateService(IWorkspaceRegistry registry)
    {
        PassthroughResolver resolver = new();
        return new WorkspaceService(registry, resolver, new WorkspaceRegistrationPolicy(resolver), new FixedClock());
    }

    private sealed class PassthroughResolver : IWorkspaceLocationResolver
    {
        private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public ValueTask<WorkspaceLocationResolution> ResolveAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return ValueTask.FromResult(new WorkspaceLocationResolution(
                new WorkspaceLocation(canonical, canonical), WorkspaceValidationResult.Allowed()));
        }
        public bool AreSame(WorkspaceLocation first, WorkspaceLocation second) =>
            string.Equals(first.CanonicalPath, second.CanonicalPath, Comparison);
        public bool IsAncestorOf(WorkspaceLocation parent, WorkspaceLocation child) =>
            !AreSame(parent, child) && child.CanonicalPath.StartsWith(
                parent.CanonicalPath + Path.DirectorySeparatorChar, Comparison);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"drm-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
