using System.Text.Json;
using Drm.Application;
using Drm.Domain;

namespace Drm.Infrastructure;

public sealed class JsonWorkspaceRegistry(string registryPath) : IWorkspaceRegistry
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _registryPath = Path.GetFullPath(registryPath);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IReadOnlyList<ProtectedWorkspace>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask AddAsync(ProtectedWorkspace workspace, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProtectedWorkspace> workspaces = [.. await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false)];
            if (workspaces.Any(item => item.Id == workspace.Id))
                throw new WorkspaceRegistryException("동일한 WorkspaceId가 이미 존재합니다.");
            workspaces.Add(workspace);
            await WriteUnsafeAsync(workspaces, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> RemoveAsync(WorkspaceId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProtectedWorkspace> workspaces = [.. await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false)];
            int removed = workspaces.RemoveAll(item => item.Id == id);
            if (removed == 0) return false;
            await WriteUnsafeAsync(workspaces, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<IReadOnlyList<ProtectedWorkspace>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath)) return [];
        try
        {
            await using FileStream stream = new(_registryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            RegistryDocument? document = await JsonSerializer.DeserializeAsync<RegistryDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (document is null || document.SchemaVersion != CurrentSchemaVersion || document.Workspaces is null)
                throw new WorkspaceRegistryCorruptedException("지원하지 않거나 손상된 작업공간 설정입니다.");
            return document.Workspaces.Select(ToDomain).ToArray();
        }
        catch (WorkspaceRegistryCorruptedException) { throw; }
        catch (JsonException exception)
        {
            throw new WorkspaceRegistryCorruptedException("작업공간 설정 파일을 해석할 수 없습니다.", exception);
        }
        catch (IOException exception)
        {
            throw new WorkspaceRegistryException("작업공간 설정 파일을 읽지 못했습니다.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new WorkspaceRegistryException("작업공간 설정 파일에 접근할 수 없습니다.", exception);
        }
    }

    private async ValueTask WriteUnsafeAsync(IReadOnlyList<ProtectedWorkspace> workspaces, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_registryPath);
        if (string.IsNullOrEmpty(directory)) throw new WorkspaceRegistryException("Registry 경로에 상위 폴더가 없습니다.");
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_registryPath)}.{Guid.NewGuid():N}.tmp");
        RegistryDocument document = new(CurrentSchemaVersion, workspaces.Select(ToDto).ToArray());

        try
        {
            Directory.CreateDirectory(directory);
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _registryPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceRegistryException("작업공간 설정 파일을 저장하지 못했습니다.", exception);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception) { }
        }
    }

    private static ProtectedWorkspace ToDomain(WorkspaceDto item) => new(
        new WorkspaceId(item.Id), item.DisplayName,
        new WorkspaceLocation(item.DisplayPath, item.CanonicalPath, item.PlatformIdentity, item.PersistentAccessReference),
        WorkspaceRegistrationState.Registered, WorkspaceProtectionState.NotActivated, item.RegisteredAt);

    private static WorkspaceDto ToDto(ProtectedWorkspace item) => new(item.Id.Value, item.DisplayName,
        item.Location.DisplayPath, item.Location.CanonicalPath, item.Location.PlatformIdentity,
        item.Location.PersistentAccessReference, item.RegisteredAt);

    private sealed record RegistryDocument(int SchemaVersion, WorkspaceDto[] Workspaces);
    private sealed record WorkspaceDto(Guid Id, string DisplayName, string DisplayPath, string CanonicalPath,
        string? PlatformIdentity, string? PersistentAccessReference, DateTimeOffset RegisteredAt);

    public void Dispose() => _gate.Dispose();
}
