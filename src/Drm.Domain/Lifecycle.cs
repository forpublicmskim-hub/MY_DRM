namespace Drm.Domain;

public enum SessionState { Created, Opening, Active, Suspended, Closing, Closed, Revoked, Faulted }
public enum LicenseState { Valid, Expiring, Expired, Revoked }
public enum ConnectivityState { Online, Offline }

public static class SessionTransitions
{
    private static readonly Dictionary<SessionState, HashSet<SessionState>> Allowed =
        new()
        {
            [SessionState.Created] = Set(SessionState.Opening, SessionState.Closing),
            [SessionState.Opening] = Set(SessionState.Active, SessionState.Closing, SessionState.Faulted, SessionState.Revoked),
            [SessionState.Active] = Set(SessionState.Suspended, SessionState.Closing, SessionState.Revoked, SessionState.Faulted),
            [SessionState.Suspended] = Set(SessionState.Active, SessionState.Closing, SessionState.Revoked, SessionState.Faulted),
            [SessionState.Faulted] = Set(SessionState.Closing),
            [SessionState.Revoked] = Set(SessionState.Closing),
            [SessionState.Closing] = Set(SessionState.Closed),
            [SessionState.Closed] = Set()
        };

    public static bool CanMove(SessionState from, SessionState to) => Allowed[from].Contains(to);
    private static HashSet<SessionState> Set(params SessionState[] states) => states.ToHashSet();
}
