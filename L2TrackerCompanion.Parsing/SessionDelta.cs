using System.Globalization;

namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Plan step 20: last accepted − first accepted, in thousands, with
/// wall-clock minutes. Lamp XP is required at both ends — never silent zeros.
/// </summary>
public static class SessionDelta
{
    public static SessionDeltaResult TryCreate(
        PlayReport first,
        PlayReport last,
        DateTimeOffset firstAt,
        DateTimeOffset lastAt)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);

        if (first.Xp is null || first.Adena is null || last.Xp is null || last.Adena is null)
        {
            return SessionDeltaResult.Fail("XP and Adena must be readable on the first and last snapshots.");
        }

        if (!first.LampXpRead || !last.LampXpRead
            || first.RedLampXp is null || first.PurpleLampXp is null
            || first.BlueLampXp is null || first.GreenLampXp is null
            || last.RedLampXp is null || last.PurpleLampXp is null
            || last.BlueLampXp is null || last.GreenLampXp is null)
        {
            return SessionDeltaResult.Fail(
                "Save is blocked until the Magic Lamp XP column is read at both ends of the session (no silent zeros).");
        }

        if (lastAt < firstAt)
        {
            return SessionDeltaResult.Fail("Last snapshot is earlier than the first.");
        }

        var xp = SubtractThousands("XP", first.Xp.Value, last.Xp.Value, out var error);
        if (error is not null)
        {
            return SessionDeltaResult.Fail(error);
        }

        var adena = SubtractThousands("Adena", first.Adena.Value, last.Adena.Value, out error);
        if (error is not null)
        {
            return SessionDeltaResult.Fail(error);
        }

        var red = SubtractThousands("Red lamp XP", first.RedLampXp.Value, last.RedLampXp.Value, out error);
        if (error is not null)
        {
            return SessionDeltaResult.Fail(error);
        }

        var purple = SubtractThousands("Purple lamp XP", first.PurpleLampXp.Value, last.PurpleLampXp.Value, out error);
        if (error is not null)
        {
            return SessionDeltaResult.Fail(error);
        }

        var blue = SubtractThousands("Blue lamp XP", first.BlueLampXp.Value, last.BlueLampXp.Value, out error);
        if (error is not null)
        {
            return SessionDeltaResult.Fail(error);
        }

        var green = SubtractThousands("Green lamp XP", first.GreenLampXp.Value, last.GreenLampXp.Value, out error);
        if (error is not null)
        {
            return SessionDeltaResult.Fail(error);
        }

        var minutes = Math.Max(
            1,
            (int)Math.Round((lastAt - firstAt).TotalMinutes, MidpointRounding.AwayFromZero));

        return SessionDeltaResult.Succeed(new SessionTotals(
            XpFarmed: xp,
            Adena: adena,
            RedLampXP: red,
            PurpleLampXP: purple,
            BlueLampXP: blue,
            GreenLampXP: green,
            Minutes: minutes,
            StartedAt: firstAt,
            EndedAt: lastAt));
    }

    public static SessionDeltaResult TryCreate(IReadOnlyList<PlayReportSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count < 2)
        {
            return SessionDeltaResult.Fail("Need at least two accepted snapshots to save a session.");
        }

        var first = snapshots[0];
        var last = snapshots[snapshots.Count - 1];
        return TryCreate(first.Report, last.Report, first.CapturedAt, last.CapturedAt);
    }

    private static long SubtractThousands(string field, long firstRaw, long lastRaw, out string? error)
    {
        var first = Amounts.ToThousands(firstRaw);
        var last = Amounts.ToThousands(lastRaw);
        if (last < first)
        {
            var inv = CultureInfo.InvariantCulture;
            error =
                $"{field} went backwards after converting to thousands "
                + $"({last.ToString("N0", inv)} < {first.ToString("N0", inv)}).";
            return 0;
        }

        error = null;
        return last - first;
    }
}

public sealed record PlayReportSnapshot(PlayReport Report, DateTimeOffset CapturedAt);

public sealed record SessionTotals(
    long XpFarmed,
    long Adena,
    long RedLampXP,
    long PurpleLampXP,
    long BlueLampXP,
    long GreenLampXP,
    int Minutes,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public sealed record SessionDeltaResult(bool Ok, string? Error, SessionTotals? Totals)
{
    public static SessionDeltaResult Succeed(SessionTotals totals) => new(true, null, totals);

    public static SessionDeltaResult Fail(string error) => new(false, error, null);
}
