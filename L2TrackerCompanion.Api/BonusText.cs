using System.Globalization;

namespace L2TrackerCompanion.Api;

/// <summary>
/// % Bonus is a percentage the user types. Try invariant first (dot decimal)
/// so a Polish locale still accepts "25.5", then current culture for "25,5".
/// </summary>
public static class BonusText
{
    public static bool TryParse(string? text, out double bonus)
    {
        bonus = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        return TryParseWith(trimmed, CultureInfo.InvariantCulture, out bonus)
            || TryParseWith(trimmed, CultureInfo.CurrentCulture, out bonus);
    }

    private static bool TryParseWith(string text, CultureInfo culture, out double bonus)
        => double.TryParse(text, NumberStyles.Float, culture, out bonus) && bonus >= 0;
}
