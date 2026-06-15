using System;
using System.Windows;
using MediaGeometry = System.Windows.Media.Geometry;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal sealed class ProGpuCombinedGeometry : MediaGeometry
{
    public ProGpuCombinedGeometry(MediaGeometry geometry1, MediaGeometry geometry2, int pathOperation)
    {
        Geometry1 = geometry1 ?? throw new ArgumentNullException(nameof(geometry1));
        Geometry2 = geometry2 ?? throw new ArgumentNullException(nameof(geometry2));
        PathOperation = pathOperation;
    }

    public MediaGeometry Geometry1 { get; }

    public MediaGeometry Geometry2 { get; }

    public int PathOperation { get; }

    public override Rect Bounds => UnionBounds(Geometry1.Bounds, Geometry2.Bounds);

    public override void Draw(global::ProGPU.Scene.DrawingContext context, global::ProGPU.Vector.Brush? fill, global::ProGPU.Vector.Pen? pen)
    {
        // ProGpuCompositionCommandSink owns conversion for this transition adapter.
    }

    private static Rect UnionBounds(Rect left, Rect right)
    {
        if (left.IsEmpty)
        {
            return right;
        }

        if (right.IsEmpty)
        {
            return left;
        }

        var minX = Math.Min(left.X, right.X);
        var minY = Math.Min(left.Y, right.Y);
        var maxX = Math.Max(left.X + left.Width, right.X + right.Width);
        var maxY = Math.Max(left.Y + left.Height, right.Y + right.Height);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
