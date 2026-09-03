namespace L2TrackerCompanion.Parsing;

/// <summary>
/// One-shot Play Report parse. Every figure was read off the screenshot;
/// nothing is derived from counts or settings. Matching <see cref="LocationHint"/>
/// against a spot list is the caller's job.
/// </summary>
public sealed record PlayReport(
    long? Xp,
    long? Adena,
    int? Minutes,
    long? RedLampXp,
    long? PurpleLampXp,
    long? BlueLampXp,
    long? GreenLampXp,
    bool LampXpRead,
    bool LampPanelClosed,
    bool LampXpExceedsDialog,
    long LampXpTotal,
    string? LocationHint,
    IReadOnlyList<string> UnreadFields,
    IReadOnlyList<string> Warnings,
    ReadConfidence Confidence)
{
    public static PlayReport From(
        long? xp,
        long? adena,
        int? minutes,
        LampXpDecision lamps,
        string? locationHint,
        ReadConfidence? confidence = null)
    {
        ArgumentNullException.ThrowIfNull(lamps);
        confidence ??= ReadConfidence.Trusted;

        var unread = new List<string>();
        var warnings = new List<string>();
        if (xp is null)
        {
            unread.Add("XP");
            warnings.Add("XP could not be read");
        }

        if (adena is null)
        {
            unread.Add("Adena");
            warnings.Add("Adena could not be read");
        }

        if (minutes is null)
        {
            unread.Add("play time");
            warnings.Add(confidence.PlayTimeDisagreed
                ? "Play time was read twice and the two reads contradicted each other"
                : "Play time could not be read");
        }

        if (confidence.AdenaDisagreed)
        {
            var dispute = confidence.DescribeAdenaDispute();
            warnings.Add(dispute is null
                ? "Adena's two reads contradicted each other"
                : $"Adena's two reads contradicted each other ({dispute})");
        }

        if (confidence.XpDisagreed)
        {
            var dispute = confidence.DescribeXpDispute();
            var how = confidence.XpSpliced
                ? "XP was repaired by splicing two disagreeing reads"
                : "XP's two reads contradicted each other";
            warnings.Add(dispute is null ? how : $"{how} ({dispute})");
        }

        if (lamps.ExceedsDialogXp)
        {
            warnings.Add(
                $"Lamp XP ({lamps.LampXpTotal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}) "
                + $"exceeds the dialog's own XP "
                + $"({(xp?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "null")}), "
                + "which is impossible — the lamp figures were discarded");
        }
        else if (!lamps.LampXpRead && lamps.HasLampRows)
        {
            warnings.Add("The lamp table's XP column couldn't be read");
        }

        return new PlayReport(
            Xp: xp,
            Adena: adena,
            Minutes: minutes,
            RedLampXp: lamps.Red,
            PurpleLampXp: lamps.Purple,
            BlueLampXp: lamps.Blue,
            GreenLampXp: lamps.Green,
            LampXpRead: lamps.LampXpRead,
            LampPanelClosed: lamps.LampPanelClosed,
            LampXpExceedsDialog: lamps.ExceedsDialogXp,
            LampXpTotal: lamps.LampXpTotal,
            LocationHint: locationHint,
            UnreadFields: unread,
            Warnings: warnings,
            Confidence: confidence);
    }
}
