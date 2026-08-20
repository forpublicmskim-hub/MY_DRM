using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Drm.Application;
using Drm.Domain;

namespace Drm.Platform.Local;

public sealed class LocalWorkspaceScanner : IWorkspaceScanner
{
    public async IAsyncEnumerable<string> ScanAsync(
        ProtectedWorkspace workspace,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string root = workspace.Location.CanonicalPath;
        Stack<string> pending = new();
        pending.Push(root);

        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { continue; }

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { continue; }

                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                string relativePath = WorkspacePathSafety.GetSafeRelativePath(root, entry);
                yield return relativePath;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                await Task.Yield();
            }
        }
    }
}
public sealed class FileSystemWatcherWorkspaceMonitorFactory(
    IWorkspaceScanner scanner,
    TimeProvider? timeProvider = null) : IWorkspaceMonitorFactory
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public IWorkspaceMonitor Create(ProtectedWorkspace workspace) =>
        new FileSystemWatcherWorkspaceMonitor(workspace, scanner, _timeProvider);
}

public sealed class FileSystemWatcherWorkspaceMonitor : IWorkspaceMonitor
{
    private const int QueueCapacity = 2048;
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(150);
    private readonly ProtectedWorkspace _workspace;
    private readonly IWorkspaceScanner _scanner;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _reconciliation = new(1, 1);
    private readonly Channel<RawFileSystemEvent> _rawEvents = Channel.CreateBounded<RawFileSystemEvent>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly Channel<WorkspaceMonitorEvent> _events = Channel.CreateBounded<WorkspaceMonitorEvent>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly HashSet<string> _knownPaths = new(GetPathComparer());
    private CancellationTokenSource? _lifetime;
    private FileSystemWatcher? _watcher;
    private Task? _worker;
    private int _state = (int)WorkspaceMonitorState.Stopped;
    private int _reconciliationRequested;
    private bool _disposed;

    public WorkspaceId WorkspaceId => _workspace.Id;
    public WorkspaceMonitorState State => (WorkspaceMonitorState)Volatile.Read(ref _state);

