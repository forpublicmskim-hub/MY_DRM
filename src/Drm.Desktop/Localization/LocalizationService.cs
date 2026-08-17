using System.Globalization;
using System.Resources;
using Drm.Application;
using Drm.Domain;
using Drm.Policy;

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

public static class PolicyMessageKeys
{
    public static string ForLoadStatus(ProtectionPolicyLoadStatus status) => status switch
    {
        ProtectionPolicyLoadStatus.Loaded => "Policy.Status.Loaded",
        ProtectionPolicyLoadStatus.NotFound => "Policy.Status.NotFound",
        ProtectionPolicyLoadStatus.AccessDenied => "Policy.Status.AccessDenied",
        ProtectionPolicyLoadStatus.InvalidDocument => "Policy.Status.Invalid",
        ProtectionPolicyLoadStatus.Unsupported => "Policy.Status.Unsupported",
        ProtectionPolicyLoadStatus.TooLarge => "Policy.Status.TooLarge",
        ProtectionPolicyLoadStatus.Untrusted => "Policy.Status.Untrusted",
        ProtectionPolicyLoadStatus.Unavailable => "Policy.Status.Unavailable",
        _ => "Common.UnexpectedError"
    };

    public static string ForValidation(string code) => code switch
    {
        PolicyValidationCodes.Required => "Policy.Validation.Required",
        PolicyValidationCodes.InvalidSchemaVersion => "Policy.Validation.UnsupportedSchema",
        PolicyValidationCodes.InvalidPolicyVersion => "Policy.Validation.InvalidVersion",
        PolicyValidationCodes.InvalidExtension => "Policy.Validation.InvalidExtension",
        PolicyValidationCodes.DuplicateExtension => "Policy.Validation.DuplicateExtension",
        PolicyValidationCodes.ExtensionConflict => "Policy.Validation.ExtensionConflict",
        PolicyValidationCodes.ProtectedExtensionIncluded => "Policy.Validation.ProtectedExtension",
        PolicyValidationCodes.InvalidMaximumSize => "Policy.Validation.InvalidMaximumSize",
        PolicyValidationCodes.InvalidValidityRange => "Policy.Validation.InvalidValidityRange",
        PolicyValidationCodes.UnsupportedCapability => "Policy.Validation.UnsupportedCapability",
        PolicyValidationCodes.MissingCapability => "Policy.Validation.MissingCapability",
        PolicyValidationCodes.UnexpectedCapability => "Policy.Validation.UnexpectedCapability",
        PolicyValidationCodes.ValueTooLong => "Policy.Validation.ValueTooLong",
        PolicyValidationCodes.TooManyValues => "Policy.Validation.TooManyValues",
        PolicyValidationCodes.DocumentTooLarge => "Policy.Validation.DocumentTooLarge",
        PolicyValidationCodes.InvalidJson => "Policy.Validation.InvalidJson",
        PolicyValidationCodes.PersistenceFailed => "Policy.Validation.PersistenceFailed",
        _ => "Common.UnexpectedError"
    };
}
