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
        if (!HasIdentityGeometryTransform(geometry))
        {
            startPoint = default;
            endPoint = default;
            return false;
        }

        if (geometry is MediaLineGeometry lineGeometry
            && IsUsablePoint(lineGeometry.StartPoint, out startPoint)
            && IsUsablePoint(lineGeometry.EndPoint, out endPoint))
        {
            return true;
        }

        if (geometry is MediaPathGeometry pathGeometry)
        {
            return TryGetPathLinePoints(pathGeometry, out startPoint, out endPoint);
        }

        startPoint = default;
        endPoint = default;
        return false;
    }

    private static bool TryGetPathLinePoints(
        MediaPathGeometry pathGeometry,
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
            && IsUsablePoint(lineSegment.Point, out endPoint))
        {
            return true;
        }

        startPoint = default;
        endPoint = default;
        return false;
    }

    private static bool HasIdentityGeometryTransform(MediaGeometry geometry)
    {
        var transform = geometry.Transform;
        return transform == null
            || (WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
                && WpfResourceResolver.IsIdentityMatrix(matrix));
    }

    private static bool IsUsablePoint(Point point, out Point usablePoint)
    {
        usablePoint = point;
        return double.IsFinite(point.X)
            && double.IsFinite(point.Y);
    }
}
