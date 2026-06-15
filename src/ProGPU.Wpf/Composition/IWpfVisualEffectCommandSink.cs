namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfVisualEffectCommandSink
{
    bool PushVisualEffect(global::ProGPU.Scene.EffectBase effect);
}
