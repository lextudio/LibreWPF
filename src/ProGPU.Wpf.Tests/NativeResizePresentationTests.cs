using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class NativeResizePresentationTests
{
    private static readonly NativeResizePresentation.Geometry Initial = new(744, 521, 744, 521, 1);
    private static readonly NativeResizePresentation.Geometry Resized = new(884, 601, 884, 601, 1);

    [Fact]
    public void AssignedGeometryDoesNotAcknowledgeAnUnpresentedResize()
        => Assert.False(NativeResizePresentation.IsReady(5, 5, true, Resized, Initial));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public void MatchingGeometryRequiresANewPresentedFrame(long current)
        => Assert.False(NativeResizePresentation.IsReady(5, current, true, Resized, Resized));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryActualPresentedGeometryFieldAndAvailabilityMustMatch(int changed)
    {
        var actual = changed switch
        {
            0 => Resized with { LogicalWidth = 883 },
            1 => Resized with { LogicalHeight = 600 },
            2 => Resized with { PixelWidth = 883 },
            3 => Resized with { PixelHeight = 600 },
            4 => Resized with { DpiScale = 1.000001 },
            _ => Resized
        };
        Assert.False(NativeResizePresentation.IsReady(5, 6, changed != 5, Resized, actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MatchingInvalidDpiCannotAcknowledgeAResize(double dpi)
    {
        var geometry = Resized with { DpiScale = dpi };
        Assert.False(NativeResizePresentation.IsReady(5, 6, true, geometry, geometry));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MatchingZeroDimensionsCannotAcknowledgeAResize(int dimension)
    {
        var geometry = dimension switch
        {
            0 => Resized with { LogicalWidth = 0 },
            1 => Resized with { LogicalHeight = 0 },
            2 => Resized with { PixelWidth = 0 },
            _ => Resized with { PixelHeight = 0 }
        };
        Assert.False(NativeResizePresentation.IsReady(5, 6, true, geometry, geometry));
    }

    [Fact]
    public void OneMatchingPresentationAcknowledgesWithoutWaitingForQuiet()
    {
        Assert.True(NativeResizePresentation.IsReady(5, 6, true, Resized, Resized));
        Assert.True(NativeResizePresentation.IsReady(5, 7, true, Resized, Resized));
        Assert.False(NativeResizePresentation.IsReady(-1, 6, true, Resized, Resized));
    }

    [Fact]
    public void RestoreRequiresANewMatchingFrameNotAnOldInitialSnapshot()
    {
        Assert.False(NativeResizePresentation.IsReady(6, 5, true, Initial, Initial));
        Assert.False(NativeResizePresentation.IsReady(6, 7, true, Initial, Resized));
        Assert.True(NativeResizePresentation.IsReady(6, 7, true, Initial, Initial));
    }

    [Fact]
    public void HighDpiKeepsLogicalAndPhysicalDimensionsDistinct()
    {
        NativeResizePresentation.Geometry geometry = new(884, 601, 1768, 1202, 2);
        Assert.True(NativeResizePresentation.IsReady(5, 6, true, geometry, geometry));
        Assert.False(NativeResizePresentation.IsReady(5, 6, true, geometry,
            geometry with { LogicalWidth = 1768, LogicalHeight = 1202 }));
    }
}
