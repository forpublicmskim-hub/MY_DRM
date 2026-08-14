using Drm.Policy;

namespace Drm.Policy.Tests;

public sealed class ProtectionPolicyTests
{
    [Fact]
    public void NormalizesExtensionsAndProducesDeterministicJson()
    {
        ProtectionPolicyDraft first = ValidDraft();
        first.IncludedExtensions.Add("PDF");
        first.IncludedExtensions.Add(".docx");
        ProtectionPolicyDraft second = ValidDraft();
        second.IncludedExtensions.Add(".docx");
        second.IncludedExtensions.Add(".PDF");

        string firstJson = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(first));
        string secondJson = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(second));

        Assert.Equal(firstJson, secondJson);
        Assert.True(firstJson.IndexOf(".docx", StringComparison.Ordinal) <
                    firstJson.IndexOf(".pdf", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDuplicateAndConflictingExtensions()
    {
        ProtectionPolicyDraft draft = ValidDraft();
        draft.IncludedExtensions.Add("pdf");
        draft.IncludedExtensions.Add(".PDF");
        draft.ExcludedExtensions.Add(".pdf");

        PolicyValidationResult result = ProtectionPolicyValidator.Validate(draft);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.DuplicateExtension);
        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.ExtensionConflict);
    }

    [Fact]
    public void RejectsInvalidValidityAndMaximumSize()
    {
        ProtectionPolicyDraft draft = ValidDraft();
        draft.MaximumFileSizeBytes = 0;
        draft.ValidFromUtc = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        draft.ValidUntilUtc = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

        PolicyValidationResult result = ProtectionPolicyValidator.Validate(draft);

        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.InvalidMaximumSize);
        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.InvalidValidityRange);
    }

    [Fact]
    public void RejectsUnknownJsonField()
    {
        string json = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(ValidDraft()));
        json = json.Replace("\"displayName\":", "\"unexpected\": true,\n  \"displayName\":", StringComparison.Ordinal);

        PolicyLoadResult result = ProtectionPolicySerializer.Deserialize(json);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Validation.Errors, error => error.Code == PolicyValidationCodes.InvalidJson);
    }

    [Fact]
    public void RejectsUnsupportedSchemaAndCapability()
    {
        ProtectionPolicyDocument valid = PolicyNormalizer.Normalize(ValidDraft());
        ProtectionPolicyDocument unsupported = valid with
        {
            SchemaVersion = 99,
            RequiredCapabilities = [..valid.RequiredCapabilities, "future.capability.v1"]
        };

        PolicyValidationResult result = ProtectionPolicyValidator.Validate(unsupported);

        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.InvalidSchemaVersion);
        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.UnsupportedCapability);
    }

    [Fact]
    public void RejectsMissingCapabilityForConfiguredOption()
    {
        ProtectionPolicyDocument valid = PolicyNormalizer.Normalize(ValidDraft());
        ProtectionPolicyDocument missing = valid with
        {
            RequiredCapabilities = valid.RequiredCapabilities
                .Where(capability => capability != PolicyCapabilities.MaximumSizeV1)
                .ToArray()
        };

        PolicyValidationResult result = ProtectionPolicyValidator.Validate(missing);

        Assert.Contains(result.Errors, error => error.Code == PolicyValidationCodes.MissingCapability);
    }

    [Fact]
    public void RejectsExplicitNullAndOversizedDocument()
    {
        string json = ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(ValidDraft()));
        string explicitNull = json.Replace("\"protection\": {", "\"protection\": null,\n    \"ignoredProtection\": {",
            StringComparison.Ordinal);

        PolicyLoadResult nullResult = ProtectionPolicySerializer.Deserialize(explicitNull);
        PolicyLoadResult oversizedResult = ProtectionPolicySerializer.Deserialize(
            new string('x', ProtectionPolicySerializer.MaximumDocumentBytes + 1));

        Assert.False(nullResult.IsSuccess);
        Assert.Contains(nullResult.Validation.Errors, error => error.Code == PolicyValidationCodes.InvalidJson);
        Assert.Contains(oversizedResult.Validation.Errors, error => error.Code == PolicyValidationCodes.DocumentTooLarge);
    }

    [Fact]
    public async Task AtomicSaveCanBeReloadedAndCompiled()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "policy.json");
        ProtectionPolicyDocument document = PolicyNormalizer.Normalize(ValidDraft());

        await PolicyFileStore.SaveAsync(document, path);
        PolicyLoadResult loaded = await PolicyFileStore.LoadAsync(path);
        EffectiveProtectionPolicy effective = PolicyNormalizer.Compile(loaded.Document!);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(document.PolicyId, effective.PolicyId);
        Assert.Contains(".tmp", effective.ExcludedExtensions);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    private static ProtectionPolicyDraft ValidDraft() => new()
    {
        PolicyId = Guid.Parse("7b972554-9d71-47b7-8efe-52021fd73341"),
        PolicyVersion = 1,
        DisplayName = "일반 문서 보호 정책",
        MaximumFileSizeBytes = 1_073_741_824
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory().FullName;
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
