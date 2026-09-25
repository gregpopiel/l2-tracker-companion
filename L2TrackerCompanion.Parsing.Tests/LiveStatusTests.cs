using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class LiveStatusTests
{
    [Fact]
    public void UnreadableXpIsRed()
    {
        var report = PlayReport.From(
            null,
            50,
            4,
            OpenLamps(0, 0, 0, 0, dialogXp: 50),
            null);
        var status = LiveStatus.FromReport(report);
        Assert.Equal(TrafficLight.Red, status.Light);
        Assert.Contains("XP", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedLampPanelIsOrangeNotRed()
    {
        var lamps = LampXp.Decide(
            new Dictionary<string, long?>
            {
                ["red"] = null,
                ["purple"] = null,
                ["blue"] = null,
                ["green"] = null,
            },
            new Dictionary<string, WordBox>(),
            dialogXp: 100,
            dialogAdena: 50);
        var report = PlayReport.From(100, 50, 4, lamps, null);
        Assert.True(report.LampPanelClosed);

        var status = LiveStatus.FromReport(report);
        Assert.Equal(TrafficLight.Orange, status.Light);
        Assert.DoesNotContain("couldn't be read", status.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("closed", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FarmAndLampsReadIsGreenEvenWithoutLocationHint()
    {
        var report = PlayReport.From(506_625, 59_493, 4, OpenLamps(260_000, 0, 0, 0, 506_625), null);
        var status = LiveStatus.FromReport(report);
        Assert.Equal(TrafficLight.Green, status.Light);
        Assert.Null(report.LocationHint);
    }

    [Fact]
    public void LampTableInFrameButUnreadableIsRed()
    {
        var unreadLamps = LampXp.Decide(
            new Dictionary<string, long?>
            {
                ["red"] = null,
                ["purple"] = null,
                ["blue"] = null,
                ["green"] = null,
            },
            OpenRows(),
            dialogXp: 1_000,
            dialogAdena: 10);
        var report = PlayReport.From(1_000, 10, 1, unreadLamps, null);
        Assert.False(report.LampPanelClosed);
        Assert.False(report.LampXpRead);

        var status = LiveStatus.FromReport(report);
        Assert.Equal(TrafficLight.Red, status.Light);
        Assert.Contains("Lamp", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void LampXpExceedingDialogIsRed()
    {
        var exceeds = LampXp.Decide(
            new Dictionary<string, long?>
            {
                ["red"] = 500,
                ["purple"] = 500,
                ["blue"] = 500,
                ["green"] = 500,
            },
            OpenRows(),
            dialogXp: 100,
            dialogAdena: 10);
        var report = PlayReport.From(100, 10, 1, exceeds, null);
        Assert.True(report.LampXpExceedsDialog);

        var status = LiveStatus.FromReport(report);
        Assert.Equal(TrafficLight.Red, status.Light);
        Assert.Contains("exceeds", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameNotRunningIsRed()
    {
        Assert.Equal(TrafficLight.Red, LiveStatus.GameNotRunning().Light);
    }

    [Fact]
    public void FormatIncludesLightAndLiveValues()
    {
        var report = PlayReport.From(506_625, 59_493, 4, OpenLamps(260_000, 0, 0, 0, 506_625), "Dragon Valley (east)");
        var text = LiveStatus.Format(LiveStatus.FromReport(report));
        Assert.Contains("Light: Green", text, StringComparison.Ordinal);
        Assert.Contains("XP/min: 126,656", text, StringComparison.Ordinal);
        Assert.Contains("Adena/min: 14,873", text, StringComparison.Ordinal);
        Assert.Contains("XP: 506,625", text, StringComparison.Ordinal);
        Assert.Contains("Dragon Valley (east)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDisplayKeepsTheTickVerdictAndShowsTheHeldReport()
    {
        var held = PlayReport.From(506_625, 59_493, 4, OpenLamps(260_000, 0, 0, 0, 506_625), "Dragon Valley (east)");
        var shown = LiveStatus.ForDisplay(LiveStatus.ParseFailed("OCR failed"), held);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.Equal("OCR failed", shown.Detail);
        Assert.Equal(held, shown.Report);
        var text = LiveStatus.Format(shown);
        Assert.Contains("Light: Red", text, StringComparison.Ordinal);
        Assert.Contains("OCR failed", text, StringComparison.Ordinal);
        Assert.Contains("XP: 506,625", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDisplayWithNoSaveSourceShowsNoTotals()
    {
        var tick = LiveStatus.ParseFailed("OCR failed");
        var held = PlayReport.From(506_625, 59_493, 4, OpenLamps(260_000, 0, 0, 0, 506_625), null);
        var withHeld = LiveStatus.ForDisplay(tick, held);
        Assert.Equal(held, withHeld.Report);

        var empty = LiveStatus.ForDisplay(tick, null);
        Assert.Equal(TrafficLight.Red, empty.Light);
        Assert.Null(empty.Report);
        Assert.Empty(LiveStatus.FormatValues(empty.Report));
    }

    [Fact]
    public void AnXpDropWithACleanHoldStaysGreen()
    {
        var held = TestReports.Open();
        var dropped = TestReports.Open(xp: 800_000);
        var shown = LiveStatus.ForPlayer(
            LiveStatus.TickRejected("Discarded: XP dropped from 1,200,000 to 800,000."),
            dropped,
            held,
            discarded: true);

        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.DoesNotContain("Discarded", shown.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("dropped", shown.Detail, StringComparison.Ordinal);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void AWithdrawnLampColumnWithACleanHoldStaysGreen()
    {
        var held = TestReports.Open(minutes: 30, green: 256_000);
        var withdrawn = LampContinuity.Withdraw(
            held,
            held,
            TestReports.Open(xp: 1_200_000, minutes: 31, green: 0));
        var shown = LiveStatus.ForPlayer(
            LiveStatus.FromReport(withdrawn),
            withdrawn,
            held,
            discarded: false);

        Assert.True(LampContinuity.WasWithdrawn(withdrawn));
        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.DoesNotContain("Magic Lamp", shown.Detail, StringComparison.Ordinal);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void AGenuinelyUnreadLampColumnStillAnnouncesItself()
    {
        var unread = TestReports.UnreadLamps();
        var held = TestReports.Open();
        var tick = LiveStatus.FromReport(unread);
        var shown = LiveStatus.ForPlayer(tick, unread, held, discarded: false);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.Equal(tick.Detail, shown.Detail);
        Assert.Contains("Magic Lamp XP column", shown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosedLampPanelStillAnnouncesItself()
    {
        var closed = TestReports.ClosedPanel();
        var tick = LiveStatus.FromReport(closed);
        var shown = LiveStatus.ForPlayer(tick, closed, TestReports.Open(), discarded: false);

        Assert.Equal(TrafficLight.Orange, shown.Light);
        Assert.Equal(tick.Detail, shown.Detail);
        Assert.Contains("closed", shown.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASilentMisreadPaintsASplicedHoldGreen()
    {
        var held = TestReports.Open(
            xp: 9_210_400,
            confidence: new ReadConfidence(
                XpDisagreed: true,
                XpSpliced: true,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: false,
                PlayTimeDisagreed: false,
                XpFromTokens: 4_210_400,
                XpFromCrop: 9_210_400));
        var shown = LiveStatus.ForPlayer(
            LiveStatus.TickRejected("Discarded: Adena dropped from 300,000 to 100,000."),
            TestReports.Open(adena: 100_000),
            held,
            discarded: true);

        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.DoesNotContain("spliced", shown.Detail, StringComparison.Ordinal);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void TickRejectedIsRedWithoutAReport()
    {
        var status = LiveStatus.TickRejected("Discarded: XP dropped from 200 to 100.");
        Assert.Equal(TrafficLight.Red, status.Light);
        Assert.Null(status.Report);
        Assert.Contains("XP dropped", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void APlayTimeDisagreementWithACleanHoldStaysGreen()
    {
        var held = TestReports.Open(minutes: 90);
        var disagreed = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: false,
                PlayTimeDisagreed: true));
        var shown = LiveStatus.ForPlayer(
            LiveStatus.FromReport(disagreed),
            disagreed,
            held,
            discarded: false);

        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.DoesNotContain("play-time", shown.Detail, StringComparison.Ordinal);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void APlayTimeDisagreementWithNoHoldStillAnnouncesItself()
    {
        var disagreed = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: false,
                PlayTimeDisagreed: true));
        var tick = LiveStatus.FromReport(disagreed);
        var shown = LiveStatus.ForPlayer(tick, disagreed, null, discarded: false);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.Equal(ReadIssues.PlayTimeDisagreed, shown.Detail);
    }

    [Fact]
    public void AnImpossibleLampSumWithACleanHoldStaysGreen()
    {
        var held = TestReports.Open(minutes: 90);
        var exceeds = PlayReport.From(
            100,
            10,
            1,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = 500,
                    ["purple"] = 500,
                    ["blue"] = 500,
                    ["green"] = 500,
                },
                OpenRows(),
                dialogXp: 100,
                dialogAdena: 10),
            null);
        var shown = LiveStatus.ForPlayer(
            LiveStatus.FromReport(exceeds),
            exceeds,
            held,
            discarded: false);

        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.DoesNotContain("exceeds", shown.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void AnImpossibleLampSumWithNoHoldStillAnnouncesItself()
    {
        var exceeds = PlayReport.From(
            100,
            10,
            1,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = 500,
                    ["purple"] = 500,
                    ["blue"] = 500,
                    ["green"] = 500,
                },
                OpenRows(),
                dialogXp: 100,
                dialogAdena: 10),
            null);
        var tick = LiveStatus.FromReport(exceeds);
        var shown = LiveStatus.ForPlayer(tick, exceeds, null, discarded: false);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.Equal(ReadIssues.LampXpExceedsDialogXp, shown.Detail);
    }

    [Fact]
    public void AnAdenaDisagreementWithACleanHoldStaysGreen()
    {
        var held = TestReports.Open(minutes: 90, adena: 1_783_525);
        var disagreed = DisagreedAdena();
        var shown = LiveStatus.ForPlayer(
            LiveStatus.FromReport(disagreed),
            disagreed,
            held,
            discarded: false);

        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.DoesNotContain("Adena", shown.Detail, StringComparison.Ordinal);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void AnAdenaDisagreementWithNoHoldStillAnnouncesItself()
    {
        var disagreed = DisagreedAdena();
        var tick = LiveStatus.FromReport(disagreed);
        var shown = LiveStatus.ForPlayer(tick, disagreed, null, discarded: false);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.StartsWith(ReadIssues.AdenaDisagreed, shown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterruptedTickWithUnreadXpKeepsTheHoldRed()
    {
        var held = TestReports.Open(minutes: 90);
        var unread = TestReports.Open() with { Xp = null, UnreadFields = ["XP"] };
        var gate = SaveGate.EvaluateWithHold(unread, At, held, At);
        var shown = LiveStatus.ForInterruptedTick(gate);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.Equal("Couldn't read XP.", shown.Detail);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void AnInterruptedTickWithAQuietPlayTimeHoldStaysGreen()
    {
        var held = TestReports.Open(minutes: 90);
        var gate = SaveGate.EvaluateWithHold(DisagreedPlayTime(), At, held, At);
        var shown = LiveStatus.ForInterruptedTick(gate);

        Assert.Equal(TrafficLight.Green, shown.Light);
        Assert.Equal("Farm and lamps read.", shown.Detail);
        Assert.Equal(held, shown.Report);
    }

    [Fact]
    public void AnInterruptedTickWithAPlayTimeBlockAndNoHoldStaysRed()
    {
        var gate = SaveGate.EvaluateWithHold(DisagreedPlayTime(), At, null, At);
        var shown = LiveStatus.ForInterruptedTick(gate);

        Assert.Equal(TrafficLight.Red, shown.Light);
        Assert.Equal(ReadIssues.PlayTimeDisagreed, shown.Detail);
        Assert.Null(shown.Report);
    }

    [Fact]
    public void AnInterruptedTickWithNothingToPaintIsIdle()
    {
        var gate = SaveGate.EvaluateWithHold(null, At, null, At);
        var shown = LiveStatus.ForInterruptedTick(gate);

        Assert.Equal(TrafficLight.Idle, shown.Light);
        Assert.Equal("No snapshot yet.", shown.Detail);
        Assert.Null(shown.Report);
    }

    private static readonly DateTimeOffset At = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);

    private static PlayReport DisagreedPlayTime()
        => TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: false,
                PlayTimeDisagreed: true));

    private static PlayReport DisagreedAdena()
        => TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: true,
                PlayTimeDisagreed: false,
                AdenaFromTokens: 1_783_525,
                AdenaFromCrop: 31_783_525));

    private static LampXpDecision OpenLamps(long red, long purple, long blue, long green, long dialogXp)
        => LampXp.Decide(
            new Dictionary<string, long?>
            {
                ["red"] = red,
                ["purple"] = purple,
                ["blue"] = blue,
                ["green"] = green,
            },
            OpenRows(),
            dialogXp,
            dialogAdena: 1);

    private static Dictionary<string, WordBox> OpenRows() => new()
    {
        ["red"] = new WordBox("Red", 10, 80, 40, 14),
        ["purple"] = new WordBox("Purple", 10, 118, 40, 14),
        ["blue"] = new WordBox("Blue", 10, 156, 40, 14),
        ["green"] = new WordBox("Green", 10, 194, 40, 14),
    };
}
