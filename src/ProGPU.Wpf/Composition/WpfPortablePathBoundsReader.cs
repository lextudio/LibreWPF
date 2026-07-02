using System;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortablePathBoundsReader
{
    public static bool TryGetLineBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!geometry.Transform.IsIdentity
            || geometry.Kind != PortableGeometryPathKind.Path
            || geometry.Figures.Length == 0)
        {
            return false;
        }

        var hasPoint = false;
        var left = 0.0;
        var top = 0.0;
        var right = 0.0;
        var bottom = 0.0;

        foreach (var figure in geometry.Figures)
        {
            if (figure.Segments.Length == 0)
            {
                return false;
            }

            if (!TryIncludePoint(figure.StartPoint, ref hasPoint, ref left, ref top, ref right, ref bottom))
            {
                return false;
            }

            foreach (var segment in figure.Segments)
            {
                if (segment.Kind != PortablePathSegmentKind.Line
                    || (!segment.IsStroked && !figure.IsFilled)
                    || !TryIncludePoint(segment.Point1, ref hasPoint, ref left, ref top, ref right, ref bottom))
                {
                    return false;
                }
            }
        }

        var width = right - left;
        var height = bottom - top;
        if (!hasPoint
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || (width == 0 && height == 0))
        {
            return false;
        }

        bounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool TryIncludePoint(
        PortablePoint point,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        if (!hasPoint)
        {
            left = point.X;
            top = point.Y;
            right = point.X;
            bottom = point.Y;
            hasPoint = true;
            return true;
        }

        left = Math.Min(left, point.X);
        top = Math.Min(top, point.Y);
        right = Math.Max(right, point.X);
        bottom = Math.Max(bottom, point.Y);
        return true;
    }
}
