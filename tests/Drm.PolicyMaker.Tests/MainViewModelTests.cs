using Drm.Policy;
using Drm.PolicyMaker.Services;
using Drm.PolicyMaker.ViewModels;

namespace Drm.PolicyMaker.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task SavesValidDraftAndIncrementsVersionAfterMeaningfulChange()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "policy.json");
        FileDialogStub dialogs = new() { SavePath = path };
        using MainViewModel viewModel = new(dialogs)
        {
            DisplayName = "문서 보호",
            IncludedExtensions = ".pdf"
        };

        await viewModel.SaveAsCommand.ExecuteAsync(null);
        Assert.Equal(1, viewModel.PolicyVersion);

        viewModel.IncludedExtensions = ".pdf\n.docx";
        await viewModel.SaveAsCommand.ExecuteAsync(null);

        PolicyLoadResult loaded = await PolicyFileStore.LoadAsync(path);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(2, loaded.Document!.PolicyVersion);
        Assert.Equal(2, viewModel.PolicyVersion);
    }

    [Fact]
    public async Task ValidationFailureDoesNotCreateFile()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "invalid.json");
        using MainViewModel viewModel = new(new FileDialogStub { SavePath = path })
        {
            DisplayName = string.Empty
        };

        await viewModel.SaveAsCommand.ExecuteAsync(null);

        Assert.False(File.Exists(path));
        Assert.NotEmpty(viewModel.ValidationErrors);
    }

    [Fact]
    public async Task OpensPolicyThroughSharedLoader()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "existing.json");
        ProtectionPolicyDraft draft = new() { DisplayName = "기존 정책" };
        draft.IncludedExtensions.Add(".xlsx");
        await PolicyFileStore.SaveAsync(PolicyNormalizer.Normalize(draft), path);
        using MainViewModel viewModel = new(new FileDialogStub { OpenPath = path });

        await viewModel.OpenCommand.ExecuteAsync(null);

        Assert.Equal("기존 정책", viewModel.DisplayName);
        Assert.Contains(".xlsx", viewModel.IncludedExtensions, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", viewModel.JsonPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LivePreviewUpdatesAfterDebounceAndKeepsLastValidJsonOnError()
    {
        using MainViewModel viewModel = new(new FileDialogStub());
        viewModel.IncludedExtensions = ".pdf";

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Contains(".pdf", viewModel.JsonPreview, StringComparison.Ordinal);
        string lastValidPreview = viewModel.JsonPreview;

        viewModel.IncludedExtensions = ".drm";
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(lastValidPreview, viewModel.JsonPreview);
        Assert.NotEmpty(viewModel.ValidationErrors);
    }

    [Fact]
    public async Task DateAndTimePickersArePersistedAsUtc()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "validity.json");
        using MainViewModel viewModel = new(new FileDialogStub { SavePath = path })
        {
            DisplayName = "기간 정책",
            ValidFromDate = new DateTime(2026, 8, 14),
            ValidFromTime = new TimeSpan(9, 30, 0),
            ValidUntilDate = new DateTime(2026, 8, 15),
            ValidUntilTime = new TimeSpan(18, 45, 0)
        };

        await viewModel.SaveAsCommand.ExecuteAsync(null);

        PolicyLoadResult loaded = await PolicyFileStore.LoadAsync(path);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero),
            loaded.Document!.Validity.ValidFromUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 18, 45, 0, TimeSpan.Zero),
            loaded.Document.Validity.ValidUntilUtc);
    }

    [Fact]
    public void CalendarPickerDateTypeCanBeAssignedWithoutConversion()
    {
        using MainViewModel viewModel = new(new FileDialogStub());

        viewModel.ValidFromDate = new DateTime(2026, 8, 22);
        viewModel.ValidUntilDate = new DateTime(2026, 8, 23);

        Assert.Equal(new DateTime(2026, 8, 22), viewModel.ValidFromDate);
        Assert.Equal(TimeSpan.Zero, viewModel.ValidFromTime);
        Assert.Equal(new DateTime(2026, 8, 23), viewModel.ValidUntilDate);
        Assert.Equal(TimeSpan.Zero, viewModel.ValidUntilTime);
    }

    private sealed class FileDialogStub : IPolicyFileDialog
    {
        public string? OpenPath { get; init; }
        public string? SavePath { get; init; }
        public ValueTask<string?> OpenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(OpenPath);
        public ValueTask<string?> SaveAsAsync(string suggestedFileName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(SavePath);
    }

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
