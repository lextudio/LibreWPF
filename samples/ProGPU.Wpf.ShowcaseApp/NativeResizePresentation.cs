namespace ProGPU.Wpf.ShowcaseApp;

// Read-only acknowledgement of one requested resize, not a quiet-state test.
// The caller supplies the actual host's retained last-presented frame and
// current surface geometry on their common owner thread.
internal static class NativeResizePresentation
{
    internal readonly record struct Geometry(uint LogicalWidth, uint LogicalHeight,
        uint PixelWidth, uint PixelHeight, double DpiScale)
    {
        internal bool IsValid => LogicalWidth > 0 && LogicalHeight > 0 &&
            PixelWidth > 0 && PixelHeight > 0 && DpiScale > 0 && double.IsFinite(DpiScale);
    }

    internal static bool IsReady(long previousFrameCount, long currentFrameCount,
        bool hasPresentedFrame, Geometry expected, Geometry presented)
        => previousFrameCount >= 0 && currentFrameCount > previousFrameCount &&
            hasPresentedFrame && expected.IsValid && presented == expected;
}
