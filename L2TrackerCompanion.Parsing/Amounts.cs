namespace L2TrackerCompanion.Parsing;

/// <summary>
/// FarmLog amounts are stored — and the API expects them — in thousands.
/// The Play Report prints the real figure; divide before POST (and before
/// computing session deltas), not just before display.
/// </summary>
public static class Amounts
{
    /// <summary>
    /// Round to the nearest whole thousand and return that count of thousands
    /// (the same <c>Math.round(raw / 1000)</c> the browser import uses).
    /// </summary>
    public static long ToThousands(long rawValue) =>
        (long)Math.Round(rawValue / 1000.0, MidpointRounding.AwayFromZero);
}
