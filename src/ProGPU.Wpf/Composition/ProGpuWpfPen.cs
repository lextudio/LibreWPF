using System.Windows.Media;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed class ProGpuWpfPen
{
    public ProGpuWpfPen(
        Brush brush,
        double thickness,
        double[] dashArray,
        double dashOffset,
        PenLineCap startLineCap,
        PenLineCap endLineCap,
        PenLineCap dashCap,
        PenLineJoin lineJoin,
        double miterLimit)
    {
        Brush = brush;
        Thickness = thickness;
        DashArray = dashArray;
        DashOffset = dashOffset;
        StartLineCap = startLineCap;
        EndLineCap = endLineCap;
        DashCap = dashCap;
        LineJoin = lineJoin;
        MiterLimit = miterLimit;
    }

    public Brush Brush { get; }

    public double Thickness { get; }

    public double[] DashArray { get; }

    public double DashOffset { get; }

    public PenLineCap StartLineCap { get; }

    public PenLineCap EndLineCap { get; }

    public PenLineCap DashCap { get; }

    public PenLineJoin LineJoin { get; }

    public double MiterLimit { get; }
}
