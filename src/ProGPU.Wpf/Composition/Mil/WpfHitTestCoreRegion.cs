using PortableRect = global::ProGPU.Wpf.Interop.PortableRect;
using IPortableHitTestCoreSource = global::ProGPU.Wpf.Interop.IPortableHitTestCoreSource;
using IPortablePointHitRegionSource = global::ProGPU.Wpf.Interop.IPortablePointHitRegionSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>
/// The optional point-hit region of a visual whose type overrides <c>HitTestCore</c>
/// (<see cref="IPortableHitTestCoreSource"/>). Consulted only when the visual is not an
/// <see cref="IPortablePointHitRegionSource"/>, whose region is authoritative. Unlike that
/// contract, a missing or unusable region is not an error: the visual keeps its drawn coverage.
/// </summary>
internal static class WpfHitTestCoreRegion
{
    public static bool TryGet(object visual, out PortableRect region)
    {
        region = default;
        if (visual is IPortablePointHitRegionSource ||
            visual is not IPortableHitTestCoreSource source ||
            !source.TryGetPortableHitTestCoreRegion(out region))
        {
            return false;
        }

        return !region.IsEmpty &&
            double.IsFinite(region.X) && double.IsFinite(region.Y) &&
            double.IsFinite(region.Width) && double.IsFinite(region.Height) &&
            region.Width > 0 && region.Height > 0 &&
            Math.Abs(region.X) <= float.MaxValue && Math.Abs(region.Y) <= float.MaxValue &&
            region.Width <= float.MaxValue && region.Height <= float.MaxValue &&
            Math.Abs(region.X + region.Width) <= float.MaxValue &&
            Math.Abs(region.Y + region.Height) <= float.MaxValue;
    }
}
