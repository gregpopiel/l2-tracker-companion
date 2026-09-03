namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Does an already-posted log still cover the frame in front of us?
/// </summary>
/// <remarks>
/// The Play Report is cumulative, so a later frame of the same run contains
/// every minute already sent — posting it again would double-count them.
///
/// Release keys on <em>XP</em>, not on play time: XP never decreases within a
/// run, so XP below the saved figure is proof that this is a different run,
/// whether we catch it in its third minute or its three-hundredth. Keying on
/// play time instead would have locked out any new session that had already
/// grown longer than the saved one before anyone looked at it. Play time is
/// only the fallback for the degenerate case of a log saved at zero XP.
/// </remarks>
public static class SaveLock
{
    public static bool Covers(long savedMinutes, long? savedXp, PlayReport current)
    {
        ArgumentNullException.ThrowIfNull(current);

        // An unreadable frame is no evidence of a reset, so stay locked.
        if (current.Minutes is null || current.Xp is null)
        {
            return true;
        }

        if (savedXp is > 0)
        {
            return current.Xp.Value >= savedXp.Value;
        }

        return current.Minutes.Value >= savedMinutes;
    }
}
