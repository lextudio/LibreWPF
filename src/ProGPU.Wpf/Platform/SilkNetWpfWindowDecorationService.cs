namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfWindowDecorationService : IWpfWindowDecorationService
{
    public bool TryBeginDragMove(object window)
    {
        return false;
    }
}
