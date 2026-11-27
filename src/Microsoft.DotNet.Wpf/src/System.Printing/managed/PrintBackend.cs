using PrintingTools.Core;

namespace System.Printing;

/// <summary>The one place System.Printing reaches the platform print system (Win32 spooler or CUPS over IPP).</summary>
internal static class PrintBackend
{
    private static readonly object Sync = new();
    private static IPrintAdapter? _adapter;

    internal static IPrintAdapter Adapter
    {
        get
        {
            lock (Sync)
            {
                return _adapter ??= Create();
            }
        }
    }

    /// <summary>Replaces the backend (tests).</summary>
    internal static void Use(IPrintAdapter? adapter)
    {
        lock (Sync)
        {
            _adapter = adapter;
        }
    }

    internal static IReadOnlyList<PrinterInfo> GetPrinters() =>
        Adapter.GetPrintersAsync().GetAwaiter().GetResult();

    private static IPrintAdapter Create()
    {
        IPrintAdapter? adapter = null;
        if (OperatingSystem.IsWindows())
        {
            adapter = new PrintingTools.Windows.Win32PrintAdapterFactory().CreateAdapter();
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            adapter = new PrintingTools.Cups.CupsPrintAdapterFactory().CreateAdapter();
        }

        return adapter ?? throw new PlatformNotSupportedException("No print system is available on this platform.");
    }
}
