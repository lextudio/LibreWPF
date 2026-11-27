using System.Printing;
using PrintingTools.Core;

namespace LibreWPF.SystemPrinting.Tests;

/// <summary>Reaches System.Printing's internal backend switch through reflection (the assembly is strong-named, no friend access).</summary>
internal static class PrintBackendAccess
{
    public static void Use(IPrintAdapter? adapter) =>
        typeof(LocalPrintServer).Assembly.GetType("System.Printing.PrintBackend", throwOnError: true)!
            .GetMethod("Use", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object?[] { adapter });
}
