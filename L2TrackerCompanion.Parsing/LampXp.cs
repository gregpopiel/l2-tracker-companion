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
            HasLampRows: !noLampRows,
            Red: read ? parsed.GetValueOrDefault("red") : null,
            Purple: read ? parsed.GetValueOrDefault("purple") : null,
            Blue: read ? parsed.GetValueOrDefault("blue") : null,
            Green: read ? parsed.GetValueOrDefault("green") : null);
    }

    /// <summary>
    /// The value the most sources agree on, falling back to
    /// <see cref="FirstParsed"/>'s precedence when nothing has more support
    /// than anything else.
    /// </summary>
    /// <remarks>
    /// Measured on the POC set: the first source in precedence order (the
    /// table cell crop) is the one that most often reads a row wrong, and it
    /// fails in a way nothing downstream can detect — a dropped <c>K</c>
    /// suffix turns <c>14M 400K</c> into a well-formed 14,000,400, so it is
    /// never null and never triggers a retry. In every such case at least two
    /// of the four sources still agreed on the right figure, which is what
    /// this resolves on. A tie keeps the old precedence, so a row where the
    /// sources merely disagree behaves exactly as before.
    /// </remarks>
    public static long? MostSupported(params long?[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var best = (long?)null;
        var bestVotes = 0;
        foreach (var candidate in candidates)
        {
            if (candidate is not { } value)
            {
                continue;
            }

            var votes = candidates.Count(other => other == value);
            if (votes > bestVotes)
            {
                best = value;
                bestVotes = votes;
            }
        }

        return bestVotes > 1 ? best : FirstParsed(candidates);
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
    bool HasLampRows,
    long? Red,
    long? Purple,
    long? Blue,
    long? Green);
