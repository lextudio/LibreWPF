namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfVisualCacheCommandSink
{
    bool PushVisualCache(System.Windows.Rect? bounds = null);
}
