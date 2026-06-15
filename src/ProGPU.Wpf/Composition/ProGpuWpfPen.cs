using System.Windows.Media;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed class ProGpuWpfPen : Pen
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
        : base(brush, thickness)
    {
        DashArray = dashArray;
        DashOffset = dashOffset;
        StartLineCap = startLineCap;
        EndLineCap = endLineCap;
        DashCap = dashCap;
        LineJoin = lineJoin;
        MiterLimit = miterLimit;
    }

    public double[] DashArray { get; }

    public double DashOffset { get; }
}