    public FileSystemWatcherWorkspaceMonitor(
        ProtectedWorkspace workspace,
        IWorkspaceScanner scanner,
        TimeProvider timeProvider)
    {
        _workspace = workspace;
        _scanner = scanner;
        _timeProvider = timeProvider;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (State is WorkspaceMonitorState.Starting or WorkspaceMonitorState.Watching or WorkspaceMonitorState.Rescanning)
                return;
            if (State != WorkspaceMonitorState.Stopped)
                throw new InvalidOperationException($"Monitor cannot start from {State}.");

            SetState(WorkspaceMonitorState.Starting);
            _lifetime = new CancellationTokenSource();
            _watcher = CreateWatcher(_workspace.Location.CanonicalPath);
            Subscribe(_watcher);
            _watcher.EnableRaisingEvents = true;

            try
            {
                await ReconcileSnapshotAsync(initialScan: true, cancellationToken).ConfigureAwait(false);
                _worker = ProcessAsync(_lifetime.Token);
                SetState(WorkspaceMonitorState.Watching);
            }
            catch
            {
                SetState(WorkspaceMonitorState.Faulted);
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    public async IAsyncEnumerable<WorkspaceMonitorEvent> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (WorkspaceMonitorEvent item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    private static FileSystemWatcher CreateWatcher(string root) => new(root)
    {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
            NotifyFilters.LastWrite | NotifyFilters.Size,
        InternalBufferSize = 32 * 1024,
        Filter = "*"
    };

    private void Subscribe(FileSystemWatcher watcher)
    {
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
    }

    private void Unsubscribe(FileSystemWatcher watcher)
    {
        watcher.Created -= OnChanged;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
    }

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        Enqueue(new RawFileSystemEvent(args.FullPath, null, args.ChangeType));

    private void OnRenamed(object sender, RenamedEventArgs args) =>
        Enqueue(new RawFileSystemEvent(args.FullPath, args.OldFullPath, WatcherChangeTypes.Renamed));

    private void OnError(object sender, ErrorEventArgs args) => RequestReconciliation();

    private void Enqueue(RawFileSystemEvent item)
    {
        if (!_rawEvents.Writer.TryWrite(item)) RequestReconciliation();
    }

    private void RequestReconciliation()
    {
        Interlocked.Exchange(ref _reconciliationRequested, 1);
        SetState(WorkspaceMonitorState.Degraded);
        _rawEvents.Writer.TryWrite(RawFileSystemEvent.Reconcile);
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _rawEvents.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                List<RawFileSystemEvent> batch = [];
                while (_rawEvents.Reader.TryRead(out RawFileSystemEvent? item))
                    if (item is not null) batch.Add(item);
                await Task.Delay(DebounceInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                while (_rawEvents.Reader.TryRead(out RawFileSystemEvent? item))
                    if (item is not null) batch.Add(item);

                if (Interlocked.Exchange(ref _reconciliationRequested, 0) != 0 || batch.Any(item => item.IsReconciliation))
                {
                    await ReconcileSnapshotAsync(initialScan: false, cancellationToken).ConfigureAwait(false);
                    SetState(WorkspaceMonitorState.Watching);
                    continue;
                }

                ProcessBatch(batch);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            SetState(WorkspaceMonitorState.Faulted);
        }
    }

    private void ProcessBatch(IReadOnlyList<RawFileSystemEvent> batch)
    {
        Dictionary<string, RawFileSystemEvent> latest = new(GetPathComparer());
        foreach (RawFileSystemEvent item in batch)
        {
            if (item.ChangeType == WatcherChangeTypes.Renamed)
            {
                ProcessRename(item);
                continue;
            }

            if (!TryGetRelativePath(item.FullPath, out string? relativePath)) continue;
            latest[relativePath] = item;
        }

        foreach ((string relativePath, RawFileSystemEvent item) in latest)
        {
            bool existed = _knownPaths.Contains(relativePath);
            bool existsNow = IsObservableEntry(item.FullPath);
            if (existsNow)
            {
                _knownPaths.Add(relativePath);
                PublishObservation(existed ? WorkspaceObservationKind.Modified : WorkspaceObservationKind.Created,
                    relativePath, null);
            }
            else if (existed)
            {
                RemoveKnownPathAndDescendants(relativePath);
                PublishObservation(WorkspaceObservationKind.Deleted, relativePath, null);
            }
        }
    }

    private void ProcessRename(RawFileSystemEvent item)
    {
        string previousRelativePath = string.Empty;
        string newRelativePath = string.Empty;
        bool oldInside = item.PreviousFullPath is not null &&
            TryGetRelativePath(item.PreviousFullPath, out previousRelativePath);
        bool newInside = TryGetRelativePath(item.FullPath, out newRelativePath) &&
            IsObservableEntry(item.FullPath);

        if (oldInside) RemoveKnownPathAndDescendants(previousRelativePath!);
        if (newInside) _knownPaths.Add(newRelativePath!);

        if (oldInside && newInside)
            PublishObservation(WorkspaceObservationKind.Renamed, newRelativePath!, previousRelativePath);
        else if (oldInside)
            PublishObservation(WorkspaceObservationKind.Deleted, previousRelativePath!, null);
        else if (newInside)
            PublishObservation(WorkspaceObservationKind.Created, newRelativePath!, null);
    }

    private void RemoveKnownPathAndDescendants(string relativePath)
    {
        _knownPaths.Remove(relativePath);
        string prefix = relativePath + Path.DirectorySeparatorChar;
        foreach (string child in _knownPaths
                     .Where(path => path.StartsWith(prefix, GetPathComparison()))
                     .ToArray())
            _knownPaths.Remove(child);
    }

    private async ValueTask ReconcileSnapshotAsync(bool initialScan, CancellationToken cancellationToken)
    {
        await _reconciliation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!initialScan)
            {
                SetState(WorkspaceMonitorState.Rescanning);
                Publish(new WorkspaceMonitorEvent(WorkspaceId,
                    WorkspaceMonitorEventKind.ReconciliationRequired, State));
            }

            HashSet<string> scanned = new(GetPathComparer());
            await foreach (string path in _scanner.ScanAsync(_workspace, cancellationToken).ConfigureAwait(false))
            {
                scanned.Add(path);
                if (initialScan)
                    PublishObservation(WorkspaceObservationKind.Existing, path, null);
                else if (!_knownPaths.Contains(path))
                    PublishObservation(WorkspaceObservationKind.Created, path, null);
            }

            if (!initialScan)
            {
                foreach (string removed in _knownPaths.Except(scanned, GetPathComparer()).ToArray())
                    PublishObservation(WorkspaceObservationKind.Deleted, removed, null);
            }

            _knownPaths.Clear();
            _knownPaths.UnionWith(scanned);
        }
        finally { _reconciliation.Release(); }
    }

    private static bool IsObservableEntry(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) return false;
            return (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return false; }
    }

