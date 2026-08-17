using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Drm.Desktop.Localization;

namespace Drm.Desktop.Services;

public interface IPolicyFilePicker
{
    ValueTask<string?> PickAsync(CancellationToken cancellationToken);
}

public sealed class PolicyFilePickerUnavailableException : Exception;

public sealed class AvaloniaPolicyFilePicker(
    Func<TopLevel?> topLevelProvider,
    ILocalizationService localization) : IPolicyFilePicker
{
    public async ValueTask<string?> PickAsync(CancellationToken cancellationToken)
    {
        TopLevel? topLevel = topLevelProvider();
        if (topLevel?.StorageProvider.CanOpen != true)
            throw new PolicyFilePickerUnavailableException();

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = localization.GetString("Policy.Picker.Title"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(localization.GetString("Policy.Picker.FileType"))
                    {
                        Patterns = ["*.json"],
                        MimeTypes = ["application/json"]
                    }
                ]
            });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
