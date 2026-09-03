namespace L2TrackerCompanion.Session;

/// <summary>
/// Decides when the game client has been restarted, which is proof that the
/// Play Report is counting from zero again.
/// </summary>
/// <remarks>
/// A changed process id is <em>not</em> that proof. The capture service follows
/// whichever client is in front, so with two clients open the id flips on every
/// alt-tab, and it also flips when one of two clients is closed. The only sound
/// signal is having observed no game window at all — the client we were
/// following actually exited — and then seeing one come back under a different
/// id. Pure logic so it can be tested without Windows.
/// </remarks>
public sealed class GameProcessWatch
{
    private bool _wentAway;

    /// <summary>The process the app is currently following, if any.</summary>
    public int? FollowedProcessId { get; private set; }

    /// <param name="processId">
    /// Process behind the game window now in view, or <c>null</c> when no game
    /// window could be found.
    /// </param>
    /// <param name="followedProcessAlive">
    /// Whether <see cref="FollowedProcessId"/> is still running. Only consulted
    /// when <paramref name="processId"/> is <c>null</c>, to tell a client that
    /// exited from one that merely has no usable window for a moment.
    /// </param>
    /// <returns><c>true</c> exactly once per observed restart.</returns>
    public bool Notice(int? processId, bool followedProcessAlive)
    {
        if (processId is null)
        {
            if (FollowedProcessId is not null && !followedProcessAlive)
            {
                _wentAway = true;
            }

            return false;
        }

        if (FollowedProcessId == processId)
        {
            return false;
        }

        var hadPrevious = FollowedProcessId is not null;
        FollowedProcessId = processId;

        // Nothing to compare against on the first sighting; and a different id
        // without a gap is another client taking the foreground, not a restart.
        if (!hadPrevious || !_wentAway)
        {
            return false;
        }

        _wentAway = false;
        return true;
    }
}
