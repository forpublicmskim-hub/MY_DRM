using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drm.Policy;

public sealed record PolicyLoadResult(ProtectionPolicyDocument? Document, PolicyValidationResult Validation)
{
    public bool IsSuccess => Document is not null && Validation.IsValid;
}

public static class ProtectionPolicySerializer
{
    public const int MaximumDocumentBytes = 1_048_576;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ProtectionPolicyDocument document)
    {
        PolicyValidationResult validation = ProtectionPolicyValidator.Validate(document);
        if (!validation.IsValid) throw new InvalidPolicyException(validation);
        ProtectionPolicyDocument normalized = PolicyNormalizer.Normalize(PolicyNormalizer.ToDraft(document));
        return JsonSerializer.Serialize(normalized, Options).ReplaceLineEndings("\n") + "\n";
    }

    public static PolicyLoadResult Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
            return DocumentTooLarge();
        try
        {
            ProtectionPolicyDocument? document = JsonSerializer.Deserialize<ProtectionPolicyDocument>(json, Options);
            if (document is null) return InvalidJson();
            PolicyValidationResult validation = ProtectionPolicyValidator.Validate(document);
            return new PolicyLoadResult(validation.IsValid ? document : null, validation);
        }
        catch (JsonException exception)
        {
            PolicyValidationError error = new(
                exception.Path ?? "$", PolicyValidationCodes.InvalidJson, PolicyValidationSeverity.Error,
                new Dictionary<string, object?> { ["line"] = exception.LineNumber, ["byte"] = exception.BytePositionInLine });
            return new PolicyLoadResult(null, new PolicyValidationResult([error]));
        }
    }

    private static PolicyLoadResult InvalidJson() => new(null,
        new PolicyValidationResult([
            new PolicyValidationError("$", PolicyValidationCodes.InvalidJson,
                PolicyValidationSeverity.Error, new Dictionary<string, object?>())
        ]));

    internal static PolicyLoadResult DocumentTooLarge() => new(null,
        new PolicyValidationResult([
            new PolicyValidationError("$", PolicyValidationCodes.DocumentTooLarge,
                PolicyValidationSeverity.Error, new Dictionary<string, object?>())
        ]));

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            IndentSize = 4,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

public static class PolicyFileStore
{
    public static async ValueTask<PolicyLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (new FileInfo(path).Length > ProtectionPolicySerializer.MaximumDocumentBytes)
            return ProtectionPolicySerializer.DocumentTooLarge();
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return ProtectionPolicySerializer.Deserialize(json);
    }

    public static async ValueTask SaveAsync(
        ProtectionPolicyDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A parent directory is required.", nameof(path));
        Directory.CreateDirectory(directory);

        string json = ProtectionPolicySerializer.Serialize(document);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false)))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            PolicyLoadResult reloaded = await LoadAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!reloaded.IsSuccess || ProtectionPolicySerializer.Serialize(reloaded.Document!) != json)
                throw new IOException("The persisted policy did not pass round-trip verification.");
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporary(temporaryPath);
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
