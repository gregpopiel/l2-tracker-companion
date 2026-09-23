using L2TrackerCompanion.Session;
using Xunit;

namespace L2TrackerCompanion.Session.Tests;

public class SaveConfirmationHoldTests
{
    [Fact]
    public void ASuccessfulSaveStopsTrackingWhenItWasRunning()
    {
        Assert.True(SaveConfirmationHold.ShouldStopTracking(wasTracking: true, saved: true));
        Assert.False(SaveConfirmationHold.ShouldStopTracking(wasTracking: false, saved: true));
        Assert.False(SaveConfirmationHold.ShouldStopTracking(wasTracking: true, saved: false));
    }

    [Fact]
    public void ASuccessfulSaveIgnoresIncomingReadsUntilReleased()
    {
        var hold = new SaveConfirmationHold();
        hold.BeginSave();
        hold.Saved();

        Assert.True(hold.Active);
        Assert.True(hold.IgnoreIncomingReads);

        hold.Release();

        Assert.False(hold.Active);
        Assert.False(hold.IgnoreIncomingReads);
    }

    [Fact]
    public void StartingAnotherSaveDropsThePreviousConfirmation()
    {
        var hold = new SaveConfirmationHold();
        hold.Saved();
        hold.BeginSave();

        Assert.False(hold.Active);
        Assert.False(hold.IgnoreIncomingReads);
    }
}
