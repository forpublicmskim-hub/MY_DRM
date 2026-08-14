using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Drm.Policy;

public static class PolicySchemaVersions
{
    public const int Current = 1;
}

public static class PolicyCapabilities
{
    public const string ExtensionFilterV1 = "protection.extension-filter.v1";
    public const string MaximumSizeV1 = "protection.maximum-size.v1";
    public const string ValidityWindowV1 = "protection.validity-window.v1";

    public static ImmutableHashSet<string> Supported { get; } =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            ExtensionFilterV1, MaximumSizeV1, ValidityWindowV1);
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyDocumentStatus>))]
public enum PolicyDocumentStatus
{
    Draft
}

public sealed class ProtectionPolicyDraft
{
    public Guid PolicyId { get; set; } = Guid.NewGuid();
    public int PolicyVersion { get; set; } = 1;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool ProtectNewFiles { get; set; } = true;
    public bool ProtectExistingFiles { get; set; }
    public IList<string> IncludedExtensions { get; } = new List<string>();
    public IList<string> ExcludedExtensions { get; } = new List<string> { ".tmp", ".drm" };
    public long? MaximumFileSizeBytes { get; set; } = 1_073_741_824;
    public DateTimeOffset? ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
}

public sealed record ProtectionPolicyDocument(
    int SchemaVersion,
    Guid PolicyId,
    int PolicyVersion,
    string DisplayName,
    PolicyDocumentStatus DocumentStatus,
    bool Enabled,
    IReadOnlyList<string> RequiredCapabilities,
    ProtectionPolicySettings Protection,
    PolicyValidity Validity);

public sealed record ProtectionPolicySettings(
    bool ProtectNewFiles,
    bool ProtectExistingFiles,
    IReadOnlyList<string> IncludedExtensions,
    IReadOnlyList<string> ExcludedExtensions,
    long? MaximumFileSizeBytes);

public sealed record PolicyValidity(
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc);

public sealed record EffectiveProtectionPolicy(
    Guid PolicyId,
    int PolicyVersion,
    string DisplayName,
    bool Enabled,
    bool ProtectNewFiles,
    bool ProtectExistingFiles,
    ImmutableHashSet<string> IncludedExtensions,
    ImmutableHashSet<string> ExcludedExtensions,
    long? MaximumFileSizeBytes,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc);
