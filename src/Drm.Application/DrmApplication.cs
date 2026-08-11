using System.Collections.Concurrent;

namespace Drm.Application;

public sealed class DrmApplication(Func<DrmSession> sessionFactory) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, DrmSession> _sessions = new();

    public DrmSession CreateSession()
    {
        DrmSession session = sessionFactory();
        if (!_sessions.TryAdd(session.Id, session)) throw new InvalidOperationException("Duplicate session id.");
        return session;
    }

    public bool TryGetSession(Guid id, out DrmSession? session) => _sessions.TryGetValue(id, out session);

    public async ValueTask CloseSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(id, out DrmSession? session)) await session.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        DrmSession[] sessions = _sessions.Values.ToArray();
        _sessions.Clear();
        foreach (DrmSession session in sessions) await session.DisposeAsync().ConfigureAwait(false);
    }
}
