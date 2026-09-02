using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class LiveRatesTests
{
    [Fact]
    public void DividesRawTotalsByPlayTimeMinutes()
    {
        var report = Report(506_625, 59_493, 4);
        var rates = LiveRates.From(report);
        Assert.False(rates.NeedPlayTime);
        Assert.Equal(126_656, rates.XpPerMin);
        Assert.Equal(14_873, rates.AdenaPerMin);
    }

    [Fact]
    public void MinutesZeroMakesBothUnavailable()
    {
        var rates = LiveRates.From(Report(506_625, 59_493, 0));
        Assert.True(rates.NeedPlayTime);
        Assert.Null(rates.XpPerMin);
        Assert.Null(rates.AdenaPerMin);
        var text = LiveRates.Format(Report(506_625, 59_493, 0));
        Assert.Contains("XP/min: (need play time)", text, StringComparison.Ordinal);
        Assert.Contains("Adena/min: (need play time)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MinutesNullMakesBothUnavailable()
    {
        var rates = LiveRates.From(Report(506_625, 59_493, null));
        Assert.True(rates.NeedPlayTime);
        Assert.Null(rates.XpPerMin);
        Assert.Null(rates.AdenaPerMin);
    }

    [Fact]
    public void UnreadXpStillAllowsAdenaPerMin()
    {
        var rates = LiveRates.From(Report(null, 59_493, 4));
        Assert.False(rates.NeedPlayTime);
        Assert.Null(rates.XpPerMin);
        Assert.Equal(14_873, rates.AdenaPerMin);
        var text = LiveRates.Format(Report(null, 59_493, 4));
        Assert.Contains("XP/min: (unread)", text, StringComparison.Ordinal);
        Assert.Contains("Adena/min: 14,873", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadAdenaStillAllowsXpPerMin()
    {
        var rates = LiveRates.From(Report(506_625, null, 4));
        Assert.Equal(126_656, rates.XpPerMin);
        Assert.Null(rates.AdenaPerMin);
        var text = LiveRates.Format(Report(506_625, null, 4));
        Assert.Contains("XP/min: 126,656", text, StringComparison.Ordinal);
        Assert.Contains("Adena/min: (unread)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BothAmountsUnreadStillFormatAsUnreadWhenPlayTimeIsKnown()
    {
        var text = LiveRates.Format(Report(null, null, 4));
        Assert.Contains("XP/min: (unread)", text, StringComparison.Ordinal);
        Assert.Contains("Adena/min: (unread)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("need play time", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DividesMagnitudesPastDoubleIntegerPrecision()
    {
        const long pastDouble = (1L << 53) + 1;
        var rates = LiveRates.From(Report(pastDouble, pastDouble, 1));
        Assert.Equal(pastDouble, rates.XpPerMin);
        Assert.Equal(pastDouble, rates.AdenaPerMin);
    }

    [Fact]
    public void RoundsHalfAwayFromZero()
    {
        var rates = LiveRates.From(Report(5, 1, 2));
        Assert.Equal(3, rates.XpPerMin);
        Assert.Equal(1, rates.AdenaPerMin);
    }

    [Fact]
    public void FormatIsEmptyWhenThereIsNoReport()
    {
        Assert.Equal(string.Empty, LiveRates.Format(null));
    }

    [Fact]
    public void LiveStatusFormatPutsRatesBeforeTotals()
    {
        var report = Report(506_625, 59_493, 4);
        var text = LiveStatus.Format(LiveStatus.FromReport(report));
        var xpMin = text.IndexOf("XP/min: 126,656", StringComparison.Ordinal);
        var adenaMin = text.IndexOf("Adena/min: 14,873", StringComparison.Ordinal);
        var xp = text.IndexOf("XP: 506,625", StringComparison.Ordinal);
        Assert.True(xpMin >= 0);
        Assert.True(adenaMin > xpMin);
        Assert.True(xp > adenaMin);
    }

    private static PlayReport Report(long? xp, long? adena, int? minutes)
        => PlayReport.From(xp, adena, minutes, OpenLamps(0, 0, 0, 0, dialogXp: xp ?? 1), null);

    private static LampXpDecision OpenLamps(long red, long purple, long blue, long green, long dialogXp)
        => LampXp.Decide(
            new Dictionary<string, long?>
            {
                ["red"] = red,
                ["purple"] = purple,
                ["blue"] = blue,
                ["green"] = green,
            },
            new Dictionary<string, WordBox>
            {
                ["red"] = new WordBox("Red", 10, 80, 40, 14),
                ["purple"] = new WordBox("Purple", 10, 118, 40, 14),
                ["blue"] = new WordBox("Blue", 10, 156, 40, 14),
                ["green"] = new WordBox("Green", 10, 194, 40, 14),
            },
            dialogXp,
            dialogAdena: 1);
}
