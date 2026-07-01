using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU;

namespace ProGPU.Wpf.Sdk;

internal static class ProGpuWpfSdkPortableBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        RuntimeHelpers.RunModuleConstructor(typeof(Clipboard).Module.ModuleHandle);
        WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation();
        WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService();
    }
}
