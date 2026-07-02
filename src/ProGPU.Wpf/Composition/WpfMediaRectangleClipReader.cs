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
        if (!TryGetAxisAlignedGeometryTransform(geometry, out var transform))
        {
            return false;
        }

        if (geometry is MediaRectangleGeometry rectangleGeometry)
        {
            return rectangleGeometry.RadiusX == 0
                && rectangleGeometry.RadiusY == 0
                && TryCreateUsableRect(rectangleGeometry.Rect, out bounds)
                && TryTransformAxisAlignedBounds(bounds, transform, out bounds);
        }

        return geometry is MediaPathGeometry pathGeometry
            && TryGetRectanglePathBounds(
                pathGeometry,
                requireFilled: true,
                requireStrokedSegments: false,
                out bounds)
            && TryTransformAxisAlignedBounds(bounds, transform, out bounds);
    }

    public static bool TryGetRectangleStrokeBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!TryGetAxisAlignedGeometryTransform(geometry, out var transform))
        {
            return false;
        }

        return geometry is MediaPathGeometry pathGeometry
            && TryGetRectanglePathBounds(
                pathGeometry,
                requireFilled: false,
                requireStrokedSegments: true,
                out bounds)
            && TryTransformAxisAlignedBounds(bounds, transform, out bounds);
    }

    private static bool TryGetAxisAlignedGeometryTransform(MediaGeometry geometry, out System.Numerics.Matrix4x4 transform)
    {
        var transformValue = geometry.Transform;
        if (transformValue == null)
        {
            transform = System.Numerics.Matrix4x4.Identity;
            return true;
        }

        if (!WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
        {
            return false;
        }

        return IsAxisAlignedTransform(transform);
    }

    private static bool IsAxisAlignedTransform(System.Numerics.Matrix4x4 transform)
    {
        return transform.M12 == 0.0f
            && transform.M21 == 0.0f
            && float.IsFinite(transform.M11)
            && float.IsFinite(transform.M22)
            && float.IsFinite(transform.M41)
            && float.IsFinite(transform.M42);
    }

    private static bool TryTransformAxisAlignedBounds(
        WpfReplayRect bounds,
        System.Numerics.Matrix4x4 transform,
        out WpfReplayRect transformedBounds)
    {
        transformedBounds = default;
        var x0 = (bounds.X * transform.M11) + transform.M41;
        var x1 = ((bounds.X + bounds.Width) * transform.M11) + transform.M41;
        var y0 = (bounds.Y * transform.M22) + transform.M42;
        var y1 = ((bounds.Y + bounds.Height) * transform.M22) + transform.M42;
        var left = Math.Min(x0, x1);
        var top = Math.Min(y0, y1);
        var right = Math.Max(x0, x1);
        var bottom = Math.Max(y0, y1);
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

        transformedBounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool TryGetRectanglePathBounds(
        MediaPathGeometry pathGeometry,
        bool requireFilled,
        bool requireStrokedSegments,
        out WpfReplayRect bounds)
    {
        bounds = default;
        if (pathGeometry.Figures.Count != 1)
        {
            return false;
        }

        var figure = pathGeometry.Figures[0];
        if (!figure.IsClosed || (requireFilled && !figure.IsFilled))
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
            if (figure.Segments[i] is not MediaLineSegment lineSegment
                || (requireStrokedSegments && !lineSegment.IsStroked))
            {
                return false;
            }

            points[i + 1] = lineSegment.Point;
        }

        if (segmentCount == 4)
        {
            if (figure.Segments[3] is not MediaLineSegment closingSegment
                || (requireStrokedSegments && !closingSegment.IsStroked)
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
