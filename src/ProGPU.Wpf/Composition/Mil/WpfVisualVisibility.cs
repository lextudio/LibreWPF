using System;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfVisualVisibility
{
    internal static bool IsVisible(PortableVisualState state)
    {
        if (!state.HasVisibility)
            return true;

        return state.Visibility switch
        {
            PortableVisualVisibility.Visible => true,
            PortableVisualVisibility.Hidden or PortableVisualVisibility.Collapsed => false,
            _ => throw new NotSupportedException($"Unknown local visual visibility {(int)state.Visibility}.")
        };
    }
}
