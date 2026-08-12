using System.Diagnostics;
using Drm.Domain;
using Drm.Platform.Abstractions;

namespace Drm.Platform.Local;

public sealed class LocalWorkspaceLocationResolver : IWorkspaceLocationResolver
{
    private readonly StringComparison _comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly string[] _applicationLocations;
    private readonly string[] _systemLocations;
    private readonly string[] _cloudLocations;
    private readonly string _temporaryLocation;

    public LocalWorkspaceLocationResolver(IEnumerable<string>? additionalForbiddenLocations = null)
    {
        IEnumerable<string> applicationLocations = new[]
        {
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        _applicationLocations = applicationLocations.Concat(additionalForbiddenLocations ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeUnchecked)
            .Distinct(GetComparer())
            .ToArray();
        _systemLocations = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeUnchecked)
            .Distinct(GetComparer())
            .ToArray();
        _temporaryLocation = NormalizeUnchecked(Path.GetTempPath());
        IEnumerable<string?> cloudLocations = OperatingSystem.IsWindows()
            ? new[] { Environment.GetEnvironmentVariable("OneDrive"), Environment.GetEnvironmentVariable("OneDriveCommercial"), Environment.GetEnvironmentVariable("OneDriveConsumer") }
            : OperatingSystem.IsMacOS()
                ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "CloudStorage") }
                : [];
        _cloudLocations = cloudLocations.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeUnchecked(path!))
            .Distinct(GetComparer())
            .ToArray();
    }

    public ValueTask<WorkspaceLocationResolution> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path)) return Denied(WorkspaceValidationCode.InvalidPath);

        string canonical;
        try { canonical = NormalizeUnchecked(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Denied(WorkspaceValidationCode.InvalidPath);
        }

        if (!Directory.Exists(canonical))
            return File.Exists(canonical)
                ? Denied(WorkspaceValidationCode.NotDirectory)
                : Denied(WorkspaceValidationCode.DoesNotExist);

        string? root = Path.GetPathRoot(canonical);
        if (root is not null && string.Equals(NormalizeUnchecked(root), canonical, _comparison))
            return Denied(WorkspaceValidationCode.FileSystemRootNotAllowed);

        if (OperatingSystem.IsWindows() && canonical.Length >= 2 && canonical[0] == '\\' && canonical[1] == '\\')
            return Denied(WorkspaceValidationCode.NetworkLocationNotSupported);

        try
        {
            FileAttributes attributes = File.GetAttributes(canonical);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return Denied(WorkspaceValidationCode.SymbolicLinkNotSupported);

            if (root is not null)
            {
                DriveType driveType = new DriveInfo(root).DriveType;
                if (driveType == DriveType.Network)
                    return Denied(WorkspaceValidationCode.NetworkLocationNotSupported);
                if (driveType == DriveType.Removable)
                    return Denied(WorkspaceValidationCode.RemovableLocationNotSupported);
                if (driveType != DriveType.Fixed)
                    return Denied(WorkspaceValidationCode.UnsupportedFileSystem);
            }

            if (IsSameOrAncestor(_temporaryLocation, canonical))
                return Denied(WorkspaceValidationCode.TemporaryLocationNotAllowed);
            if (_cloudLocations.Any(cloud => IsSameOrAncestor(cloud, canonical)))
                return Denied(WorkspaceValidationCode.CloudLocationNotSupported);
            if (_systemLocations.Any(forbidden => IsSameOrAncestor(forbidden, canonical)))
                return Denied(WorkspaceValidationCode.SystemLocationNotAllowed);
            if (_applicationLocations.Any(forbidden => IsSameOrAncestor(forbidden, canonical)))
                return Denied(WorkspaceValidationCode.ApplicationLocationNotAllowed);

            _ = Directory.EnumerateFileSystemEntries(canonical).Take(1).ToArray();
            string probePath = Path.Combine(canonical, $".drm-write-probe-{Guid.NewGuid():N}.tmp");
            using FileStream probe = new(probePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return Denied(WorkspaceValidationCode.AccessDenied);
        }

        WorkspaceLocation location = new(canonical, canonical);
        return ValueTask.FromResult(new WorkspaceLocationResolution(location, WorkspaceValidationResult.Allowed()));
    }

    public bool AreSame(WorkspaceLocation first, WorkspaceLocation second) =>
        string.Equals(first.CanonicalPath, second.CanonicalPath, _comparison);

    public bool IsAncestorOf(WorkspaceLocation parent, WorkspaceLocation child) =>
        IsSameOrAncestor(parent.CanonicalPath, child.CanonicalPath) && !AreSame(parent, child);

    private bool IsSameOrAncestor(string parent, string child)
    {
        if (string.Equals(parent, child, _comparison)) return true;
        string prefix = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, _comparison);
    }

    private static string NormalizeUnchecked(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static StringComparer GetComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static ValueTask<WorkspaceLocationResolution> Denied(WorkspaceValidationCode code) =>
        ValueTask.FromResult(new WorkspaceLocationResolution(null, WorkspaceValidationResult.Denied(code)));
}

public sealed class LocalWorkspacePathLauncher : IWorkspacePathLauncher
{
    public ValueTask OpenAsync(WorkspaceLocation location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(location.CanonicalPath))
            throw new DirectoryNotFoundException("The registered workspace directory is unavailable.");
        Process.Start(new ProcessStartInfo { FileName = location.CanonicalPath, UseShellExecute = true });
        return ValueTask.CompletedTask;
    }
}
