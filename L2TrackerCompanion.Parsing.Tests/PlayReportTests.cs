namespace L2TrackerCompanion.Parsing.Tests;

public class PlayReportTests
{
    [Fact]
    public void CompleteReadHasNoUnreadFields()
    {
        var lamps = LampXp.Decide(
            Four(260_000, 0, 0, 0),
            OpenRows(),
            dialogXp: 506_625,
            dialogAdena: 59_493);
        var report = PlayReport.From(506_625, 59_493, 4, lamps, locationHint: null);

        Assert.Empty(report.UnreadFields);
        Assert.Empty(report.Warnings);
        Assert.True(report.LampXpRead);
        Assert.Equal(260_000, report.RedLampXp);
        Assert.Null(report.LocationHint);
    }

    [Fact]
    public void MissingFarmFieldsAreNamedUnread()
    {
        var lamps = LampXp.Decide(
            Four(null, null, null, null),
            new Dictionary<string, WordBox>(),
            dialogXp: null,
            dialogAdena: null);
        var report = PlayReport.From(null, null, null, lamps, null);

        Assert.Equal(["XP", "Adena", "play time"], report.UnreadFields);
        Assert.Contains(report.Warnings, w => w.Contains("XP could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void ClosedLampPanelIsNotAFailedReadWarning()
    {
        var lamps = LampXp.Decide(
            Four(null, null, null, null),
            new Dictionary<string, WordBox>(),
            dialogXp: 100,
            dialogAdena: 50);
        var report = PlayReport.From(100, 50, 4, lamps, null);

        Assert.True(report.LampPanelClosed);
        Assert.False(report.LampXpRead);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("lamp table", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocationHintIsPassedThroughUntouched()
    {
        var lamps = LampXp.Decide(Four(0, 0, 0, 0), OpenRows(), 1, 1);
        var report = PlayReport.From(1, 1, 1, lamps, "Dragon Valley (east)");
        Assert.Equal("Dragon Valley (east)", report.LocationHint);
    }

    private static Dictionary<string, long?> Four(long? red, long? purple, long? blue, long? green) => new()
    {
        ["red"] = red,
        ["purple"] = purple,
        ["blue"] = blue,
        ["green"] = green,
    };

    private static Dictionary<string, WordBox> OpenRows() => new()
    {
        ["red"] = new WordBox("Red", 10, 80, 40, 14),
        ["purple"] = new WordBox("Purple", 10, 118, 40, 14),
        ["blue"] = new WordBox("Blue", 10, 156, 40, 14),
        ["green"] = new WordBox("Green", 10, 194, 40, 14),
    };
}
