using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class MonotonicityTests
{
    [Fact]
    public void FirstSnapshotIsAlwaysAccepted()
    {
        var decision = Monotonicity.Evaluate(null, Farm(100, 10, 1));
        Assert.True(decision.Accepted);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void EqualOrHigherFarmFieldsAreAccepted()
    {
        var last = Farm(100, 10, 1);
        Assert.True(Monotonicity.Evaluate(last, Farm(100, 10, 1)).Accepted);
        Assert.True(Monotonicity.Evaluate(last, Farm(200, 20, 2)).Accepted);
    }

    [Fact]
    public void XpDropIsRejected()
    {
        var decision = Monotonicity.Evaluate(Farm(200, 20, 2), Farm(100, 20, 2));
        Assert.False(decision.Accepted);
        Assert.Contains("XP dropped", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AdenaDropIsRejected()
    {
        var decision = Monotonicity.Evaluate(Farm(200, 20, 2), Farm(200, 5, 2));
        Assert.False(decision.Accepted);
        Assert.Contains("Adena dropped", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayTimeDropIsRejected()
    {
        var decision = Monotonicity.Evaluate(Farm(200, 20, 10), Farm(200, 20, 4));
        Assert.False(decision.Accepted);
        Assert.Contains("play time dropped", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LosingAPreviouslyReadFarmFieldIsRejected()
    {
        var last = Farm(200, 20, 2);
        var unreadXp = PlayReport.From(null, 20, 2, OpenLamps(0, 0, 0, 0, dialogXp: 20), null);
        var decision = Monotonicity.Evaluate(last, unreadXp);
        Assert.False(decision.Accepted);
        Assert.Contains("XP could not be read", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LampDropIsRejectedOnlyWhenBothTicksReadLamps()
    {
        var last = Farm(1_000, 10, 1, red: 100, purple: 50);
        var lowerPurple = Farm(1_000, 10, 1, red: 100, purple: 40);
        var decision = Monotonicity.Evaluate(last, lowerPurple);
        Assert.False(decision.Accepted);
        Assert.Contains("Purple lamp XP dropped", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedLampPanelIsNotALampDrop()
    {
        var last = Farm(1_000, 10, 1, red: 100, purple: 50);
        var closed = ClosedPanel(1_200, 12, 2);
        var decision = Monotonicity.Evaluate(last, closed);
        Assert.True(decision.Accepted);
        Assert.True(closed.LampPanelClosed);
        Assert.False(closed.LampXpRead);
    }

    [Fact]
    public void UnreadLampsDoNotUseLampMonotonicity()
    {
        var last = Farm(1_000, 10, 1, red: 100, purple: 50);
        var unreadLamps = PlayReport.From(
            1_200,
            12,
            2,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = null,
                    ["purple"] = null,
                    ["blue"] = null,
                    ["green"] = null,
                },
                OpenRows(),
                dialogXp: 1_200,
                dialogAdena: 12),
            null);
        Assert.False(unreadLamps.LampXpRead);
        Assert.False(unreadLamps.LampPanelClosed);
        Assert.True(Monotonicity.Evaluate(last, unreadLamps).Accepted);
    }

    private static PlayReport Farm(
        long xp,
        long adena,
        int minutes,
        long red = 0,
        long purple = 0,
        long blue = 0,
        long green = 0)
        => PlayReport.From(xp, adena, minutes, OpenLamps(red, purple, blue, green, xp), null);

    private static PlayReport ClosedPanel(long xp, long adena, int minutes)
        => PlayReport.From(
            xp,
            adena,
            minutes,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = null,
                    ["purple"] = null,
                    ["blue"] = null,
                    ["green"] = null,
                },
                new Dictionary<string, WordBox>(),
                xp,
                adena),
            null);

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
