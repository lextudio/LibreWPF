using System;
using System.Numerics;
using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using ProGpuBrush = global::ProGPU.Vector.Brush;
using ProGpuLinearGradientBrush = global::ProGPU.Vector.LinearGradientBrush;
using ProGpuRadialGradientBrush = global::ProGPU.Vector.RadialGradientBrush;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal enum ProGpuBrushMappingMode
{
    RelativeToBoundingBox,
    Absolute
}

internal sealed class ProGpuNativeBrush : MediaBrush
{
    private readonly ProGpuBrush _brush;
    private readonly Matrix4x4? _transform;
    private readonly Matrix4x4? _relativeTransform;
    private readonly int _unsupportedGradientStateCount;

    public ProGpuNativeBrush(ProGpuBrush brush)
        : this(brush, ProGpuBrushMappingMode.Absolute)
    {
    }

    public ProGpuNativeBrush(ProGpuBrush brush, ProGpuBrushMappingMode mappingMode)
        : this(brush, mappingMode, transform: null, relativeTransform: null)
    {
    }

    public ProGpuNativeBrush(
        ProGpuBrush brush,
        ProGpuBrushMappingMode mappingMode,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform)
        : this(brush, mappingMode, transform, relativeTransform, unsupportedGradientStateCount: 0)
    {
    }

    internal ProGpuNativeBrush(
        ProGpuBrush brush,
        ProGpuBrushMappingMode mappingMode,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        int unsupportedGradientStateCount)
    {
        _brush = brush;
        _transform = transform;
        _relativeTransform = relativeTransform;
        _unsupportedGradientStateCount = Math.Max(0, unsupportedGradientStateCount);
        MappingMode = mappingMode;
    }

    public ProGpuBrushMappingMode MappingMode { get; }

    public override ProGpuBrush ToNative()
    {
        return _brush;
    }

    public ProGpuBrush ToNative(Rect bounds)
    {
        var hasUsableBounds = IsUsable(bounds);
        if (MappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && !hasUsableBounds)
        {
            return _brush;
        }

        var hasTransform = TryGetEffectiveTransform(bounds, hasUsableBounds, out var transform);
        var hasCoordinateTransform = TryGetCoordinateTransform(transform, hasTransform, out var coordinateTransform);

        return _brush switch
        {
            ProGpuLinearGradientBrush linear => new ProGpuLinearGradientBrush(
                MapBrushPoint(linear.StartPoint, bounds, hasUsableBounds),
                MapBrushPoint(linear.EndPoint, bounds, hasUsableBounds),
                linear.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
            {
                Opacity = linear.Opacity,
                SpreadMethod = linear.SpreadMethod,
                ColorInterpolationMode = linear.ColorInterpolationMode,
                CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
            },
            ProGpuRadialGradientBrush radial => CreateRadialGradientBrush(radial, bounds, hasUsableBounds, coordinateTransform, hasCoordinateTransform),
            _ => _brush
        };
    }

    internal int CountUnsupportedStateForBounds(Rect bounds)
    {
        var count = _unsupportedGradientStateCount;
        if (HasUnsupportedTransformForBounds(bounds))
        {
            count++;
        }

        return count;
    }

    private bool HasUnsupportedTransformForBounds(Rect bounds)
    {
        var hasUsableBounds = IsUsable(bounds);
        return (_brush is ProGpuLinearGradientBrush || _brush is ProGpuRadialGradientBrush)
            && TryGetEffectiveTransform(bounds, hasUsableBounds, out var transform)
            && !TryCreateCoordinateTransform(transform, out _);
    }

    private ProGpuRadialGradientBrush CreateRadialGradientBrush(
        ProGpuRadialGradientBrush radial,
        Rect bounds,
        bool hasUsableBounds,
        Matrix4x4 coordinateTransform,
        bool hasCoordinateTransform)
    {
        var center = MapBrushPoint(radial.Center, bounds, hasUsableBounds);
        var gradientOrigin = MapBrushPoint(radial.GradientOrigin, bounds, hasUsableBounds);
        var radiusX = MappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusX * bounds.Width)
            : radial.RadiusX;
        var radiusY = MappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusY * bounds.Height)
            : radial.RadiusY;

        return new ProGpuRadialGradientBrush(
            center,
            gradientOrigin,
            radiusX,
            radiusY,
            radial.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
        {
            Opacity = radial.Opacity,
            SpreadMethod = radial.SpreadMethod,
            ColorInterpolationMode = radial.ColorInterpolationMode,
            CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
        };
    }

    private Vector2 MapBrushPoint(Vector2 point, Rect bounds, bool hasUsableBounds)
    {
        if (MappingMode != ProGpuBrushMappingMode.RelativeToBoundingBox || !hasUsableBounds)
        {
            return point;
        }

        return new Vector2(
            (float)(bounds.X + point.X * bounds.Width),
            (float)(bounds.Y + point.Y * bounds.Height));
    }

    private bool TryGetEffectiveTransform(Rect bounds, bool hasUsableBounds, out Matrix4x4 transform)
    {
        transform = Matrix4x4.Identity;
        var hasTransform = false;

        if (_relativeTransform.HasValue && hasUsableBounds)
        {
            transform *= CreateRelativeBoundsTransform(_relativeTransform.Value, bounds);
            hasTransform = true;
        }

        if (_transform.HasValue)
        {
            transform *= _transform.Value;
            hasTransform = true;
        }

        return hasTransform;
    }

    private static bool TryGetCoordinateTransform(
        Matrix4x4 transform,
        bool hasTransform,
        out Matrix4x4 coordinateTransform)
    {
        coordinateTransform = Matrix4x4.Identity;
        return !hasTransform || TryCreateCoordinateTransform(transform, out coordinateTransform);
    }

    private static bool TryCreateCoordinateTransform(Matrix4x4 transform, out Matrix4x4 coordinateTransform)
    {
        coordinateTransform = Matrix4x4.Identity;
        return Is2DAffine(transform)
            && Matrix4x4.Invert(transform, out coordinateTransform)
            && Is2DAffine(coordinateTransform);
    }

    private static Matrix4x4 CreateRelativeBoundsTransform(Matrix4x4 relativeTransform, Rect bounds)
    {
        return Matrix4x4.CreateTranslation((float)-bounds.X, (float)-bounds.Y, 0)
            * Matrix4x4.CreateScale((float)(1 / bounds.Width), (float)(1 / bounds.Height), 1)
            * relativeTransform
            * Matrix4x4.CreateScale((float)bounds.Width, (float)bounds.Height, 1)
            * Matrix4x4.CreateTranslation((float)bounds.X, (float)bounds.Y, 0);
    }

    private static bool Is2DAffine(Matrix4x4 transform)
    {
        return NearlyZero(transform.M13)
            && NearlyZero(transform.M14)
            && NearlyZero(transform.M23)
            && NearlyZero(transform.M24)
            && NearlyZero(transform.M31)
            && NearlyZero(transform.M32)
            && NearlyEqual(transform.M33, 1)
            && NearlyZero(transform.M34)
            && NearlyZero(transform.M43)
            && NearlyEqual(transform.M44, 1);
    }

    private static bool NearlyZero(float value)
    {
        return MathF.Abs(value) <= 0.0001f;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) <= 0.0001f;
    }

    private static bool IsUsable(Rect bounds)
    {
        return !bounds.IsEmpty
            && bounds.Width > 0
            && bounds.Height > 0
            && IsFinite(bounds.X)
            && IsFinite(bounds.Y)
            && IsFinite(bounds.Width)
            && IsFinite(bounds.Height);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
