using System.Text;
using Drm.Application;
using Drm.Platform.Local;
using Drm.Policy;

namespace Drm.Workspaces.Tests;

public sealed class LocalProtectionPolicySourceTests
{
    [Fact]
    public async Task ReadsExactlyMaximumBytes()
    {
        using TemporaryFile file = new(new byte[ProtectionPolicySerializer.MaximumDocumentBytes]);
        LocalFileProtectionPolicySource source = new();

        ProtectionPolicySourceReadResult result = await source.ReadAsync(file.Path, CancellationToken.None);

        Assert.Equal(PolicySourceReadStatus.Read, result.Status);
        Assert.Equal(ProtectionPolicySerializer.MaximumDocumentBytes, result.Content!.Length);
    }

    [Fact]
    public async Task RejectsOneByteOverMaximumWithoutReadingUnboundedContent()
    {
        using TemporaryFile file = new(new byte[ProtectionPolicySerializer.MaximumDocumentBytes + 1]);
        LocalFileProtectionPolicySource source = new();

        ProtectionPolicySourceReadResult result = await source.ReadAsync(file.Path, CancellationToken.None);

        Assert.Equal(PolicySourceReadStatus.TooLarge, result.Status);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task RejectsInvalidUtf8()
    {
        using TemporaryFile file = new([0xC3, 0x28]);
        LocalFileProtectionPolicySource source = new();

        ProtectionPolicySourceReadResult result = await source.ReadAsync(file.Path, CancellationToken.None);

        Assert.Equal(PolicySourceReadStatus.InvalidEncoding, result.Status);
    }

    [Fact]
    public async Task ClassifiesMissingFileAndDirectoryPath()
    {
        using TemporaryDirectory directory = new();
        LocalFileProtectionPolicySource source = new();

        ProtectionPolicySourceReadResult missing = await source.ReadAsync(
            Path.Combine(directory.Path, "missing.json"), CancellationToken.None);
        ProtectionPolicySourceReadResult directoryResult = await source.ReadAsync(
            directory.Path, CancellationToken.None);

        Assert.Equal(PolicySourceReadStatus.NotFound, missing.Status);
        Assert.Equal(PolicySourceReadStatus.Unavailable, directoryResult.Status);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        using TemporaryFile file = new(Encoding.UTF8.GetBytes("{}"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        LocalFileProtectionPolicySource source = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ReadAsync(file.Path, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task PolicyMakerFileRoundTripsThroughDrmConsumerBoundary()
    {
        using TemporaryDirectory directory = new();
        string path = System.IO.Path.Combine(directory.Path, "policy.json");
        ProtectionPolicyDraft draft = new() { DisplayName = "Contract Policy" };
        draft.IncludedExtensions.Add(".pdf");
        await PolicyFileStore.SaveAsync(PolicyNormalizer.Normalize(draft), path);
        ProtectionPolicyLoader loader = new(
            new LocalFileProtectionPolicySource(), new FixedClock(), PolicyTrustOptions.Development);

        ProtectionPolicyLoadResult result = await loader.LoadAsync(path);

        Assert.True(result.IsLoaded);
        Assert.Equal(draft.PolicyId, result.Snapshot!.Policy.PolicyId);
        Assert.Equal("Contract Policy", result.Snapshot.Policy.DisplayName);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(byte[] content)
        {
            Path = System.IO.Path.GetTempFileName();
            File.WriteAllBytes(Path, content);
        }

        public string Path { get; }
        public void Dispose() => File.Delete(Path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, true);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
    }
}
