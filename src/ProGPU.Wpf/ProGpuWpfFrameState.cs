using System;

namespace System.Windows.Media.ProGPU;

public readonly struct ProGpuWpfFrameState : IEquatable<ProGpuWpfFrameState>
{
    public ProGpuWpfFrameState(
        uint pixelWidth,
        uint pixelHeight,
        long sceneChangeVersion,
        long retainedWpfChangeVersion,
        long flatDrawingChangeVersion)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        SceneChangeVersion = sceneChangeVersion;
        RetainedWpfChangeVersion = retainedWpfChangeVersion;
        FlatDrawingChangeVersion = flatDrawingChangeVersion;
    }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }

    public long SceneChangeVersion { get; }

    public long RetainedWpfChangeVersion { get; }

    public long FlatDrawingChangeVersion { get; }

    public bool Equals(ProGpuWpfFrameState other)
    {
        return PixelWidth == other.PixelWidth &&
               PixelHeight == other.PixelHeight &&
               SceneChangeVersion == other.SceneChangeVersion &&
               RetainedWpfChangeVersion == other.RetainedWpfChangeVersion &&
               FlatDrawingChangeVersion == other.FlatDrawingChangeVersion;
    }

    public override bool Equals(object? obj)
    {
        return obj is ProGpuWpfFrameState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            PixelWidth,
            PixelHeight,
            SceneChangeVersion,
            RetainedWpfChangeVersion,
            FlatDrawingChangeVersion);
    }

    public static bool operator ==(ProGpuWpfFrameState left, ProGpuWpfFrameState right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ProGpuWpfFrameState left, ProGpuWpfFrameState right)
    {
        return !left.Equals(right);
    }
}
