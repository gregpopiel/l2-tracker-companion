using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Whether the spot a session is set to save to still matches where the
/// player is currently standing.
/// </summary>
/// <remarks>
/// <see cref="SpotResolve"/> only auto-resolves a spot when the picker is
/// empty, and a selected spot wins there. <see cref="SpotAutoCorrect"/> does
/// re-check the picker once against a hint that exactly names an owned spot,
/// but after the player answers that correction their pick is pinned and
/// nothing re-checks it again. That is correct for <em>which spot to save
/// to</em>, but it means a session that quietly walked from one spot to another
/// produces no signal at all. This is that signal: purely a warning for
/// the location banner, same spirit as <see cref="LocationChangeWatch"/> in
/// <c>L2TrackerCompanion.Session</c> — it never blocks a save, since the Play
/// Report spans the whole session and attributing it to one spot or the other
/// is the player's call. Matching is fuzzy (<see cref="LocationName.SamePlace"/>),
/// the same rule the move reminder uses, so OCR garble of the pinned spot
/// is not reported as a walk to somewhere else.
/// </remarks>
public static class SpotLocationWarning
{
    /// <param name="selected">The spot the save would attach to, or null.</param>
    /// <param name="stableCanonicalName">
    /// The location this read is evidence for — see
    /// <see cref="SpotResolve.DetectedName"/>: a hint that exactly names an
    /// owned spot, else the settled canonical name, else null. Callers must not
    /// pass the settled name alone: where minimap OCR never repeats a spelling
    /// nothing ever settles, and this would then stay silent exactly when a
    /// pinned pick has drifted from where the player is standing.
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

        // Same fuzzy rule LocationChangeWatch uses for a move: exact equality
        // treated OCR garble (SelM@hum / prägon Villey) as a different spot
        // and raised this warning for a player who had not moved.
        if (LocationName.SamePlace(selected.Name, stableCanonicalName))
        {
            return null;
        }

        return $"Location now reads \"{stableCanonicalName.Trim()}\", "
            + $"but this session will save to \"{selected.Name}\" — check you're saving the right spot.";
    }
}
