namespace L2TrackerCompanion.Parsing;

/// <summary>
/// Build a farm log out of a <em>single</em> Play Report read. The panel is
/// already a complete session record — it carries its own totals and its own
/// duration — so nothing here subtracts an earlier snapshot or consults the
/// wall clock.
/// </summary>
/// <remarks>
/// Minutes come from the panel, not from how long the companion happened to be
/// running, which is what makes a log savable long after the farming stopped.
/// Amounts are converted to thousands exactly once (the old two-snapshot path
/// rounded both ends before subtracting, and could drift by a thousand).
/// Lamp XP stays all-or-none: a collapsed Magic Lamp panel blocks the save
/// rather than writing silent zeros.
/// </remarks>
public static class SessionSnapshot
{
    public static SnapshotSaveResult TryCreate(PlayReport report, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Xp is null || report.Adena is null)
        {
            return SnapshotSaveResult.Fail("XP and Adena must both be readable to save.");
        }

        if (report.Minutes is null)
        {
            return SnapshotSaveResult.Fail("The Play Report's own play time must be readable to save.");
        }

        if (report.Minutes.Value <= 0)
        {
            return SnapshotSaveResult.Fail("The Play Report shows no elapsed time yet.");
        }

        if (report.LampPanelClosed)
        {
            return SnapshotSaveResult.Fail(
                "Expand the Magic Lamp panel before saving — a collapsed panel would be stored as zero lamp XP.");
        }

        // Order matters: LampXp.Decide answers an impossible sum by clearing
        // LampXpRead and nulling the four figures, so the unread check below
        // would swallow this case and report the wrong reason for it.
        if (report.LampXpExceedsDialog)
        {
            return SnapshotSaveResult.Fail(
                "Lamp XP exceeds the dialog's own XP, which is impossible — the frame was misread.");
        }

        if (!report.LampXpRead
            || report.RedLampXp is null || report.PurpleLampXp is null
            || report.BlueLampXp is null || report.GreenLampXp is null)
        {
            return SnapshotSaveResult.Fail(
                "The Magic Lamp XP column could not be read (no silent zeros).");
        }

        var totals = new SessionTotals(
            XpFarmed: Amounts.ToThousands(report.Xp.Value),
            Adena: Amounts.ToThousands(report.Adena.Value),
            RedLampXP: Amounts.ToThousands(report.RedLampXp.Value),
            PurpleLampXP: Amounts.ToThousands(report.PurpleLampXp.Value),
            BlueLampXP: Amounts.ToThousands(report.BlueLampXp.Value),
            GreenLampXP: Amounts.ToThousands(report.GreenLampXp.Value),
            Minutes: report.Minutes.Value,
            StartedAt: capturedAt.AddMinutes(-report.Minutes.Value),
            EndedAt: capturedAt);

        return SnapshotSaveResult.Succeed(totals);
    }

    /// <summary>
    /// Identity of the report a save consumed, so the same panel cannot be
    /// posted twice. Raw figures, not thousands: rounding would let two
    /// genuinely different reports collide.
    /// </summary>
    public static string Fingerprint(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return string.Join(
            '|',
            report.Xp?.ToString() ?? "-",
            report.Adena?.ToString() ?? "-",
            report.Minutes?.ToString() ?? "-",
            report.LampXpTotal.ToString());
    }
}

/// <summary>
/// What a save will post: amounts already in thousands, minutes taken from the
/// Play Report itself.
/// </summary>
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

public sealed record SnapshotSaveResult(bool Ok, string? Error, SessionTotals? Totals)
{
    public static SnapshotSaveResult Succeed(SessionTotals totals) => new(true, null, totals);

    public static SnapshotSaveResult Fail(string error) => new(false, error, null);
}
