using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Windows;
using ProGPU.Backend;
using ProGPU.Scene;
using Silk.NET.WebGPU;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using ProGpuCompositor = ProGPU.Scene.Compositor;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
using ProGpuRect = ProGPU.Scene.Rect;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal sealed class WpfShaderEffectSamplerTextureCache : IDisposable
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MaxSamplerTextureDimension = 4096;

    private readonly WgpuContext _context;
    private readonly ProGpuCompositor _compositor;
    private readonly WpfViewport3DTextureCache _viewport3DTextureCache;
    private readonly Dictionary<object, TextureEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private bool _isDisposed;

    public WpfShaderEffectSamplerTextureCache(
        WgpuContext context,
        ProGpuCompositor compositor,
        WpfViewport3DTextureCache viewport3DTextureCache)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _viewport3DTextureCache = viewport3DTextureCache ?? throw new ArgumentNullException(nameof(viewport3DTextureCache));
    }

    public bool TryCreateSampler(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfShaderEffectSampler sampler)
    {
        ThrowIfDisposed();
        sampler = null!;

        if (brush == null
            || (!TypeNameEndsWith(brush, "DrawingBrush") && !TypeNameEndsWith(brush, "VisualBrush"))
            || !TryGetBrushSourceBounds(brush, out var sourceBounds)
            || !TryCreateTextureBounds(sourceBounds, out var textureBounds, out var pixelWidth, out var pixelHeight))
        {
            return false;
        }

        var entry = GetOrCreateEntry(brush, pixelWidth, pixelHeight);
        if (!RenderBrushToTexture(brush, textureBounds, entry.Texture, imageSourceAdapter))
        {
            return false;
        }

        sampler = new WpfShaderEffectSampler(registerIndex, entry.Texture, samplingMode);
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
        _isDisposed = true;
    }

    private TextureEntry GetOrCreateEntry(object brush, uint pixelWidth, uint pixelHeight)
    {
        if (!_entries.TryGetValue(brush, out var entry))
        {
            entry = new TextureEntry(_context, pixelWidth, pixelHeight);
            _entries.Add(brush, entry);
            return entry;
        }

        entry.EnsureSize(pixelWidth, pixelHeight);
        return entry;
    }

    private bool RenderBrushToTexture(
        object brush,
        Rect textureBounds,
        GpuTexture texture,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        var visual = new ProGpuDrawingVisual
        {
            Size = new Vector2(texture.Width, texture.Height)
        };

        using var drawingContext = new MediaDrawingContext(visual.Context);
        using var sink = new ProGpuCompositionCommandSink(
            drawingContext,
            _context,
            _viewport3DTextureCache);

        var geometry = new MediaRectangleGeometry(textureBounds);
        var drawing = new ShaderSamplerGeometryDrawing(geometry, brush);
        MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return imageSourceAdapter?.AdaptImageSource(imageSource);
        }

        var replayStatus = WpfReflectionDrawingReplay.Replay(
            drawing,
            sink,
            AdaptImageSource);

        if (replayStatus != WpfDrawingReplayStatus.Applied)
        {
            return false;
        }

        visual.ClipBounds = new ProGpuRect(0, 0, texture.Width, texture.Height);
        _compositor.RenderOffscreen(
            visual,
            texture.Width,
            texture.Height,
            texture,
            padding: 0f,
            dpiScale: 1f,
            includeRootTransform: false,
            includeRootVisualState: false);
        return true;
    }

    private static bool TryGetBrushSourceBounds(object brush, out Rect bounds)
    {
        if (TryGetAbsoluteViewbox(brush, out bounds))
        {
            return true;
        }

        if (TypeNameEndsWith(brush, "DrawingBrush")
            && TryGetPropertyValue(brush, "Drawing", out var drawing)
            && drawing != null
            && TryReadFiniteRectProperty(drawing, "Bounds", out bounds))
        {
            return true;
        }

        if (TypeNameEndsWith(brush, "VisualBrush")
            && TryGetPropertyValue(brush, "Visual", out var visual)
            && visual != null
            && TryGetVisualBounds(visual, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetAbsoluteViewbox(object brush, out Rect viewbox)
    {
        viewbox = default;
        if (!TryGetPropertyValue(brush, "Viewbox", out var viewboxValue)
            || viewboxValue == null
            || !TryReadRect(viewboxValue, out viewbox)
            || !IsUsableBounds(viewbox)
            || !TryGetPropertyValue(brush, "ViewboxUnits", out var units)
            || units?.ToString()?.Contains("Absolute", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetVisualBounds(object visual, out Rect bounds)
    {
        if (TryReadFiniteRectProperty(visual, "ContentBounds", out bounds)
            || TryReadFiniteRectProperty(visual, "Bounds", out bounds))
        {
            return true;
        }

        if (TryReadSizeProperty(visual, "RenderSize", out bounds)
            || TryReadSizeProperty(visual, "DesiredSize", out bounds))
        {
            return true;
        }

        if ((TryReadDoubleProperty(visual, "ActualWidth", out var width)
             || TryReadDoubleProperty(visual, "Width", out width))
            && (TryReadDoubleProperty(visual, "ActualHeight", out var height)
                || TryReadDoubleProperty(visual, "Height", out height))
            && width > 0
            && height > 0
            && double.IsFinite(width)
            && double.IsFinite(height))
        {
            bounds = new Rect(0, 0, width, height);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryCreateTextureBounds(
        Rect sourceBounds,
        out Rect textureBounds,
        out uint pixelWidth,
        out uint pixelHeight)
    {
        textureBounds = default;
        pixelWidth = 0;
        pixelHeight = 0;

        if (!IsUsableBounds(sourceBounds))
        {
            return false;
        }

        pixelWidth = ClampTextureDimension(sourceBounds.Width);
        pixelHeight = ClampTextureDimension(sourceBounds.Height);
        textureBounds = new Rect(0, 0, pixelWidth, pixelHeight);
        return true;
    }

    private static uint ClampTextureDimension(double value)
    {
        return (uint)Math.Clamp((int)Math.Ceiling(value), 1, MaxSamplerTextureDimension);
    }

    private static bool TryReadFiniteRectProperty(object source, string propertyName, out Rect rect)
    {
        rect = default;
        return TryGetPropertyValue(source, propertyName, out var rectValue)
            && rectValue != null
            && TryReadRect(rectValue, out rect)
            && IsUsableBounds(rect);
    }

    private static bool TryReadSizeProperty(object source, string propertyName, out Rect rect)
    {
        rect = default;
        if (TryGetPropertyValue(source, propertyName, out var sizeValue)
            && sizeValue != null
            && TryReadDoubleProperty(sizeValue, "Width", out var width)
            && TryReadDoubleProperty(sizeValue, "Height", out var height)
            && width > 0
            && height > 0
            && double.IsFinite(width)
            && double.IsFinite(height))
        {
            rect = new Rect(0, 0, width, height);
            return true;
        }

        return false;
    }

    private static bool TryReadRect(object rectValue, out Rect rect)
    {
        if (rectValue is Rect mediaRect)
        {
            rect = mediaRect;
            return true;
        }

        if (TryReadDoubleProperty(rectValue, "X", out var x)
            && TryReadDoubleProperty(rectValue, "Y", out var y)
            && TryReadDoubleProperty(rectValue, "Width", out var width)
            && TryReadDoubleProperty(rectValue, "Height", out var height))
        {
            rect = new Rect(x, y, width, height);
            return true;
        }

        rect = default;
        return false;
    }

    private static bool IsUsableBounds(Rect bounds)
    {
        return !bounds.IsEmpty
            && bounds.Width > 0
            && bounds.Height > 0
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height);
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        try
        {
            value = property.GetValue(instance);
            return true;
        }
        catch (TargetInvocationException)
        {
            value = null;
            return false;
        }
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = default;
        if (!TryGetPropertyValue(instance, propertyName, out var rawValue) || rawValue == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(rawValue, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TypeNameEndsWith(object instance, string suffix)
    {
        var type = instance.GetType();
        return type.Name.EndsWith(suffix, StringComparison.Ordinal)
            || (type.FullName?.EndsWith(suffix, StringComparison.Ordinal) ?? false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private sealed class TextureEntry : IDisposable
    {
        private readonly WgpuContext _context;

        public TextureEntry(WgpuContext context, uint width, uint height)
        {
            _context = context;
            Texture = CreateTexture(width, height);
        }

        public GpuTexture Texture { get; private set; }

        public void EnsureSize(uint width, uint height)
        {
            if (Texture.Width == width && Texture.Height == height)
            {
                return;
            }

            Texture.Dispose();
            Texture = CreateTexture(width, height);
        }

        public void Dispose()
        {
            Texture.Dispose();
        }

        private GpuTexture CreateTexture(uint width, uint height)
        {
            return new GpuTexture(
                _context,
                width,
                height,
                TextureFormat.Rgba8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                "WPF ShaderEffect Brush Sampler Texture");
        }
    }

    private sealed class ShaderSamplerGeometryDrawing
    {
        public ShaderSamplerGeometryDrawing(MediaGeometry geometry, object brush)
        {
            Geometry = geometry;
            Brush = brush;
        }

        public MediaGeometry Geometry { get; }

        public object Brush { get; }

        public object? Pen => null;
    }
}

internal sealed class WpfShaderEffectSamplerImageSourceAdapter :
    IWpfImageSourceAdapter,
    IWpfShaderEffectSamplerBrushAdapter
{
    private readonly IWpfImageSourceAdapter? _inner;
    private readonly WpfShaderEffectSamplerTextureCache _samplerTextureCache;

    public WpfShaderEffectSamplerImageSourceAdapter(
        IWpfImageSourceAdapter? inner,
        WpfShaderEffectSamplerTextureCache samplerTextureCache)
    {
        _inner = inner;
        _samplerTextureCache = samplerTextureCache ?? throw new ArgumentNullException(nameof(samplerTextureCache));
    }

    public MediaImageSource? AdaptImageSource(object? imageSource)
    {
        return _inner?.AdaptImageSource(imageSource);
    }

    public bool TryAdaptShaderEffectSamplerBrush(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler)
    {
        if (_inner is IWpfShaderEffectSamplerBrushAdapter innerSamplerAdapter
            && innerSamplerAdapter.TryAdaptShaderEffectSamplerBrush(
                brush,
                registerIndex,
                samplingMode,
                out sampler))
        {
            return true;
        }

        return _samplerTextureCache.TryCreateSampler(
            brush,
            registerIndex,
            samplingMode,
            this,
            out sampler);
    }
}
