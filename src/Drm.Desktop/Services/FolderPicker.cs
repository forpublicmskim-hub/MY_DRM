using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Drm.Desktop.Localization;

namespace Drm.Desktop.Services;

public interface IFolderPicker
{
    ValueTask<string?> PickAsync(CancellationToken cancellationToken);
}

public sealed class FolderPickerUnavailableException : Exception;

public sealed class AvaloniaFolderPicker(
    Func<TopLevel?> topLevelProvider,
    ILocalizationService localization) : IFolderPicker
{
    public async ValueTask<string?> PickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TopLevel? topLevel = topLevelProvider();
        if (topLevel is null || !topLevel.StorageProvider.CanPickFolder)
            throw new FolderPickerUnavailableException();

        IReadOnlyList<IStorageFolder> selected = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = localization.GetString("Workspace.Picker.Title"), AllowMultiple = false });
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Count == 0) return null;
        return selected[0].TryGetLocalPath();
    }
}
