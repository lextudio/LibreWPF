using System;
using System.Numerics;
using ProGPU.Wpf.Interop;
using VectorArcSegment = ProGPU.Vector.ArcSegment;
using VectorCubicBezierSegment = ProGPU.Vector.CubicBezierSegment;
using VectorFillRule = ProGPU.Vector.FillRule;
using VectorLineSegment = ProGPU.Vector.LineSegment;
using VectorPathFigure = ProGPU.Vector.PathFigure;
using VectorPathGeometry = ProGPU.Vector.PathGeometry;
using VectorQuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;
using VectorSweepDirection = ProGPU.Vector.SweepDirection;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortablePathGeometryConverter
{
    public static bool TryConvert(
        PortableGeometryPath portablePath,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        out WpfReplayRect bounds)
    {
        path = Convert(portablePath);
        if (!transform.IsIdentity)
        {
            path = path.CreateTransformed(transform);
        }

        bounds = GetBoundsOrEmpty(path);
        return true;
    }

    public static bool TryGetNativePathBounds(PortableGeometryPath portablePath, out WpfReplayRect bounds)
    {
        if (!TryConvert(portablePath, Matrix4x4.Identity, out _, out bounds)
            || !IsUsableBounds(bounds))
        {
            bounds = default;
            return false;
        }

        return true;
    }

    public static WpfReplayRect GetBoundsOrEmpty(VectorPathGeometry path)
    {
        if (!path.TryGetBounds(out var min, out var max))
        {
            return WpfReplayRect.Empty;
        }

        return new WpfReplayRect(
            min.X,
            min.Y,
            Math.Max(0.0, max.X - min.X),
            Math.Max(0.0, max.Y - min.Y));
    }

    private static bool IsUsableBounds(WpfReplayRect bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width >= 0
            && bounds.Height >= 0
            && (bounds.Width != 0 || bounds.Height != 0);
    }

    private static VectorPathGeometry Convert(PortableGeometryPath portablePath)
    {
        VectorPathGeometry path;
        if (portablePath.Kind == PortableGeometryPathKind.Combined)
        {
            path = new VectorPathGeometry
            {
                IsCombined = true,
                PathA = portablePath.PathA != null
                    ? Convert(portablePath.PathA)
                    : new VectorPathGeometry(),
                PathB = portablePath.PathB != null
                    ? Convert(portablePath.PathB)
                    : new VectorPathGeometry(),
                Op = portablePath.CombineOperation,
                FillRule = ToNativeFillRule(portablePath.FillRule)
            };
        }
        else
        {
            path = new VectorPathGeometry
            {
                FillRule = ToNativeFillRule(portablePath.FillRule)
            };

            var portableFigures = portablePath.Figures;
            for (var figureIndex = 0; figureIndex < portableFigures.Length; figureIndex++)
            {
                var portableFigure = portableFigures[figureIndex];
                var figure = new VectorPathFigure
                {
                    StartPoint = ToVector2(portableFigure.StartPoint),
                    IsClosed = portableFigure.IsClosed,
                    IsFilled = portableFigure.IsFilled
                };

                var segments = portableFigure.Segments;
                for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                {
                    var segment = segments[segmentIndex];
                    AddPortableSegment(figure, segment);
                }

                path.Figures.Add(figure);
            }
        }

        var localTransform = ToMatrix4x4(portablePath.Transform);
        return localTransform.IsIdentity
            ? path
            : path.CreateTransformed(localTransform);
    }

    private static void AddPortableSegment(VectorPathFigure figure, PortablePathSegment segment)
    {
        switch (segment.Kind)
        {
            case PortablePathSegmentKind.Line:
                figure.Segments.Add(new VectorLineSegment(
                    ToVector2(segment.Point1),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.QuadraticBezier:
                figure.Segments.Add(new VectorQuadraticBezierSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Point2),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.CubicBezier:
                figure.Segments.Add(new VectorCubicBezierSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Point2),
                    ToVector2(segment.Point3),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.Arc:
                figure.Segments.Add(new VectorArcSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Size),
                    (float)segment.RotationAngle,
                    segment.IsLargeArc,
                    ToNativeSweepDirection(segment.SweepDirection),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
        }
    }

    private static VectorFillRule ToNativeFillRule(PortableFillRule fillRule)
    {
        return fillRule == PortableFillRule.Nonzero
            ? VectorFillRule.Nonzero
            : VectorFillRule.EvenOdd;
    }

    private static VectorSweepDirection ToNativeSweepDirection(PortableSweepDirection sweepDirection)
    {
        return sweepDirection == PortableSweepDirection.Clockwise
            ? VectorSweepDirection.Clockwise
            : VectorSweepDirection.Counterclockwise;
    }

    private static Matrix4x4 ToMatrix4x4(PortableMatrix3x2 matrix)
    {
        return new Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0.0f,
            0.0f,
            (float)matrix.M21,
            (float)matrix.M22,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            1.0f,
            0.0f,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0.0f,
            1.0f);
    }

    private static Vector2 ToVector2(PortablePoint point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2 ToVector2(PortableSize size)
    {
        return new Vector2((float)size.Width, (float)size.Height);
    }
}
