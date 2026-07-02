using System.Collections.Generic;
using System.Numerics;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaLineGeometry = System.Windows.Media.LineGeometry;
using MediaLineSegment = System.Windows.Media.LineSegment;
using MediaPathGeometry = System.Windows.Media.PathGeometry;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfMediaLineGeometryReader
{
    public static bool TryGetLinePoints(
        MediaGeometry geometry,
        out Point startPoint,
        out Point endPoint)
    {
        if (!TryGetGeometryTransform(geometry, out var transform))
        {
            startPoint = default;
            endPoint = default;
            return false;
        }

        if (geometry is MediaLineGeometry lineGeometry
            && IsUsablePoint(lineGeometry.StartPoint, out startPoint)
            && IsUsablePoint(lineGeometry.EndPoint, out endPoint)
            && TryTransformPoint(startPoint, transform, out startPoint)
            && TryTransformPoint(endPoint, transform, out endPoint))
        {
            return true;
        }

        if (geometry is MediaPathGeometry pathGeometry)
        {
            return TryGetPathLinePoints(pathGeometry, transform, out startPoint, out endPoint);
        }

        startPoint = default;
        endPoint = default;
        return false;
    }

    public static bool TryGetPolylineSegments(
        MediaGeometry geometry,
        out IReadOnlyList<WpfReplayLineSegment> segments)
    {
        if (!TryGetGeometryTransform(geometry, out var transform)
            || geometry is not MediaPathGeometry pathGeometry)
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        return TryGetPathPolylineSegments(pathGeometry, transform, out segments);
    }

    private static bool TryGetPathLinePoints(
        MediaPathGeometry pathGeometry,
        Matrix4x4 transform,
        out Point startPoint,
        out Point endPoint)
    {
        if (pathGeometry.Figures.Count != 1)
        {
            startPoint = default;
            endPoint = default;
            return false;
        }

        var figure = pathGeometry.Figures[0];
        if (figure.IsClosed || figure.Segments.Count != 1)
        {
            startPoint = default;
            endPoint = default;
            return false;
        }

        if (figure.Segments[0] is MediaLineSegment lineSegment
            && lineSegment.IsStroked
            && IsUsablePoint(figure.StartPoint, out startPoint)
            && IsUsablePoint(lineSegment.Point, out endPoint)
            && TryTransformPoint(startPoint, transform, out startPoint)
            && TryTransformPoint(endPoint, transform, out endPoint))
        {
            return true;
        }

        startPoint = default;
        endPoint = default;
        return false;
    }

    private static bool TryGetPathPolylineSegments(
        MediaPathGeometry pathGeometry,
        Matrix4x4 transform,
        out IReadOnlyList<WpfReplayLineSegment> segments)
    {
        if (pathGeometry.Figures.Count != 1)
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        var figure = pathGeometry.Figures[0];
        var segmentCount = figure.Segments.Count;
        if (segmentCount < 2)
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        if (figure.IsClosed
            && WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(pathGeometry, out _))
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        if (!IsUsablePoint(figure.StartPoint, out var startPoint)
            || !TryTransformPoint(startPoint, transform, out startPoint))
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        var currentPoint = startPoint;
        var lineSegments = new WpfReplayLineSegment[segmentCount + (figure.IsClosed ? 1 : 0)];
        var writtenSegmentCount = 0;
        for (var i = 0; i < segmentCount; i++)
        {
            if (figure.Segments[i] is not MediaLineSegment lineSegment
                || !lineSegment.IsStroked
                || !IsUsablePoint(lineSegment.Point, out var nextPoint)
                || !TryTransformPoint(nextPoint, transform, out nextPoint))
            {
                segments = Array.Empty<WpfReplayLineSegment>();
                return false;
            }

            lineSegments[writtenSegmentCount++] = new WpfReplayLineSegment(
                new WpfReplayPoint(currentPoint.X, currentPoint.Y),
                new WpfReplayPoint(nextPoint.X, nextPoint.Y));
            currentPoint = nextPoint;
        }

        if (figure.IsClosed && !SamePoint(currentPoint, startPoint))
        {
            lineSegments[writtenSegmentCount++] = new WpfReplayLineSegment(
                new WpfReplayPoint(currentPoint.X, currentPoint.Y),
                new WpfReplayPoint(startPoint.X, startPoint.Y));
        }

        if (writtenSegmentCount != lineSegments.Length)
        {
            Array.Resize(ref lineSegments, writtenSegmentCount);
        }

        segments = lineSegments;
        return true;
    }

    private static bool TryGetGeometryTransform(MediaGeometry geometry, out Matrix4x4 transform)
    {
        var transformValue = geometry.Transform;
        if (transformValue == null)
        {
            transform = Matrix4x4.Identity;
            return true;
        }

        return WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform);
    }

    private static bool IsUsablePoint(Point point, out Point usablePoint)
    {
        usablePoint = point;
        return double.IsFinite(point.X)
            && double.IsFinite(point.Y);
    }

    private static bool TryTransformPoint(Point point, Matrix4x4 transform, out Point transformedPoint)
    {
        var x = (point.X * transform.M11) + (point.Y * transform.M21) + transform.M41;
        var y = (point.X * transform.M12) + (point.Y * transform.M22) + transform.M42;
        transformedPoint = new Point(x, y);
        return double.IsFinite(x)
            && double.IsFinite(y);
    }

    private static bool SamePoint(Point left, Point right)
    {
        return left.X == right.X
            && left.Y == right.Y;
    }
}

internal readonly record struct WpfReplayLineSegment(
    WpfReplayPoint StartPoint,
    WpfReplayPoint EndPoint);
