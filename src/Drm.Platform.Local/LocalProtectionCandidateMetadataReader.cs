using System.Security;
using Drm.Application;
using Drm.Domain;
using Drm.Policy;

namespace Drm.Platform.Local;

public sealed class LocalProtectionCandidateMetadataReader : IProtectionCandidateMetadataReader
{
    public ValueTask<ProtectionCandidateMetadataResult> ReadAsync(
        ProtectedWorkspace workspace, string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return Result(ProtectionCandidateMetadataStatus.UnsafePath);

        string root;
        string fullPath;
        string safeRelative;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.Location.CanonicalPath));
            fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            safeRelative = WorkspacePathSafety.GetSafeRelativePath(root, fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(ProtectionCandidateMetadataStatus.UnsafePath);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ContainsReparsePoint(root, safeRelative, cancellationToken))
                return Result(ProtectionCandidateMetadataStatus.SymbolicLinkNotSupported);

            FileAttributes attributes = File.GetAttributes(fullPath);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            long? length = isDirectory ? null : new FileInfo(fullPath).Length;
            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);
            DateTimeOffset lastWrite = new(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc));
            string extension = PolicyNormalizer.NormalizeExtension(Path.GetExtension(fullPath));
            FileVersionStamp version = new(length, lastWrite);
            ProtectionCandidateMetadata metadata = new(safeRelative, extension, isDirectory, length, version);
            return ValueTask.FromResult(ProtectionCandidateMetadataResult.Available(metadata));
        }
        catch (FileNotFoundException) { return Result(ProtectionCandidateMetadataStatus.NotFound); }
        catch (DirectoryNotFoundException) { return Result(ProtectionCandidateMetadataStatus.NotFound); }
        catch (UnauthorizedAccessException) { return Result(ProtectionCandidateMetadataStatus.AccessDenied); }
        catch (SecurityException) { return Result(ProtectionCandidateMetadataStatus.AccessDenied); }
        catch (IOException) { return Result(ProtectionCandidateMetadataStatus.Unavailable); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(ProtectionCandidateMetadataStatus.UnsafePath);
        }
    }

    private static bool ContainsReparsePoint(string root, string safeRelative, CancellationToken cancellationToken)
    {
        string current = root;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        foreach (string segment in safeRelative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static ValueTask<ProtectionCandidateMetadataResult> Result(ProtectionCandidateMetadataStatus status) =>
        ValueTask.FromResult(ProtectionCandidateMetadataResult.Failure(status));
}
