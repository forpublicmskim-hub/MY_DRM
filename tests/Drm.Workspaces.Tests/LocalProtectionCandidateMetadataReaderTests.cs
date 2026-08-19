using Drm.Application;
using Drm.Domain;
using Drm.Platform.Local;

namespace Drm.Workspaces.Tests;

public sealed class LocalProtectionCandidateMetadataReaderTests
{
    [Fact]
    public async Task ReadsNormalizedFileMetadataAndVersion()
    {
        using TemporaryDirectory directory = new();
        string child = Path.Combine("Documents", "Report.PDF");
        Directory.CreateDirectory(Path.Combine(directory.Path, "Documents"));
        string file = Path.Combine(directory.Path, child);
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);

        ProtectionCandidateMetadataResult result =
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), child, default);

        Assert.Equal(ProtectionCandidateMetadataStatus.Available, result.Status);
        Assert.Equal(child, result.Metadata!.RelativePath);
        Assert.Equal(".pdf", result.Metadata.NormalizedExtension);
        Assert.False(result.Metadata.IsDirectory);
        Assert.Equal(4, result.Metadata.FileSizeBytes);
        Assert.Equal(4, result.Metadata.Version.Length);
        Assert.Equal(TimeSpan.Zero, result.Metadata.Version.LastWriteTimeUtc.Offset);
    }

    [Fact]
    public async Task ReadsDirectoryWithoutFileLength()
    {
        using TemporaryDirectory directory = new();
        Directory.CreateDirectory(Path.Combine(directory.Path, "Folder"));

        ProtectionCandidateMetadataResult result =
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), "Folder", default);

        Assert.Equal(ProtectionCandidateMetadataStatus.Available, result.Status);
        Assert.True(result.Metadata!.IsDirectory);
        Assert.Null(result.Metadata.FileSizeBytes);
        Assert.Null(result.Metadata.Version.Length);
    }

    [Fact]
    public async Task FileWithoutExtensionHasEmptyNormalizedExtension()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "README"), "text");

        ProtectionCandidateMetadataResult result =
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), "README", default);

        Assert.Equal(string.Empty, result.Metadata!.NormalizedExtension);
    }

    [Fact]
    public async Task MissingFileReturnsNotFound()
    {
        using TemporaryDirectory directory = new();

        ProtectionCandidateMetadataResult result =
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), "missing.pdf", default);

        Assert.Equal(ProtectionCandidateMetadataStatus.NotFound, result.Status);
        Assert.Null(result.Metadata);
    }

    [Theory]
    [InlineData("..\\outside.pdf")]
    [InlineData("../outside.pdf")]
    public async Task ParentTraversalIsRejected(string relativePath)
    {
        using TemporaryDirectory directory = new();

        ProtectionCandidateMetadataResult result =
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), relativePath, default);

        Assert.Equal(ProtectionCandidateMetadataStatus.UnsafePath, result.Status);
    }

    [Fact]
    public async Task RootedPathIsRejected()
    {
        using TemporaryDirectory directory = new();
        string rooted = Path.GetFullPath(Path.Combine(directory.Path, "..", "outside.pdf"));

        ProtectionCandidateMetadataResult result =
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), rooted, default);

        Assert.Equal(ProtectionCandidateMetadataStatus.UnsafePath, result.Status);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using TemporaryDirectory directory = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CreateReader().ReadAsync(CreateWorkspace(directory.Path), "file.pdf", cancellation.Token));
    }

    private static LocalProtectionCandidateMetadataReader CreateReader() => new();

    private static ProtectedWorkspace CreateWorkspace(string path) => new(
        WorkspaceId.New(),
        "Test",
        new WorkspaceLocation(path, path),
        WorkspaceRegistrationState.Registered,
        WorkspaceProtectionState.NotActivated,
        DateTimeOffset.UtcNow);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
