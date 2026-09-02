namespace L2TrackerCompanion.Parsing;

/// <summary>
/// All-or-none lamp XP plus the sum-vs-dialog-XP gate. Does not OCR counts
/// or multiply by <c>LAMP_XP_PER_UNIT</c> / settings values.
/// </summary>
public static class LampXp
{
    public static LampXpDecision Decide(
        IReadOnlyDictionary<string, long?> parsed,
        IReadOnlyDictionary<string, WordBox> dialogRows,
        long? dialogXp,
        long? dialogAdena)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(dialogRows);

        var noLampRows = LampGeometry.Colors.All(color => !dialogRows.ContainsKey(color));
        var closed = noLampRows && dialogXp is not null && dialogAdena is not null;

        var allParsed = LampGeometry.Colors.All(color => parsed.GetValueOrDefault(color) is not null);
        var total = allParsed ? LampGeometry.Colors.Sum(color => parsed[color]!.Value) : 0L;
        var exceeds = allParsed && dialogXp is not null && total > dialogXp.Value;
        var read = allParsed && !exceeds;

        return new LampXpDecision(
            LampXpRead: read,
            LampPanelClosed: closed,
            ExceedsDialogXp: exceeds,
            LampXpTotal: total,
            Red: read ? parsed.GetValueOrDefault("red") : null,
            Purple: read ? parsed.GetValueOrDefault("purple") : null,
            Blue: read ? parsed.GetValueOrDefault("blue") : null,
            Green: read ? parsed.GetValueOrDefault("green") : null);
    }

    /// <summary>
    /// First parseable source wins, in the order the browser tries: table
    /// cell crop, table tokens, dialog tokens, dialog cell crop.
    /// </summary>
    public static long? FirstParsed(params long?[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var value in candidates)
        {
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}

public sealed record LampXpDecision(
    bool LampXpRead,
    bool LampPanelClosed,
    bool ExceedsDialogXp,
    long LampXpTotal,
    long? Red,
    long? Purple,
    long? Blue,
    long? Green);
