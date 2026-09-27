using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class NativeDragInputReceiptTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("U", false)]
    [InlineData("D", false)]
    [InlineData("UD", false)]
    [InlineData("DU", true)]
    [InlineData("DUU", true)]
    [InlineData("DUD", false)]
    [InlineData("DUDU", true)]
    public void CompletionRequiresDriverAndDeliveredPressRelease(string events, bool released)
    {
        var receipt = new NativeDragInputReceipt();
        foreach (char buttonEvent in events)
            receipt.ObserveLeftButton(buttonEvent == 'D');

        Assert.Equal(released, receipt.HasSourceRelease);
        Assert.False(receipt.IsComplete(driverCompleted: false));
        Assert.Equal(released, receipt.IsComplete(driverCompleted: true));
    }

    [Fact]
    public void DriverCompletionBeforeQueuedMouseUpCannotAdvanceToPopupValidation()
    {
        var receipt = new NativeDragInputReceipt();
        Assert.False(receipt.IsComplete(driverCompleted: true));
        receipt.ObserveLeftButton(pressed: true);
        Assert.False(receipt.IsComplete(driverCompleted: true));
        receipt.ObserveLeftButton(pressed: false);
        Assert.True(receipt.IsComplete(driverCompleted: true));
    }

    [Fact]
    public async Task DeliveredReleaseCanBeObservedByProbeTaskBeforeDriverAcknowledgment()
    {
        var receipt = new NativeDragInputReceipt();
        await Task.Run(() =>
        {
            receipt.ObserveLeftButton(pressed: true);
            receipt.ObserveLeftButton(pressed: false);
        });
        Assert.True(receipt.HasSourceRelease);
        Assert.False(receipt.IsComplete(driverCompleted: false));
        Assert.True(receipt.IsComplete(driverCompleted: true));
    }
}
