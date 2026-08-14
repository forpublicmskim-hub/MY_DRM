using System.Collections.Immutable;

namespace Drm.Policy;

public static class PolicyNormalizer
{
    public static ProtectionPolicyDocument Normalize(ProtectionPolicyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        IReadOnlyList<string> included = NormalizeExtensions(draft.IncludedExtensions);
        IReadOnlyList<string> excluded = NormalizeExtensions(draft.ExcludedExtensions);
        List<string> capabilities = [PolicyCapabilities.ExtensionFilterV1];
        if (draft.MaximumFileSizeBytes is not null) capabilities.Add(PolicyCapabilities.MaximumSizeV1);
        if (draft.ValidFromUtc is not null || draft.ValidUntilUtc is not null)
            capabilities.Add(PolicyCapabilities.ValidityWindowV1);

        return new ProtectionPolicyDocument(
            PolicySchemaVersions.Current,
            draft.PolicyId,
            draft.PolicyVersion,
            draft.DisplayName.Trim(),
            PolicyDocumentStatus.Draft,
            draft.Enabled,
            capabilities.Order(StringComparer.Ordinal).ToArray(),
            new ProtectionPolicySettings(
                draft.ProtectNewFiles,
                draft.ProtectExistingFiles,
                included,
                excluded,
                draft.MaximumFileSizeBytes),
            new PolicyValidity(draft.ValidFromUtc?.ToUniversalTime(), draft.ValidUntilUtc?.ToUniversalTime()));
    }

    public static ProtectionPolicyDraft ToDraft(ProtectionPolicyDocument document)
    {
        ProtectionPolicyDraft draft = new()
        {
            PolicyId = document.PolicyId,
            PolicyVersion = document.PolicyVersion,
            DisplayName = document.DisplayName,
            Enabled = document.Enabled,
            ProtectNewFiles = document.Protection.ProtectNewFiles,
            ProtectExistingFiles = document.Protection.ProtectExistingFiles,
            MaximumFileSizeBytes = document.Protection.MaximumFileSizeBytes,
            ValidFromUtc = document.Validity.ValidFromUtc,
            ValidUntilUtc = document.Validity.ValidUntilUtc
        };
        draft.IncludedExtensions.Clear();
        foreach (string extension in document.Protection.IncludedExtensions) draft.IncludedExtensions.Add(extension);
        draft.ExcludedExtensions.Clear();
        foreach (string extension in document.Protection.ExcludedExtensions) draft.ExcludedExtensions.Add(extension);
        return draft;
    }

    public static EffectiveProtectionPolicy Compile(ProtectionPolicyDocument document)
    {
        PolicyValidationResult validation = ProtectionPolicyValidator.Validate(document);
        if (!validation.IsValid) throw new InvalidPolicyException(validation);
        ProtectionPolicyDocument normalized = Normalize(ToDraft(document));
        return new EffectiveProtectionPolicy(
            normalized.PolicyId,
            normalized.PolicyVersion,
            normalized.DisplayName,
            normalized.Enabled,
            normalized.Protection.ProtectNewFiles,
            normalized.Protection.ProtectExistingFiles,
            normalized.Protection.IncludedExtensions.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            normalized.Protection.ExcludedExtensions.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            normalized.Protection.MaximumFileSizeBytes,
            normalized.Validity.ValidFromUtc,
            normalized.Validity.ValidUntilUtc);
    }

    public static string NormalizeExtension(string? extension)
    {
        string value = extension?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length > 0 && value[0] != '.') value = "." + value;
        return value;
    }

    private static string[] NormalizeExtensions(IEnumerable<string> extensions) =>
        extensions.Select(NormalizeExtension)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

public sealed class InvalidPolicyException(PolicyValidationResult validation)
    : Exception("The policy failed validation.")
{
    public PolicyValidationResult Validation { get; } = validation;
}
