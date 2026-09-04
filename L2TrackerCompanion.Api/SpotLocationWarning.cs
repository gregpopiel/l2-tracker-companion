namespace L2TrackerCompanion.Api;

/// <summary>
/// Whether the spot a session is set to save to still matches where the
/// player is currently standing.
/// </summary>
/// <remarks>
/// <see cref="SpotResolve"/> only auto-resolves a spot when the picker is
/// empty — once a spot is selected (manually, or auto-picked earlier in the
/// session) it always wins, by design, and nothing downstream re-checks it
/// against a later location change. That is correct for <em>which spot to
/// save to</em>, but it means a session that quietly walked from one spot to
/// another produces no signal at all. This is that signal: purely a warning
/// text for the status line, same spirit as <see cref="LocationChangeWatch"/>
/// in <c>L2TrackerCompanion.Session</c> — it never blocks a save, since the
/// Play Report spans the whole session and attributing it to one spot or the
/// other is the player's call.
/// </remarks>
public static class SpotLocationWarning
{
    /// <param name="selected">The spot the save would attach to, or null.</param>
    /// <param name="stableCanonicalName">
    /// The current settled location's canonical name (from
    /// <c>LocationStability</c>), or null/blank while unsettled — an
    /// unsettled read is not evidence of a move.
    /// </param>
    /// <returns>A warning to show, or null when there is nothing to say.</returns>
    public static string? Evaluate(SpotInfo? selected, string? stableCanonicalName)
    {
        if (selected is null || string.IsNullOrWhiteSpace(selected.Name))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(stableCanonicalName))
        {
            return null;
        }

        if (SpotMatch.SameName(selected.Name, stableCanonicalName))
        {
            return null;
        }

        return $"Location now reads \"{stableCanonicalName.Trim()}\", "
            + $"but this session will save to \"{selected.Name}\" — check you're saving the right spot.";
    }
}
