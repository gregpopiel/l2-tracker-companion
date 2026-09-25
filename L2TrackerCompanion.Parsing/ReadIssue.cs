namespace L2TrackerCompanion.Parsing;

/// <summary>
/// One defect found in a single Play Report read: the colour it deserves, the
/// sentence shown for it, and whether it stops the read from being saved.
/// </summary>
public sealed record ReadIssue(TrafficLight Light, string Message, bool BlocksSave);

/// <summary>
/// The one place that says what is wrong with a Play Report read.
/// </summary>
/// <remarks>
/// <see cref="SaveGate"/> (what Save may post) and <see cref="LiveStatus"/> (what
/// the traffic light shows) used to walk the same flags in almost the same order
/// and phrase the findings independently, so the two drifted: the same frame
/// could report "XP and Adena must both be readable to save." next to "Couldn't
/// read XP and play time." – contradicting each other about which field failed,
/// while a play-time contradiction blocked Save with no colour change at all.
/// Both now describe a frame through this class, so one defect produces exactly
/// one sentence and the UI only has to decide where to show it.
/// </remarks>
public static class ReadIssues
{
    public const string UnreadLampColumn =
        "The Magic Lamp XP column could not be read (no silent zeros).";

    public const string PlayTimeDisagreed =
        "The play-time line was read twice and the two reads disagreed.";

    public const string LampXpExceedsDialogXp =
        "Lamp XP exceeds the dialog's own XP, which is impossible – the frame was misread.";

    public const string AdenaDisagreed = "Adena's two reads disagreed";

    /// <summary>
    /// The column was withdrawn because a lamp figure fell, and that is the
    /// only defect this frame would announce. A stronger defect still speaks.
    /// </summary>
    public static bool WithdrawnColumnIsTheOnlyDefect(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return LampContinuity.WasWithdrawn(report)
            && Describe(report)?.Message == UnreadLampColumn;
    }

    /// <summary>
    /// A play-time contradiction, an impossible lamp sum, or an Adena
    /// disagreement. The frame still cannot be saved. The player is not told
    /// when a good frame is already held: the Adena sentence names two OCR
    /// passes of one number, and the player cannot choose either.
    /// </summary>
    public static bool IsQuietDefect(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var message = Describe(report)?.Message;
        return message == PlayTimeDisagreed
            || message == LampXpExceedsDialogXp
            || message?.StartsWith(AdenaDisagreed, StringComparison.Ordinal) == true;
    }

    /// <returns>The first defect found, or null when the read is clean.</returns>
    public static ReadIssue? Describe(PlayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // A field whose two independent reads disagree is worse than an unread
        // one: it has a plausible-looking value that must not be trusted, so
        // the disagreements are checked ahead of the unread list.
        if (report.Confidence.PlayTimeDisagreed)
        {
            return Blocking(TrafficLight.Red, PlayTimeDisagreed);
        }

        if (report.Confidence.AdenaDisagreed)
        {
            return Blocking(
                TrafficLight.Red,
                Detail(AdenaDisagreed, report.Confidence.DescribeAdenaDispute()));
        }

        if (report.Confidence.XpMagnitudeMismatch)
        {
            return Blocking(
                TrafficLight.Red,
                Detail(
                    "The two XP reads disagreed on the number of digits – one of them dropped a digit",
                    report.Confidence.DescribeXpDispute()));
        }

        // Naming the fields that actually failed, rather than the pair a save
        // happens to need: a frame that read Adena fine must not be told Adena
        // is unreadable.
        if (report.UnreadFields.Count > 0)
        {
            return Blocking(TrafficLight.Red, DescribeUnread(report.UnreadFields));
        }

        if (report.Minutes <= 0)
        {
            return Blocking(TrafficLight.Red, "The Play Report shows no elapsed time yet.");
        }

        // A collapsed panel is the player's own doing, not a bad read – orange,
        // but still blocking, since saving it would store silent zeros.
        if (report.LampPanelClosed)
        {
            return Blocking(
                TrafficLight.Orange,
                "Magic Lamp panel closed – expand it before saving; a collapsed panel "
                + "would be stored as zero lamp XP.");
        }

        // Order matters: LampXp.Decide answers an impossible sum by clearing
        // LampXpRead and nulling the four figures, so the unread check below
        // would swallow this case and report the wrong reason for it.
        if (report.LampXpExceedsDialog)
        {
            return Blocking(TrafficLight.Red, LampXpExceedsDialogXp);
        }

        if (!report.LampXpRead
            || report.RedLampXp is null || report.PurpleLampXp is null
            || report.BlueLampXp is null || report.GreenLampXp is null)
        {
            return Blocking(TrafficLight.Red, UnreadLampColumn);
        }

        // A picked or spliced XP is the figure Save posts. The two OCR passes
        // disagreed, but the sentence named nothing the player can choose, so
        // it is not an issue.
        return null;
    }

    private static ReadIssue Blocking(TrafficLight light, string message)
        => new(light, message, BlocksSave: true);

    private static string DescribeUnread(IReadOnlyList<string> unread)
    {
        if (unread.Count >= 3)
        {
            return "Couldn't read farm data.";
        }

        if (unread.Count == 1)
        {
            return $"Couldn't read {unread[0]}.";
        }

        return $"Couldn't read {unread[0]} and {unread[1]}.";
    }

    /// <summary>
    /// Append the two competing figures when both are known, so the message
    /// says what to look at rather than only that something is wrong.
    /// </summary>
    internal static string Detail(string headline, string? dispute)
        => dispute is null ? headline + "." : $"{headline} – {dispute}.";
}
