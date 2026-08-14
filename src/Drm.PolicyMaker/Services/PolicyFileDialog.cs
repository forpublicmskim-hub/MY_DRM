using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Drm.PolicyMaker.Services;

public interface IPolicyFileDialog
{
    ValueTask<string?> OpenAsync(CancellationToken cancellationToken);
    ValueTask<string?> SaveAsAsync(string suggestedFileName, CancellationToken cancellationToken);
}

public sealed class AvaloniaPolicyFileDialog(Func<TopLevel?> topLevelProvider) : IPolicyFileDialog
{
    private static readonly FilePickerFileType JsonFileType = new("JSON 정책")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"]
    };

    public async ValueTask<string?> OpenAsync(CancellationToken cancellationToken)
    {
        TopLevel topLevel = GetTopLevel();
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "정책 Draft 열기",
                AllowMultiple = false,
                FileTypeFilter = [JsonFileType]
            });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async ValueTask<string?> SaveAsAsync(string suggestedFileName, CancellationToken cancellationToken)
    {
        TopLevel topLevel = GetTopLevel();
        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "개발용 정책 Draft 저장",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "json",
                FileTypeChoices = [JsonFileType],
                ShowOverwritePrompt = true
            });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    private TopLevel GetTopLevel()
    {
        TopLevel? topLevel = topLevelProvider();
        if (topLevel is null || !topLevel.StorageProvider.CanOpen || !topLevel.StorageProvider.CanSave)
            throw new InvalidOperationException("이 환경에서는 로컬 정책 파일을 열거나 저장할 수 없습니다.");
        return topLevel;
    }
}
