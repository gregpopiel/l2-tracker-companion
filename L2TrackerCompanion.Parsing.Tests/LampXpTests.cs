namespace L2TrackerCompanion.Parsing.Tests;

public class LampXpTests
{
    [Fact]
    public void AllFourAndSumUnderDialogXpIsRead()
    {
        var parsed = Four(260_000, 0, 0, 0);
        var rows = OpenRows();
        var decision = LampXp.Decide(parsed, rows, dialogXp: 506_625, dialogAdena: 59_493);

        Assert.True(decision.LampXpRead);
        Assert.False(decision.LampPanelClosed);
        Assert.False(decision.ExceedsDialogXp);
        Assert.Equal(260_000, decision.Red);
        Assert.Equal(0, decision.Purple);
    }

    [Fact]
    public void TenXPurpleExceedingDialogXpIsDiscardedNotSaved()
    {
        var parsed = Four(32_825_000, 449_250_000, 49_725_000, 14_400_000);
        var decision = LampXp.Decide(parsed, OpenRows(), dialogXp: 283_881_103, dialogAdena: 1);

        Assert.True(decision.ExceedsDialogXp);
        Assert.False(decision.LampXpRead);
        Assert.False(decision.LampPanelClosed);
        Assert.Null(decision.Purple);
        Assert.True(decision.LampXpTotal > 283_881_103);
    }

    [Fact]
    public void MissingColourIsAllOrNoneNotAPartialSave()
    {
        var parsed = new Dictionary<string, long?>
        {
            ["red"] = 260_000,
            ["purple"] = 0,
            ["blue"] = null,
            ["green"] = 0,
        };
        var decision = LampXp.Decide(parsed, OpenRows(), 1_000_000, 1);

        Assert.False(decision.LampXpRead);
        Assert.Null(decision.Red);
    }

    [Fact]
    public void NoColourNamesWithFarmFieldsIsAClosedPanelNotAFailedRead()
    {
        var parsed = new Dictionary<string, long?>
        {
            ["red"] = null,
            ["purple"] = null,
            ["blue"] = null,
            ["green"] = null,
        };
        var decision = LampXp.Decide(parsed, new Dictionary<string, WordBox>(), dialogXp: 100, dialogAdena: 50);

        Assert.True(decision.LampPanelClosed);
        Assert.False(decision.LampXpRead);
        Assert.False(decision.ExceedsDialogXp);
    }

    [Fact]
    public void NoColourNamesWithoutFarmFieldsIsNotClosed()
    {
        var parsed = new Dictionary<string, long?>
        {
            ["red"] = null,
            ["purple"] = null,
            ["blue"] = null,
            ["green"] = null,
        };
        var decision = LampXp.Decide(parsed, new Dictionary<string, WordBox>(), dialogXp: null, dialogAdena: 50);

        Assert.False(decision.LampPanelClosed);
    }

    [Fact]
    public void FirstParsedKeepsZeroAndSkipsNulls()
    {
        Assert.Equal(0, LampXp.FirstParsed(null, 0, 200_000));
        Assert.Equal(44_250_000, LampXp.FirstParsed(44_250_000, 449_250_000));
        Assert.Null(LampXp.FirstParsed(null, null));
    }

    /// <summary>
    /// Real rows from the POC set, in source order (table crop, table tokens,
    /// dialog tokens, dialog crop). The table crop leads the precedence order
    /// and is the source most often wrong, so first-parseable picked the bad
    /// figure while two other sources agreed on the right one.
    /// </summary>
    [Theory]
    // 003834 green: table crop lost the K in "14M 400K" and read 14,000,400.
    [InlineData(14_400_000L, 14_000_400L, 14_000_000L, 14_400_000L, 14_400_000L)]
    // 003834 blue: table crop dropped "500K" outright.
    [InlineData(49_500_000L, 49_000_000L, 49_500_000L, null, 49_500_000L)]
    // 140305 blue: table crop misread the leading digit, 7M as 9M.
    [InlineData(7_776_000L, 9_776_000L, 7_776_000L, null, 7_776_000L)]
    // 235004 green: table tokens truncated "1M 120K" to "1M".
    [InlineData(1_120_000L, null, 1_000_000L, 1_120_000L, 1_120_000L)]
    // 140305 green: the table crop was right and the token path truncated.
    [InlineData(2_048_000L, 2_048_000L, 2_000_000L, null, 2_048_000L)]
    public void MostSupportedTakesTheValueTwoSourcesAgreeOn(
        long expected,
        long? tableCrop,
        long? tableTokens,
        long? dialogTokens,
        long? dialogCrop)
    {
        Assert.Equal(expected, LampXp.MostSupported(tableCrop, tableTokens, dialogTokens, dialogCrop));
    }

    [Fact]
    public void MostSupportedFallsBackToPrecedenceWithoutAgreement()
    {
        // Nothing agrees: the old first-parseable order still decides, so the
        // 10× leading-digit case resolves exactly as it did before.
        Assert.Equal(44_250_000, LampXp.MostSupported(44_250_000, 449_250_000, null, null));
        Assert.Equal(200_000, LampXp.MostSupported(null, 200_000, null, null));
        Assert.Null(LampXp.MostSupported(null, null, null, null));
    }

    [Fact]
    public void MostSupportedCountsZeroAsAValue()
    {
        // An empty lamp row is a real 0, not a missing read — two sources
        // agreeing on 0 must beat one source that produced a figure.
        // Both cases would resolve to the figure on precedence alone, so the
        // agreeing zeros are what decides them.
        Assert.Equal(0, LampXp.MostSupported(200_000, 0, 0, null));
        Assert.Equal(0, LampXp.MostSupported(200_000, null, 0, 0));
    }

    private static Dictionary<string, long?> Four(long red, long purple, long blue, long green) => new()
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
