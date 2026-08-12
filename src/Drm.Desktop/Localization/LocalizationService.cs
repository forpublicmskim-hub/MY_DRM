using System.Globalization;
using System.Resources;
using Drm.Domain;

namespace Drm.Desktop.Localization;

public interface ILocalizationService
{
    string GetString(string key);
    string GetStringForCulture(string key, CultureInfo culture);
    string Format(string key, params object?[] arguments);
}

public sealed class LocalizationService : ILocalizationService
{
    private const string FallbackKey = "Common.UnexpectedError";
    private static readonly ResourceManager Resources = new(
        "Drm.Desktop.Localization.Strings", typeof(LocalizationService).Assembly);

    public string GetString(string key) => GetStringForCulture(key, CultureInfo.CurrentUICulture);

    public string GetStringForCulture(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Resources.GetString(key, culture)
            ?? Resources.GetString(FallbackKey, culture)
            ?? "An unexpected error occurred.";
    }

    public string Format(string key, params object?[] arguments) =>
        FormatForCulture(key, CultureInfo.CurrentUICulture, arguments);

    public string FormatForCulture(string key, CultureInfo culture, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(culture, GetStringForCulture(key, culture), arguments);
    }
}

public static class WorkspaceMessageKeys
{
    public static string ForValidation(WorkspaceValidationCode code) => code switch
    {
        WorkspaceValidationCode.Allowed => "Workspace.Validation.Allowed",
        WorkspaceValidationCode.InvalidPath => "Workspace.Validation.InvalidPath",
        WorkspaceValidationCode.DoesNotExist => "Workspace.Validation.DoesNotExist",
        WorkspaceValidationCode.NotDirectory => "Workspace.Validation.NotDirectory",
        WorkspaceValidationCode.AccessDenied => "Workspace.Validation.AccessDenied",
        WorkspaceValidationCode.FileSystemRootNotAllowed => "Workspace.Validation.FileSystemRootNotAllowed",
        WorkspaceValidationCode.SystemLocationNotAllowed => "Workspace.Validation.SystemLocationNotAllowed",
        WorkspaceValidationCode.ApplicationLocationNotAllowed => "Workspace.Validation.ApplicationLocationNotAllowed",
        WorkspaceValidationCode.TemporaryLocationNotAllowed => "Workspace.Validation.TemporaryLocationNotAllowed",
        WorkspaceValidationCode.DuplicateWorkspace => "Workspace.Policy.Duplicate",
        WorkspaceValidationCode.ExistingWorkspaceContainsCandidate => "Workspace.Policy.ExistingContainsCandidate",
        WorkspaceValidationCode.CandidateContainsExistingWorkspace => "Workspace.Policy.CandidateContainsExisting",
        WorkspaceValidationCode.SymbolicLinkNotSupported => "Workspace.Validation.SymbolicLinkNotSupported",
        WorkspaceValidationCode.NetworkLocationNotSupported => "Workspace.Validation.NetworkLocationNotSupported",
        WorkspaceValidationCode.RemovableLocationNotSupported => "Workspace.Validation.RemovableLocationNotSupported",
        WorkspaceValidationCode.CloudLocationNotSupported => "Workspace.Validation.CloudLocationNotSupported",
        WorkspaceValidationCode.UnsupportedFileSystem => "Workspace.Validation.UnsupportedFileSystem",
        WorkspaceValidationCode.PersistenceFailed => "Workspace.Storage.SaveFailed",
        WorkspaceValidationCode.RegistryCorrupted => "Workspace.Storage.Corrupted",
        _ => "Common.UnexpectedError"
    };
}
