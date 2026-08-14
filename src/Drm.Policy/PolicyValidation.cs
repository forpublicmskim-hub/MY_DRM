using System.Collections.Immutable;

namespace Drm.Policy;

public enum PolicyValidationSeverity { Error, Warning }

public static class PolicyValidationCodes
{
    public const string Required = "value.required";
    public const string InvalidSchemaVersion = "schema.unsupported";
    public const string InvalidPolicyVersion = "policy.version.invalid";
    public const string InvalidExtension = "extension.invalid";
    public const string DuplicateExtension = "extension.duplicate";
    public const string ExtensionConflict = "extension.conflict";
    public const string ProtectedExtensionIncluded = "extension.protected-format-included";
    public const string InvalidMaximumSize = "protection.maximum-size.invalid";
    public const string InvalidValidityRange = "validity.range.invalid";
    public const string UnsupportedCapability = "capability.unsupported";
    public const string MissingCapability = "capability.missing";
    public const string UnexpectedCapability = "capability.unexpected";
    public const string ValueTooLong = "value.too-long";
    public const string TooManyValues = "value.too-many";
    public const string DocumentTooLarge = "document.too-large";
    public const string InvalidJson = "json.invalid";
    public const string PersistenceFailed = "persistence.failed";
}

public sealed record PolicyValidationError(
    string Path,
    string Code,
    PolicyValidationSeverity Severity,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed record PolicyValidationResult(IReadOnlyList<PolicyValidationError> Errors)
{
    public bool IsValid => Errors.All(error => error.Severity != PolicyValidationSeverity.Error);
    public static PolicyValidationResult Valid { get; } = new(Array.Empty<PolicyValidationError>());
}

public static class ProtectionPolicyValidator
{
    public const int MaximumDisplayNameLength = 200;
    public const int MaximumExtensionLength = 32;
    public const int MaximumExtensionsPerList = 256;
    public const long MaximumProtectedFileSizeBytes = 1_099_511_627_776;
    private static readonly ImmutableHashSet<string> ReservedProtectedExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".drm");

    public static PolicyValidationResult Validate(ProtectionPolicyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<PolicyValidationError> errors = [];
        if (draft.PolicyId == Guid.Empty) Add(errors, "policyId", PolicyValidationCodes.Required);
        if (draft.PolicyVersion <= 0) Add(errors, "policyVersion", PolicyValidationCodes.InvalidPolicyVersion);
        if (string.IsNullOrWhiteSpace(draft.DisplayName)) Add(errors, "displayName", PolicyValidationCodes.Required);
        else if (draft.DisplayName.Length > MaximumDisplayNameLength)
            Add(errors, "displayName", PolicyValidationCodes.ValueTooLong, MaximumDisplayNameLength);
        if (draft.IncludedExtensions.Count > MaximumExtensionsPerList)
            Add(errors, "protection.includedExtensions", PolicyValidationCodes.TooManyValues, MaximumExtensionsPerList);
        if (draft.ExcludedExtensions.Count > MaximumExtensionsPerList)
            Add(errors, "protection.excludedExtensions", PolicyValidationCodes.TooManyValues, MaximumExtensionsPerList);
        ValidateExtensions(draft.IncludedExtensions, "protection.includedExtensions", errors);
        ValidateExtensions(draft.ExcludedExtensions, "protection.excludedExtensions", errors);

        HashSet<string> included = NormalizeSet(draft.IncludedExtensions);
        HashSet<string> excluded = NormalizeSet(draft.ExcludedExtensions);
        foreach (string extension in included.Intersect(excluded, StringComparer.OrdinalIgnoreCase))
            Add(errors, "protection.includedExtensions", PolicyValidationCodes.ExtensionConflict, extension);
        foreach (string extension in included.Intersect(ReservedProtectedExtensions, StringComparer.OrdinalIgnoreCase))
            Add(errors, "protection.includedExtensions", PolicyValidationCodes.ProtectedExtensionIncluded, extension);

        if (draft.MaximumFileSizeBytes is <= 0 or > MaximumProtectedFileSizeBytes)
            Add(errors, "protection.maximumFileSizeBytes", PolicyValidationCodes.InvalidMaximumSize);
        if (draft.ValidFromUtc is not null && draft.ValidUntilUtc is not null &&
            draft.ValidUntilUtc <= draft.ValidFromUtc)
            Add(errors, "validity.validUntilUtc", PolicyValidationCodes.InvalidValidityRange);
        return new PolicyValidationResult(errors);
    }

    public static PolicyValidationResult Validate(
        ProtectionPolicyDocument document,
        IReadOnlySet<string>? supportedCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<PolicyValidationError> errors = [];
        if (document.SchemaVersion != PolicySchemaVersions.Current)
            Add(errors, "schemaVersion", PolicyValidationCodes.InvalidSchemaVersion, document.SchemaVersion);
        ProtectionPolicyDraft draft = PolicyNormalizer.ToDraft(document);
        errors.AddRange(Validate(draft).Errors);
        IReadOnlySet<string> supported = supportedCapabilities ?? PolicyCapabilities.Supported;
        foreach (string capability in document.RequiredCapabilities.Where(capability => !supported.Contains(capability)))
            Add(errors, "requiredCapabilities", PolicyValidationCodes.UnsupportedCapability, capability);
        HashSet<string> declared = document.RequiredCapabilities.ToHashSet(StringComparer.Ordinal);
        HashSet<string> expected = PolicyNormalizer.Normalize(draft).RequiredCapabilities.ToHashSet(StringComparer.Ordinal);
        foreach (string capability in expected.Except(declared, StringComparer.Ordinal))
            Add(errors, "requiredCapabilities", PolicyValidationCodes.MissingCapability, capability);
        foreach (string capability in declared.Except(expected, StringComparer.Ordinal))
            Add(errors, "requiredCapabilities", PolicyValidationCodes.UnexpectedCapability, capability);
        return new PolicyValidationResult(errors);
    }

    private static void ValidateExtensions(IEnumerable<string> source, string path, List<PolicyValidationError> errors)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (string value in source)
        {
            string normalized = PolicyNormalizer.NormalizeExtension(value);
            if (normalized.Length < 2 || normalized.Length > MaximumExtensionLength || normalized[0] != '.' ||
                normalized.AsSpan(1).ContainsAny(Path.GetInvalidFileNameChars()) ||
                normalized.AsSpan(1).Contains('.') || normalized.Any(char.IsWhiteSpace))
                Add(errors, $"{path}[{index}]", PolicyValidationCodes.InvalidExtension, value);
            else if (!seen.Add(normalized))
                Add(errors, $"{path}[{index}]", PolicyValidationCodes.DuplicateExtension, normalized);
            index++;
        }
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string> source) =>
        source.Select(PolicyNormalizer.NormalizeExtension)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Add(
        List<PolicyValidationError> errors,
        string path,
        string code,
        object? value = null) =>
        errors.Add(new PolicyValidationError(path, code, PolicyValidationSeverity.Error,
            value is null
                ? ImmutableDictionary<string, object?>.Empty
                : ImmutableDictionary<string, object?>.Empty.Add("value", value)));
}
