namespace L2TrackerCompanion.Session;

/// <summary>
/// Notices that the player has settled at a different location than the one
/// they were at, so the app can remind them to restart the in-game Play Report.
/// </summary>
/// <remarks>
/// Purely a reminder — nothing here blocks a save or hides a figure. The Play
/// Report counts from login, so a session spanning two spots is attributed to
/// whichever one is picked at save time; keeping that correct is the player's
/// call, and this is the nudge.
/// <para>
/// Only settled locations count. Feeding it the raw minimap hint would fire on
/// every OCR wobble, so callers pass the canonical name from
/// <c>LocationStability</c> and null while the window is still unsettled — an
/// unsettled stretch is not a move, it is a gap, and the next settled name is
/// only news if it differs from the last one.
/// </para>
/// </remarks>
public sealed class LocationChangeWatch
{
    /// <summary>The last settled location, or null before the first one.</summary>
    public string? Current { get; private set; }

    /// <param name="stableName">
    /// Canonical name of a settled location, or null/blank while the location
    /// window is unsettled.
    /// </param>
    /// <returns>A message to show, exactly once per observed move; else null.</returns>
    public string? Notice(string? stableName)
    {
        if (string.IsNullOrWhiteSpace(stableName))
        {
            return null;
        }

        var name = stableName.Trim();

        // First sighting is where the player already was, not a move.
        if (Current is null)
        {
            Current = name;
            return null;
        }

        if (string.Equals(Current, name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Current = name;
        return $"Location changed to {name} — restart the in-game Play Report if you moved spots.";
    }

    /// <summary>
    /// Forget where the player was. For a restarted game client, where the next
    /// location is a first sighting again rather than a move.
    /// </summary>
    public void Reset() => Current = null;
}
