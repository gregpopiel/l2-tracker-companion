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
        Assert.Contains("XP: 506,625", text, StringComparison.Ordinal);
        Assert.Contains("Dragon Valley (east)", text, StringComparison.Ordinal);
    }

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
