using System.Runtime.InteropServices;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfMacMenuHostTests
{
    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(1, 1UL << 20)]
    [InlineData(2, 1UL << 17)]
    [InlineData(4, 1UL << 19)]
    [InlineData(8, 1UL << 18)]
    [InlineData(1 | 2 | 4 | 8, (1UL << 20) | (1UL << 17) | (1UL << 19) | (1UL << 18))]
    public void ModifierFlagsMapToNsEventModifierFlags(int modifiers, ulong expected)
    {
        Assert.Equal(expected, WpfMacMenuHost.MapModifierMask(modifiers));
    }

    [Fact]
    public void BuildsTheGeneratedApplicationMenuAndTheModelAndReportsActivations()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        PortableMenuItem Leaf(int id, string title, string key, int mods, bool enabled = true, bool isChecked = false) =>
            new(id, title, key, mods, false, enabled, isChecked, 0, Array.Empty<PortableMenuItem>());

        var model = new[]
        {
            new PortableMenuItem(1, "View", string.Empty, 0, false, true, false, 0, new[]
            {
                Leaf(2, "Vitals", "1", 1),
                Leaf(3, "Silicon", "2", 1, enabled: false),
                new PortableMenuItem(4, string.Empty, string.Empty, 0, true, false, false, 0, Array.Empty<PortableMenuItem>()),
                Leaf(5, "Auto", string.Empty, 0, isChecked: true),
            }),
            new PortableMenuItem(6, "Window", string.Empty, 0, false, true, false, 2, new[] { Leaf(7, "Zoom", string.Empty, 0) }),
        };

        var activated = new List<int>();
        nint bar = WpfMacMenuHost.BuildMenuBar("Pulse", model, activated.Add);
        Assert.NotEqual(0, bar);

        string[] top = Titles(bar);
        Assert.Equal(new[] { "Pulse", "View", "Window" }, top);

        nint app = Submenu(bar, 0);
        string[] appTitles = Titles(app);
        Assert.Equal("About Pulse", appTitles[0]);
        Assert.Contains("Hide Pulse", appTitles);
        Assert.Contains("Hide Others", appTitles);
        Assert.Contains("Show All", appTitles);
        Assert.Contains("Quit Pulse", appTitles);
        Assert.Equal("q", KeyEquivalent(app, Array.IndexOf(appTitles, "Quit Pulse")));
        Assert.Equal("h", KeyEquivalent(app, Array.IndexOf(appTitles, "Hide Pulse")));

        nint view = Submenu(bar, 1);
        Assert.Equal(new[] { "Vitals", "Silicon", string.Empty, "Auto" }, Titles(view));
        Assert.Equal("1", KeyEquivalent(view, 0));
        Assert.Equal(1UL << 20, ModifierMask(view, 0));
        Assert.True(IsEnabled(view, 0));
        Assert.False(IsEnabled(view, 1));
        Assert.Equal(0L, State(view, 0));
        Assert.Equal(1L, State(view, 3));

        PerformAction(view, 0);
        Assert.Equal(new[] { 2 }, activated);

        PerformAction(app, Array.IndexOf(appTitles, "Quit Pulse"));
        Assert.Equal(new[] { 2, WpfMacMenuHost.QuitId }, activated);
    }

    private static nint Item(nint menu, int index) => SendObjectLong(SendObject(menu, Sel("itemArray")), Sel("objectAtIndex:"), index);

    private static nint Submenu(nint menu, int index) => SendObject(Item(menu, index), Sel("submenu"));

    private static string[] Titles(nint menu)
    {
        nint items = SendObject(menu, Sel("itemArray"));
        int count = (int)SendLong(items, Sel("count"));
        var titles = new string[count];
        for (int i = 0; i < count; i++)
        {
            titles[i] = Text(SendObject(SendObjectLong(items, Sel("objectAtIndex:"), i), Sel("title")));
        }

        return titles;
    }

    private static string KeyEquivalent(nint menu, int index) => Text(SendObject(Item(menu, index), Sel("keyEquivalent")));

    private static ulong ModifierMask(nint menu, int index) => (ulong)SendLong(Item(menu, index), Sel("keyEquivalentModifierMask"));

    private static bool IsEnabled(nint menu, int index) => SendLong(Item(menu, index), Sel("isEnabled")) != 0;

    private static long State(nint menu, int index) => SendLong(Item(menu, index), Sel("state"));

    private static void PerformAction(nint menu, int index) => SendVoidLong(menu, Sel("performActionForItemAtIndex:"), index);

    private static string Text(nint nsString) =>
        nsString == 0 ? string.Empty : Marshal.PtrToStringUTF8(SendObject(nsString, Sel("UTF8String"))) ?? string.Empty;

    private static nint Sel(string name) => sel_registerName(name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendObject(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendObjectLong(nint receiver, nint selector, long value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern long SendLong(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidLong(nint receiver, nint selector, long value);
}
