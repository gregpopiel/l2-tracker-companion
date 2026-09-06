using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

/// <summary>
/// The one describer both SaveGate and LiveStatus read from. These tests pin
/// the order it walks the flags in, since that order is what decides which of
/// several simultaneous defects the player is actually told about.
/// </summary>
public class ReadIssuesTests
{
    private static ReadConfidence Confidence(
        bool xpDisagreed = false,
        bool xpSpliced = false,
        bool xpMagnitudeMismatch = false,
        bool adenaDisagreed = false,
        bool playTimeDisagreed = false,
        long? xpFromTokens = null,
        long? xpFromCrop = null)
        => new(
            xpDisagreed,
            xpSpliced,
            xpMagnitudeMismatch,
            adenaDisagreed,
            playTimeDisagreed,
            xpFromTokens,
            xpFromCrop);

    [Fact]
    public void ACleanReadHasNothingToSay()
        => Assert.Null(ReadIssues.Describe(TestReports.Open()));

    [Fact]
    public void AnUnreadFieldIsNamedRatherThanThePairASaveNeeds()
    {
        // The old save-side wording said "XP and Adena must both be readable"
        // whichever of the two had failed, so a frame that read Adena fine was
        // told Adena was unreadable — next to a banner naming the real fields.
        var issue = ReadIssues.Describe(TestReports.Open(xp: null, minutes: null));

        Assert.NotNull(issue);
        Assert.True(issue.BlocksSave);
        Assert.Equal(TrafficLight.Red, issue.Light);
        Assert.Equal("Couldn't read XP and play time.", issue.Message);
        Assert.DoesNotContain("Adena", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneUnreadFieldIsNamedOnItsOwn()
        => Assert.Equal(
            "Couldn't read Adena.",
            ReadIssues.Describe(TestReports.Open(adena: null))!.Message);

    [Fact]
    public void ThreeUnreadFieldsStopListingThem()
        => Assert.Equal(
            "Couldn't read farm data.",
            ReadIssues.Describe(TestReports.Open(xp: null, adena: null, minutes: null))!.Message);

    [Fact]
    public void APlayTimeDisagreementIsRedRatherThanSilent()
    {
        // This blocked the save all along, but nothing described it: the light
        // stayed green and no banner appeared, so Save simply refused to unlock
        // with no reason on screen anywhere.
        var issue = ReadIssues.Describe(
            TestReports.Open(confidence: Confidence(playTimeDisagreed: true)));

        Assert.NotNull(issue);
        Assert.True(issue.BlocksSave);
        Assert.Equal(TrafficLight.Red, issue.Light);
        Assert.Contains("play-time", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroLengthPlayReportIsRedRatherThanSilent()
    {
        var issue = ReadIssues.Describe(TestReports.Open(minutes: 0));

        Assert.NotNull(issue);
        Assert.True(issue.BlocksSave);
        Assert.Equal(TrafficLight.Red, issue.Light);
        Assert.Contains("no elapsed time", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADisagreementOutranksAnUnreadField()
    {
        // A plausible-looking figure that must not be trusted is worse news
        // than a blank one, so it is the half worth reporting.
        var issue = ReadIssues.Describe(
            TestReports.Open(adena: null, confidence: Confidence(adenaDisagreed: true)));

        Assert.Contains("disagreed", issue!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosedLampPanelIsOrangeAndStillBlocks()
    {
        var issue = ReadIssues.Describe(TestReports.ClosedPanel());

        Assert.NotNull(issue);
        Assert.Equal(TrafficLight.Orange, issue.Light);
        Assert.True(issue.BlocksSave);
        Assert.Contains("closed", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnreadLampColumnIsRed()
    {
        var issue = ReadIssues.Describe(TestReports.UnreadLamps());

        Assert.NotNull(issue);
        Assert.Equal(TrafficLight.Red, issue.Light);
        Assert.Contains("Magic Lamp XP column", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImpossibleLampSumIsReportedAheadOfTheUnreadColumnItCauses()
    {
        // LampXp.Decide answers an impossible sum by clearing LampXpRead and
        // nulling the figures, so checking "unread" first would report the
        // symptom and hide the cause.
        var report = PlayReport.From(
            1_000_000,
            250_000,
            60,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = 900_000,
                    ["purple"] = 900_000,
                    ["blue"] = 0,
                    ["green"] = 0,
                },
                TestReports.OpenRows(),
                dialogXp: 1_000_000,
                dialogAdena: 250_000),
            null);

        Assert.True(report.LampXpExceedsDialog);
        Assert.Contains("exceeds", ReadIssues.Describe(report)!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SplicedXpIsTheOneDefectThatDoesNotBlock()
    {
        var issue = ReadIssues.Describe(
            TestReports.Open(
                xp: 9_210_400,
                confidence: Confidence(
                    xpDisagreed: true,
                    xpSpliced: true,
                    xpFromTokens: 4_210_400,
                    xpFromCrop: 9_210_400)));

        Assert.NotNull(issue);
        Assert.False(issue.BlocksSave);
        Assert.Equal(TrafficLight.Orange, issue.Light);
        Assert.Contains("Saving 9,210,400 (spliced)", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGateAndTheTrafficLightNowSayTheSameThing()
    {
        // The whole point of the shared describer: one defect, one sentence,
        // whichever of the two surfaces the player happens to be looking at.
        var report = TestReports.Open(xp: null, minutes: null);

        Assert.Equal(
            LiveStatus.FromReport(report).Detail,
            SaveGate.Evaluate(report, DateTimeOffset.UnixEpoch).BlockReason);
    }
}
