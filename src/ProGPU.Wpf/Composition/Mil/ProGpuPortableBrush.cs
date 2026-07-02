using System;
using System.Windows;
using System.Windows.Media.Composition;
using ProGPU.Wpf.Interop;
using MediaBrush = System.Windows.Media.Brush;
using ProGpuBrush = global::ProGPU.Vector.Brush;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal enum ProGpuBrushMappingMode
{
    RelativeToBoundingBox,
    Absolute
}

internal sealed class ProGpuPortableBrush : MediaBrush, IPortableBrushSource
{
    private readonly ProGpuBrush _brush;
    private readonly PortableBrush? _portableBrush;

    public ProGpuPortableBrush(ProGpuBrush brush)
        : this(brush, portableBrush: null)
    {
    }

    internal ProGpuPortableBrush(ProGpuBrush brush, PortableBrush? portableBrush)
    {
        _brush = brush;
        _portableBrush = portableBrush;
    }

    public new ProGpuBrush ToNative()
    {
        return _brush;
    }

    protected override Freezable CreateInstanceCore()
    {
        return new ProGpuPortableBrush(
            _brush,
            _portableBrush);
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

    bool IPortableBrushSource.TryGetPortableBrush(out PortableBrush brush)
    {
        if (_portableBrush == null)
        {
            brush = null!;
            return false;
        }

        brush = _portableBrush;
        return true;
    }
}
