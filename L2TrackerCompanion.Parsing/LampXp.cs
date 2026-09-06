namespace L2TrackerCompanion.Parsing;

/// <summary>
/// All-or-none lamp XP plus the sum-vs-dialog-XP gate. Does not OCR counts
/// or multiply by <c>LAMP_XP_PER_UNIT</c> / settings values.
/// </summary>
public static class LampXp
{
    /// <summary>
    /// Deliberately no 1,000: lamp rows are printed as M and K groups only,
    /// so a figure never loses just a units group. Including it would instead
    /// let a lost-K misread that spilled its digits into the units (17M 888K
    /// read as 17,000,888) swallow the round readings it sits beside.
    /// </summary>
    private static readonly long[] MagnitudeScales = [1_000_000, 1_000_000_000];

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
    /// The value the most sources agree on, counting a truncated reading as
    /// support for the fuller one it came from, and falling back to
    /// <see cref="FirstParsed"/>'s precedence when nothing has more support
    /// than anything else.
    /// </summary>
    /// <remarks>
    /// Measured on the POC set: the first source in precedence order (the
    /// table cell crop) is the one that most often reads a row wrong, and it
    /// fails in a way nothing downstream can detect — a dropped <c>K</c>
    /// suffix turns <c>14M 400K</c> into a well-formed 14,000,400, so it is
    /// never null and never triggers a retry.
    /// <para>
    /// Agreement alone is not enough, because two sources routinely lose the
    /// same <c>K</c> group and then out-vote the one source that read it —
    /// live on 2026-09-06 that stored Green as 36,000,000 against the game's
    /// own <c>36M 608K</c>. A truncated figure is not an arbitrary wrong
    /// number though (see <see cref="IsTruncationOf"/>), so it is promoted to
    /// the fuller reading before the vote and reinforces it instead.
    /// </para>
    /// </remarks>
    public static long? MostSupported(params long?[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // A truncated reading should reinforce the precise one it came from
        // rather than compete with it.
        var resolved = candidates
            .Select(candidate => candidate is { } value
                ? candidates.Where(other => other is { } precise && IsTruncationOf(value, precise)).Max() ?? value
                : candidate)
            .ToArray();

        var best = (long?)null;
        var bestVotes = 0;
        foreach (var candidate in resolved)
        {
            if (candidate is not { } value)
            {
                continue;
            }

            var votes = resolved.Count(other => other == value);
            if (votes > bestVotes)
            {
                best = value;
                bestVotes = votes;
            }
        }

        return bestVotes > 1 ? best : FirstParsed(resolved);
    }

    /// <summary>
    /// Is this the same figure with one or more trailing magnitude groups
    /// lost? WinOCR drops the <c>K</c> group, so <c>36M 608K</c> comes back
    /// as <c>36M</c> — 36,000,000 is 36,608,000 with its thousands zeroed.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than "prefer the larger figure": a misread
    /// leading digit (<c>7M 776K</c> read as <c>9M 776K</c>) is also larger,
    /// but 9,776,000 is not 7,776,000 with a group zeroed, so it is left to
    /// the vote. Zero is excluded because it is arithmetically a truncation
    /// of every figure below the next magnitude, and an empty lamp row is a
    /// real reading rather than a degraded one.
    /// <para>
    /// The target has to be a whole number of thousands. Lamp rows are
    /// printed as M and K groups, so a real figure always is — while the
    /// same lost-<c>K</c> failure can spill digits into the units instead
    /// (<c>17M 888K</c> read as 17,000,888). Without this, the round
    /// readings beside such a misread count as truncations of it and hand
    /// it the vote.
    /// </para>
    /// </remarks>
    private static bool IsTruncationOf(long value, long precise)
        => value > 0
            && value < precise
            && precise % 1_000 == 0
            && MagnitudeScales.Any(scale => precise / scale * scale == value);

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
