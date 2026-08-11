using Drm.Domain;

namespace Drm.Application.Tests;

public sealed class SessionTransitionsTests
{
    [Theory]
    [InlineData(SessionState.Created, SessionState.Opening)]
    [InlineData(SessionState.Opening, SessionState.Active)]
    [InlineData(SessionState.Active, SessionState.Suspended)]
    [InlineData(SessionState.Active, SessionState.Closing)]
    [InlineData(SessionState.Closing, SessionState.Closed)]
    public void ExpectedTransitionIsAllowed(SessionState from, SessionState to) =>
        Assert.True(SessionTransitions.CanMove(from, to));

    [Theory]
    [InlineData(SessionState.Created, SessionState.Active)]
    [InlineData(SessionState.Closed, SessionState.Active)]
    [InlineData(SessionState.Revoked, SessionState.Active)]
    public void UnsafeTransitionIsDenied(SessionState from, SessionState to) =>
        Assert.False(SessionTransitions.CanMove(from, to));
}