    private bool TryGetRelativePath(string fullPath, out string relativePath)
    {
        try
        {
            relativePath = WorkspacePathSafety.GetSafeRelativePath(_workspace.Location.CanonicalPath, fullPath);
            return true;
        }
        catch (ArgumentException)
        {
            relativePath = string.Empty;
            return false;
        }
    }

    private void PublishObservation(
        WorkspaceObservationKind kind,
        string relativePath,
        string? previousRelativePath)
    {
        WorkspaceObservation observation = new(
            WorkspaceId, kind, relativePath, previousRelativePath, _timeProvider.GetUtcNow());
        Publish(new WorkspaceMonitorEvent(WorkspaceId,
            WorkspaceMonitorEventKind.Observation, State, observation));
    }

    private void SetState(WorkspaceMonitorState state)
    {
        WorkspaceMonitorState previous = (WorkspaceMonitorState)Interlocked.Exchange(ref _state, (int)state);
        if (previous != state)
            Publish(new WorkspaceMonitorEvent(WorkspaceId, WorkspaceMonitorEventKind.StateChanged, state));
    }

    private void Publish(WorkspaceMonitorEvent item)
    {
        if (_events.Writer.TryWrite(item)) return;

        Interlocked.Exchange(ref _state, (int)WorkspaceMonitorState.Degraded);
        Interlocked.Exchange(ref _reconciliationRequested, 1);
        _rawEvents.Writer.TryWrite(RawFileSystemEvent.Reconcile);
    }

    private async ValueTask StopCoreAsync(CancellationToken cancellationToken)
    {
        if (State == WorkspaceMonitorState.Stopped) return;
        SetState(WorkspaceMonitorState.Stopping);
        FileSystemWatcher? watcher = _watcher;
        _watcher = null;
        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            Unsubscribe(watcher);
            watcher.Dispose();
        }

        CancellationTokenSource? lifetime = _lifetime;
        _lifetime = null;
        lifetime?.Cancel();
        if (_worker is not null)
        {
            try { await _worker.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime?.IsCancellationRequested == true) { }
            _worker = null;
        }
        lifetime?.Dispose();
        _knownPaths.Clear();
        SetState(WorkspaceMonitorState.Stopped);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _rawEvents.Writer.TryComplete();
        _events.Writer.TryComplete();
        _reconciliation.Dispose();
        _lifecycle.Dispose();
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record RawFileSystemEvent(
        string FullPath,
        string? PreviousFullPath,
        WatcherChangeTypes ChangeType,
        bool IsReconciliation = false)
    {
        public static RawFileSystemEvent Reconcile { get; } = new(string.Empty, null, 0, true);
    }
}
