using System.Windows;
using MediaGeometry = System.Windows.Media.Geometry;
using NativePathGeometrySource = ProGPU.Scene.INativePathGeometrySource;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfMediaGeometryBoundsReader
{
    public static bool TryGetGeometryBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        if (geometry is PortableGeometryPathSource portableGeometrySource
            && portableGeometrySource.TryGetPortableGeometryPath(out var portableGeometry))
        {
            if (WpfPortableRectangleClipReader.TryGetRectangleClipBounds(portableGeometry, out bounds)
                || WpfPortablePathBoundsReader.TryGetPathBounds(portableGeometry, out bounds))
            {
                return true;
            }

            if (WpfPortablePathGeometryConverter.TryGetNativePathBounds(portableGeometry, out bounds))
            {
                return true;
            }

            if (!WpfPortableGeometryPathData.HasPathData(portableGeometry))
            {
                bounds = FromPortableRect(portableGeometry.Bounds);
                return IsUsableBounds(bounds);
            }

            bounds = default;
            return false;
        }

        if (geometry is NativePathGeometrySource nativePathGeometrySource
            && nativePathGeometrySource.TryGetPathGeometry(out var nativeGeometry, out var nativeTransform))
        {
            if (!nativeTransform.IsIdentity)
            {
                nativeGeometry = nativeGeometry.CreateTransformed(nativeTransform);
            }

            bounds = WpfPortablePathGeometryConverter.GetBoundsOrEmpty(nativeGeometry);
            return IsUsableBounds(bounds);
        }

        bounds = FromMediaRect(geometry.Bounds);
        return IsUsableBounds(bounds);
    }

    private static WpfReplayRect FromMediaRect(Rect bounds)
    {
        return new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static WpfReplayRect FromPortableRect(PortableRect bounds)
    {
        return new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
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
}
