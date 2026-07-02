using System;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaLineSegment = System.Windows.Media.LineSegment;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfMediaRectangleClipReader
{
    public static bool TryGetRectangleClipBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!HasIdentityGeometryTransform(geometry))
        {
            return false;
        }

        if (geometry is MediaRectangleGeometry rectangleGeometry)
        {
            return rectangleGeometry.RadiusX == 0
                && rectangleGeometry.RadiusY == 0
                && TryCreateUsableRect(rectangleGeometry.Rect, out bounds);
        }

        return geometry is MediaPathGeometry pathGeometry
            && TryGetRectanglePathBounds(pathGeometry, out bounds);
    }

    private static bool HasIdentityGeometryTransform(MediaGeometry geometry)
    {
        var transform = geometry.Transform;
        return transform == null
            || (WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
                && WpfResourceResolver.IsIdentityMatrix(matrix));
    }

    private static bool TryGetRectanglePathBounds(MediaPathGeometry pathGeometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (pathGeometry.Figures.Count != 1)
        {
            return false;
        }

        var figure = pathGeometry.Figures[0];
        if (!figure.IsClosed || !figure.IsFilled)
        {
            return false;
        }

        var segmentCount = figure.Segments.Count;
        if (segmentCount is not (3 or 4))
        {
            return false;
        }

        var points = new Point[4];
        points[0] = figure.StartPoint;
        for (var i = 0; i < 3; i++)
        {
            if (figure.Segments[i] is not MediaLineSegment lineSegment)
            {
                return false;
            }

            points[i + 1] = lineSegment.Point;
        }

        if (segmentCount == 4)
        {
            if (figure.Segments[3] is not MediaLineSegment closingSegment
                || !NearlyEqual(closingSegment.Point.X, points[0].X)
                || !NearlyEqual(closingSegment.Point.Y, points[0].Y))
            {
                return false;
            }
        }

        return TryCreateRectangleFromPolygon(points, out bounds);
    }

    private static bool TryCreateRectangleFromPolygon(Point[] points, out WpfReplayRect bounds)
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

        var hasTopLeft = false;
        var hasTopRight = false;
        var hasBottomRight = false;
        var hasBottomLeft = false;
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var isLeft = NearlyEqual(point.X, left);
            var isRight = NearlyEqual(point.X, right);
            var isTop = NearlyEqual(point.Y, top);
            var isBottom = NearlyEqual(point.Y, bottom);
            var isOnVerticalEdge = isLeft || isRight;
            var isOnHorizontalEdge = isTop || isBottom;
            if (!isOnVerticalEdge || !isOnHorizontalEdge)
            {
                return false;
            }

            if (isLeft && isTop)
            {
                if (hasTopLeft)
                {
                    return false;
                }

                hasTopLeft = true;
            }
            else if (isRight && isTop)
            {
                if (hasTopRight)
                {
                    return false;
                }

                hasTopRight = true;
            }
            else if (isRight && isBottom)
            {
                if (hasBottomRight)
                {
                    return false;
                }

                hasBottomRight = true;
            }
            else if (isLeft && isBottom)
            {
                if (hasBottomLeft)
                {
                    return false;
                }

                hasBottomLeft = true;
            }
            else
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

        if (hasTopLeft && hasTopRight && hasBottomRight && hasBottomLeft)
        {
            bounds = new WpfReplayRect(left, top, width, height);
            return true;
        }

        return false;
    }

    private static bool TryCreateUsableRect(Rect rect, out WpfReplayRect bounds)
    {
        bounds = new WpfReplayRect(rect.X, rect.Y, rect.Width, rect.Height);
        return !rect.IsEmpty
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }
}
