using Drm.Domain;

namespace Drm.Application;

public sealed class DrmSession(OpenPipeline openPipeline, IAuditSink audit, IClock clock) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private IProtectedContentSession? _protectedSession;
    private long _generation;
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public SessionState State { get; private set; } = SessionState.Created;
    public VerifiedLicense? License { get; private set; }

    public async ValueTask OpenAsync(OpenSessionRequest request, CancellationToken cancellationToken = default)
    {
        long operationGeneration;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            MoveTo(SessionState.Opening);
            operationGeneration = ++_generation;
        }
        finally { _gate.Release(); }

        OpenSessionResult result;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            result = await openPipeline.ExecuteAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch
        {
            await MarkOpenFailureAsync(operationGeneration).ConfigureAwait(false);
            throw;
        }

        bool staleResult;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            staleResult = State != SessionState.Opening || operationGeneration != _generation;
            if (!staleResult)
            {
                License = result.License;
                _protectedSession = result.ProtectedSession;
                MoveTo(SessionState.Active);
            }
        }
        finally { _gate.Release(); }

        if (staleResult)
        {
            await result.ProtectedSession.DisposeAsync().ConfigureAwait(false);
            throw new OperationCanceledException("The session changed while it was opening.");
        }

        await AuditAsync("session.opened", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        IProtectedContentSession? resource;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is SessionState.Closed or SessionState.Closing) return;
            MoveTo(SessionState.Closing);
            ++_generation;
            _lifetime.Cancel();
            resource = _protectedSession;
            _protectedSession = null;
            License = null;
        }
        finally { _gate.Release(); }

        Exception? cleanupFailure = null;
        try { if (resource is not null) await resource.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { cleanupFailure = exception; }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try { MoveTo(SessionState.Closed); }
        finally { _gate.Release(); }

        await AuditAsync("session.closed", CancellationToken.None).ConfigureAwait(false);
        if (cleanupFailure is not null) throw new AggregateException("Protected resource cleanup failed.", cleanupFailure);
    }

    private async ValueTask MarkOpenFailureAsync(long operationGeneration)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try { if (State == SessionState.Opening && operationGeneration == _generation) MoveTo(SessionState.Faulted); }
        finally { _gate.Release(); }
    }

    private void MoveTo(SessionState target)
    {
        if (!SessionTransitions.CanMove(State, target))
            throw new InvalidOperationException($"Transition {State} -> {target} is not allowed.");
        State = target;
    }

    private ValueTask AuditAsync(string name, CancellationToken token) => audit.WriteAsync(new AuditEvent(Id, name, clock.UtcNow), token);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
