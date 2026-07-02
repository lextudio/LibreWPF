using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media.Composition;
using System.Windows.Media.ProGPU.Composition;
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

    public new ProGpuBrush ToNative()
    {
        return _brush;
    }

    protected override Freezable CreateInstanceCore()
    {
        return new ProGpuNativeBrush(
            _brush,
            MappingMode,
            _transform,
            _relativeTransform,
            _unsupportedGradientStateCount);
    }

    internal override DUCE.ResourceHandle AddRefOnChannelCore(DUCE.Channel channel)
    {
        return DUCE.ResourceHandle.Null;
    }

    internal override void ReleaseOnChannelCore(DUCE.Channel channel)
    {
    }

    internal override DUCE.ResourceHandle GetHandleCore(DUCE.Channel channel)
    {
        return DUCE.ResourceHandle.Null;
    }

    internal override int GetChannelCountCore()
    {
        return 0;
    }

    internal override DUCE.Channel GetChannelCore(int index)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal ProGpuBrush ToNative(WpfReplayRect bounds)
    {
        return ToNative(bounds.X, bounds.Y, bounds.Width, bounds.Height, IsUsable(bounds));
    }

    private ProGpuBrush ToNative(double x, double y, double width, double height, bool hasUsableBounds)
    {
        if (MappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && !hasUsableBounds)
        {
            return _brush;
        }

        var hasTransform = TryGetEffectiveTransform(x, y, width, height, hasUsableBounds, out var transform);
        var hasCoordinateTransform = TryGetCoordinateTransform(transform, hasTransform, out var coordinateTransform);

        return _brush switch
        {
            ProGpuLinearGradientBrush linear => new ProGpuLinearGradientBrush(
                MapBrushPoint(linear.StartPoint, x, y, width, height, hasUsableBounds),
                MapBrushPoint(linear.EndPoint, x, y, width, height, hasUsableBounds),
                linear.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
            {
                Opacity = linear.Opacity,
                SpreadMethod = linear.SpreadMethod,
                ColorInterpolationMode = linear.ColorInterpolationMode,
                CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
            },
            ProGpuRadialGradientBrush radial => CreateRadialGradientBrush(radial, x, y, width, height, hasUsableBounds, coordinateTransform, hasCoordinateTransform),
            _ => _brush
        };
    }

    internal int CountUnsupportedStateForBounds(WpfReplayRect bounds)
    {
        var count = _unsupportedGradientStateCount;
        if (HasUnsupportedTransformForBounds(bounds))
        {
            count++;
        }

        return count;
    }

    private bool HasUnsupportedTransformForBounds(WpfReplayRect bounds)
    {
        return HasUnsupportedTransformForBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height, IsUsable(bounds));
    }

    private bool HasUnsupportedTransformForBounds(double x, double y, double width, double height, bool hasUsableBounds)
    {
        return (_brush is ProGpuLinearGradientBrush || _brush is ProGpuRadialGradientBrush)
            && TryGetEffectiveTransform(x, y, width, height, hasUsableBounds, out var transform)
            && !TryCreateCoordinateTransform(transform, out _);
    }

    private ProGpuRadialGradientBrush CreateRadialGradientBrush(
        ProGpuRadialGradientBrush radial,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds,
        Matrix4x4 coordinateTransform,
        bool hasCoordinateTransform)
    {
        var center = MapBrushPoint(radial.Center, x, y, width, height, hasUsableBounds);
        var gradientOrigin = MapBrushPoint(radial.GradientOrigin, x, y, width, height, hasUsableBounds);
        var radiusX = MappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusX * width)
            : radial.RadiusX;
        var radiusY = MappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusY * height)
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

    private Vector2 MapBrushPoint(Vector2 point, double x, double y, double width, double height, bool hasUsableBounds)
    {
        if (MappingMode != ProGpuBrushMappingMode.RelativeToBoundingBox || !hasUsableBounds)
        {
            return point;
        }

        return new Vector2(
            (float)(x + point.X * width),
            (float)(y + point.Y * height));
    }

    private bool TryGetEffectiveTransform(double x, double y, double width, double height, bool hasUsableBounds, out Matrix4x4 transform)
    {
        transform = Matrix4x4.Identity;
        var hasTransform = false;

        if (_relativeTransform.HasValue && hasUsableBounds)
        {
            transform *= CreateRelativeBoundsTransform(_relativeTransform.Value, x, y, width, height);
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

    private static Matrix4x4 CreateRelativeBoundsTransform(Matrix4x4 relativeTransform, double x, double y, double width, double height)
    {
        return Matrix4x4.CreateTranslation((float)-x, (float)-y, 0)
            * Matrix4x4.CreateScale((float)(1 / width), (float)(1 / height), 1)
            * relativeTransform
            * Matrix4x4.CreateScale((float)width, (float)height, 1)
            * Matrix4x4.CreateTranslation((float)x, (float)y, 0);
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

    private static bool IsUsable(WpfReplayRect bounds)
    {
        return bounds.Width > 0
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
