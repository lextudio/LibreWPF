using System;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortableRectangleClipReader
{
    public static bool TryGetRectangleClipBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!geometry.Transform.IsIdentity
            || geometry.Kind != PortableGeometryPathKind.Path
            || geometry.Figures.Length != 1)
        {
            return false;
        }

        var figure = geometry.Figures[0];
        if (!figure.IsClosed || !figure.IsFilled)
        {
            return false;
        }

        var segmentCount = figure.Segments.Length;
        if (segmentCount is not (3 or 4))
        {
            return false;
        }

        var points = new PortablePoint[4];
        points[0] = figure.StartPoint;
        for (var i = 0; i < 3; i++)
        {
            var segment = figure.Segments[i];
            if (segment.Kind != PortablePathSegmentKind.Line)
            {
                return false;
            }

            points[i + 1] = segment.Point1;
        }

        if (segmentCount == 4)
        {
            var segment = figure.Segments[3];
            if (segment.Kind != PortablePathSegmentKind.Line
                || !NearlyEqual(segment.Point1.X, points[0].X)
                || !NearlyEqual(segment.Point1.Y, points[0].Y))
            {
                return false;
            }
        }

        return TryCreateRectangleClipFromPolygon(points, out bounds);
    }

    private static bool TryCreateRectangleClipFromPolygon(PortablePoint[] points, out WpfReplayRect bounds)
    {
        bounds = default;
        var left = points[0].X;
        var top = points[0].Y;
        var right = points[0].X;
        var bottom = points[0].Y;
        for (var i = 1; i < points.Length; i++)
        {
            var point = points[i];
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        var width = right - left;
        var height = bottom - top;
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var isOnVerticalEdge = NearlyEqual(point.X, left) || NearlyEqual(point.X, right);
            var isOnHorizontalEdge = NearlyEqual(point.Y, top) || NearlyEqual(point.Y, bottom);
            if (!isOnVerticalEdge || !isOnHorizontalEdge)
            {
                return false;
            }

            var next = points[(i + 1) % points.Length];
            var sameX = NearlyEqual(point.X, next.X);
            var sameY = NearlyEqual(point.Y, next.Y);
            if (sameX == sameY)
            {
                return false;
            }
        }

        bounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }
}
