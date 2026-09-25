using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class SaveGateTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AgreeingFrameIsGreenAndSavable()
    {
        var decision = SaveGate.Evaluate(TestReports.Open(), At);

        Assert.True(decision.CanSave);
        Assert.Equal(TrafficLight.Green, decision.Light);
        Assert.NotNull(decision.Totals);
    }

    [Fact]
    public void ASingleFrameIsEnoughNoRepetitionRequired()
    {
        // A player who stopped farming produces identical frames, so waiting
        // for repeated reads would only re-confirm a misread. One frame whose
        // own reads agree is the strongest evidence available.
        var decision = SaveGate.Evaluate(TestReports.Open(), At);

        Assert.True(decision.CanSave);
    }

    [Fact]
    public void AdenaDisagreementBlocksTheSave()
    {
        var report = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: true,
                PlayTimeDisagreed: false));

        var decision = SaveGate.Evaluate(report, At);

        Assert.False(decision.CanSave);
        Assert.Equal(TrafficLight.Red, decision.Light);
        Assert.Contains("Adena", decision.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void XpDigitCountDisagreementBlocksTheSave()
    {
        var report = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: true,
                XpSpliced: false,
                XpMagnitudeMismatch: true,
                AdenaDisagreed: false,
                PlayTimeDisagreed: false));

        var decision = SaveGate.Evaluate(report, At);

        Assert.False(decision.CanSave);
        Assert.Contains("digits", decision.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void SplicedXpWarnsButStillSaves()
    {
        var report = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: true,
                XpSpliced: true,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: false,
                PlayTimeDisagreed: false));

        var decision = SaveGate.Evaluate(report, At);

        Assert.True(decision.CanSave);
        Assert.Equal(TrafficLight.Orange, decision.Light);
        Assert.False(decision.Issue!.BlocksSave);
    }

    [Fact]
    public void ClosedLampPanelIsOrangeNotRed()
    {
        var decision = SaveGate.Evaluate(TestReports.ClosedPanel(), At);

        Assert.False(decision.CanSave);
        Assert.Equal(TrafficLight.Orange, decision.Light);
    }

    [Fact]
    public void ALaterFrameOfTheSamePanelIsStillSavable()
    {
        // Duplicate POSTs are allowed: the player may want several logs from
        // one in-game session, including the same stretch more than once.
        var laterFrame = TestReports.Open(xp: 4_390_000, minutes: 139);

        var decision = SaveGate.Evaluate(laterFrame, At);

        Assert.True(decision.CanSave);
        Assert.NotNull(decision.Totals);
    }

    [Fact]
    public void ADisputedXpNamesBothFiguresAndTheOneBeingSaved()
    {
        var report = TestReports.Open(
            xp: 9_210_400,
            confidence: new ReadConfidence(
                XpDisagreed: true,
                XpSpliced: true,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: false,
                PlayTimeDisagreed: false,
                XpFromTokens: 4_210_400,
                XpFromCrop: 9_210_400));

        var decision = SaveGate.Evaluate(report, At);

        Assert.True(decision.CanSave);
        var message = decision.Issue!.Message;
        Assert.Contains("4,210,400", message, StringComparison.Ordinal);
        Assert.Contains("9,210,400", message, StringComparison.Ordinal);
        Assert.Contains("spliced", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockedAdenaSaysWhichTwoFiguresDisagreed()
    {
        var report = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: true,
                PlayTimeDisagreed: false,
                AdenaFromTokens: 883_500,
                AdenaFromCrop: 88_350));

        var decision = SaveGate.Evaluate(report, At);

        Assert.False(decision.CanSave);
        Assert.Contains("883,500", decision.BlockReason, StringComparison.Ordinal);
        Assert.Contains("88,350", decision.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingSecondFigureStillProducesAReadableMessage()
    {
        var report = TestReports.Open(
            confidence: new ReadConfidence(
                XpDisagreed: false,
                XpSpliced: false,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: true,
                PlayTimeDisagreed: false));

        var decision = SaveGate.Evaluate(report, At);

        Assert.False(decision.CanSave);
        Assert.Contains("Adena's two reads disagreed.", decision.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void NoReadYetIsIdle()
    {
        var decision = SaveGate.Evaluate(null, At);

        Assert.False(decision.CanSave);
        Assert.Equal(TrafficLight.Idle, decision.Light);
    }

    [Fact]
    public void AHeldFallbackSaysWhyItPassedOverTheCurrentFrameAndNothingElse()
    {
        // The live card already shows the held frame's figures, so the gate
        // only says why the current frame was passed over.
        var held = TestReports.Open(xp: 1_000_000, minutes: 60);
        var current = TestReports.Open(xp: 4_390_000, minutes: 139);

        var decision = SaveGate.EvaluateWithHold(
            current,
            At,
            held,
            At.AddMinutes(-1),
            currentAccepted: false);

        Assert.True(decision.CanSave);
        Assert.True(decision.UsedHeldRead);
        Assert.Equal(held, decision.Source);
        Assert.Equal(1_000, decision.Totals!.XpFarmed);
        Assert.Equal(TrafficLight.Red, decision.Light);
        Assert.Contains("not accepted", decision.HoldReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosedLampPanelFallsBackToTheHeldOpenPanel()
    {
        var held = TestReports.Open(xp: 2_000_000, minutes: 90);
        var current = TestReports.ClosedPanel(xp: 2_100_000, minutes: 95);

        var decision = SaveGate.EvaluateWithHold(current, At, held, At);

        Assert.True(decision.CanSave);
        Assert.True(decision.UsedHeldRead);
        Assert.Equal(2_000, decision.Totals!.XpFarmed);
        Assert.Equal(TrafficLight.Orange, decision.Light);
        Assert.Contains("Magic Lamp", decision.HoldReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AWithdrawnLampColumnDoesNotExplainTheHold()
    {
        var held = TestReports.Open(xp: 1_000_000, minutes: 30, green: 256_000);
        var withdrawn = LampContinuity.Withdraw(
            held,
            held,
            TestReports.Open(xp: 1_200_000, minutes: 31, green: 0));

        var decision = SaveGate.EvaluateWithHold(withdrawn, At, held, At);

        Assert.True(decision.CanSave);
        Assert.True(decision.UsedHeldRead);
        Assert.Equal(held, decision.Source);
        Assert.Null(decision.HoldReason);
        Assert.Equal(TrafficLight.Red, decision.Light);
    }

    [Fact]
    public void AWithdrawnLampColumnWithUnreadXpStillExplainsTheHold()
    {
        // Describe reads UnreadFields before the lamp column, so the farm
        // defect has to be on that list. Clearing Xp alone would leave the
        // withdrawal as the only announced defect.
        var held = TestReports.Open(xp: 1_000_000, minutes: 30, green: 256_000);
        var withdrawn = LampContinuity.Withdraw(
            held,
            held,
            TestReports.Open(xp: 1_200_000, minutes: 31, green: 0));
        var alsoUnreadXp = withdrawn with { Xp = null, UnreadFields = ["XP"] };
        Assert.True(LampContinuity.WasWithdrawn(alsoUnreadXp));

        var decision = SaveGate.EvaluateWithHold(alsoUnreadXp, At, held, At);

        Assert.True(decision.CanSave);
        Assert.True(decision.UsedHeldRead);
        Assert.Equal(held, decision.Source);
        Assert.Equal("Couldn't read XP.", decision.HoldReason);
    }

    [Fact]
    public void AGenuinelyUnreadLampColumnStillExplainsTheHold()
    {
        var held = TestReports.Open(xp: 1_000_000, minutes: 30);
        var unread = TestReports.UnreadLamps(xp: 1_200_000, minutes: 31);

        var decision = SaveGate.EvaluateWithHold(unread, At, held, At);

        Assert.True(decision.CanSave);
        Assert.Equal(held, decision.Source);
        Assert.Contains("Magic Lamp XP column", decision.HoldReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnacceptedFrameDoesNotBeatTheHoldEvenWhenInFrameItAgrees()
    {
        var held = TestReports.Open(xp: 2_000_000, minutes: 90);
        var dropped = TestReports.Open(xp: 1_000_000, minutes: 90);

        var decision = SaveGate.EvaluateWithHold(
            dropped,
            At,
            held,
            At,
            currentAccepted: false);

        Assert.True(decision.CanSave);
        Assert.True(decision.UsedHeldRead);
        Assert.Equal(held, decision.Source);
        Assert.Equal(2_000, decision.Totals!.XpFarmed);
        Assert.Contains("not accepted", decision.HoldReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrustworthyCurrentFrameIsPreferredOverTheHold()
    {
        var held = TestReports.Open(xp: 1_000_000, minutes: 60);
        var current = TestReports.Open(xp: 4_390_000, minutes: 139);

        var decision = SaveGate.EvaluateWithHold(current, At, held, At);

        Assert.True(decision.CanSave);
        Assert.False(decision.UsedHeldRead);
        Assert.Equal(current, decision.Source);
        Assert.Equal(4_390, decision.Totals!.XpFarmed);
    }

    [Fact]
    public void HoldIsIgnoredWhenItWouldNotPassTheGate()
    {
        var held = TestReports.ClosedPanel();
        var current = TestReports.ClosedPanel(xp: 2_000_000);

        var decision = SaveGate.EvaluateWithHold(current, At, held, At);

        Assert.False(decision.CanSave);
        Assert.False(decision.UsedHeldRead);
        Assert.Equal(TrafficLight.Orange, decision.Light);
    }

    [Fact]
    public void NoCurrentReadStillSavesTheHeldFrame()
    {
        var held = TestReports.Open();

        var decision = SaveGate.EvaluateWithHold(null, At, held, At);

        Assert.True(decision.CanSave);
        Assert.True(decision.UsedHeldRead);
        Assert.Equal(TrafficLight.Green, decision.Light);
        Assert.Equal(held, decision.Source);
        // No current frame is already the live-status line ("Game not running.").
        // A filler HoldReason here composed into a second, invented defect.
        Assert.Null(decision.HoldReason);
    }
}
