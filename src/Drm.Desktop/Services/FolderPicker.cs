using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Drm.Desktop.Services;

public interface IFolderPicker
{
    ValueTask<string?> PickAsync(CancellationToken cancellationToken);
}

public sealed class AvaloniaFolderPicker(Func<TopLevel?> topLevelProvider) : IFolderPicker
{
    public async ValueTask<string?> PickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TopLevel? topLevel = topLevelProvider();
        if (topLevel is null || !topLevel.StorageProvider.CanPickFolder)
            throw new InvalidOperationException("이 플랫폼에서는 폴더 선택 창을 사용할 수 없습니다.");

        IReadOnlyList<IStorageFolder> selected = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "보호 대상으로 등록할 폴더 선택", AllowMultiple = false });
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Count == 0) return null;
        return selected[0].TryGetLocalPath();
    }
}
