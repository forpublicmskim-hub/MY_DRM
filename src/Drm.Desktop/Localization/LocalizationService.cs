using System.Globalization;
using System.Resources;
using Drm.Domain;

namespace Drm.Desktop.Localization;

public interface ILocalizationService
{
    string GetString(string key);
}

public sealed class LocalizationService : ILocalizationService
{
    private const string FallbackKey = "Common.UnexpectedError";
    private static readonly ResourceManager Resources = new(
        "Drm.Desktop.Localization.Strings", typeof(LocalizationService).Assembly);

    public string GetString(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? Resources.GetString(FallbackKey, CultureInfo.CurrentUICulture)
        ?? "Unexpected error.";

    public static bool ContainsResource(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) is not null;
}

public sealed class WorkspaceErrorLocalizer(ILocalizationService localization)
{
    public string GetMessage(WorkspaceValidationCode code) => localization.GetString(GetResourceKey(code));

    public static string GetResourceKey(WorkspaceValidationCode code) => code switch
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
