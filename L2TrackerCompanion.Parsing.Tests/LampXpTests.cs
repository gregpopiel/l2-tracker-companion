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

    /// <summary>
    /// Real rows where two sources lost the same trailing group and would
    /// out-vote the one source that read it. A truncated figure is the right
    /// figure with a group zeroed, so it is promoted rather than counted
    /// against it.
    /// </summary>
    [Theory]
    // The live 2026-09-06 failure: game showed "36M 608K", app stored 36,000,000.
    [InlineData(36_608_000L, 36_000_000L, 36_000_000L, null, 36_608_000L)]
    // 100714 green, "2M 400K" — one of the two mismatches left after voting alone.
    [InlineData(2_400_000L, null, 2_000_000L, null, 2_400_000L)]
    // 235757 blue, "4M 680K" — the other one.
    [InlineData(4_680_000L, null, 4_000_000L, null, 4_680_000L)]
    // 191638 blue: the lost-K-suffix shape (23,000,040) survives untouched,
    // but promoting its sibling gives the correct figure the majority.
    [InlineData(23_040_000L, 23_000_040L, 23_000_000L, null, 23_040_000L)]
    public void MostSupportedPromotesATruncatedReadingToTheFullerOne(
        long expected,
        long? tableCrop,
        long? tableTokens,
        long? dialogTokens,
        long? dialogCrop)
    {
        Assert.Equal(expected, LampXp.MostSupported(tableCrop, tableTokens, dialogTokens, dialogCrop));
    }

    [Fact]
    public void MostSupportedLeavesAMisreadLeadingDigitToTheVote()
    {
        // 140305 blue: "7M 776K" read as 9M by the table crop. 9,776,000 is
        // larger but is not 7,776,000 with a group zeroed, so promotion must
        // not touch it — "prefer the larger figure" would pick the wrong one.
        Assert.Equal(7_776_000, LampXp.MostSupported(9_776_000, 7_776_000, null, 7_776_000));
        Assert.Equal(9_776_000, LampXp.MostSupported(9_776_000, 7_776_000, null, null));
    }

    [Fact]
    public void MostSupportedDoesNotPromoteIntoAUnitsRemainderMisread()
    {
        // 191706 red: true figure 17,888,000, and one source reads
        // "17M 888K" with the K lost into the units as 17,000,888. Promoting
        // on a 1,000 scale would make the two round readings truncations of
        // that garbage and hand it a 3-0 win; lamp rows only ever print M and
        // K groups, so nothing legitimately loses just a units group.
        Assert.Equal(17_000_000, LampXp.MostSupported(17_000_888, 17_000_000, 17_000_000, null));

        // The real figure still wins when a source actually read it.
        Assert.Equal(17_888_000, LampXp.MostSupported(17_000_888, 17_000_000, 17_000_000, 17_888_000));
    }

    [Fact]
    public void MostSupportedNeverPromotesZero()
    {
        // Zero is arithmetically a truncation of every figure below the next
        // magnitude, so without the guard an empty lamp row plus one stray
        // reading would be promoted into that reading.
        Assert.Equal(0, LampXp.MostSupported(0, 0, null, 500_000));
        Assert.Equal(0, LampXp.MostSupported(0, 0, 0, 36_608_000));
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
