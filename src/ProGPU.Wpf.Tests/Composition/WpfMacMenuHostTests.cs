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
        var enabledAnswers = new Dictionary<int, bool>();
        nint bar = WpfMacMenuHost.BuildMenuBar(
            Application("Pulse"),
            model,
            new PortableMenuCallbacks(activated.Add, id => !enabledAnswers.TryGetValue(id, out bool answer) || answer));
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

        // Opening a menu asks the app whether each of our items is enabled right now.
        enabledAnswers[2] = false;
        enabledAnswers[3] = true;
        Update(view);
        Assert.False(IsEnabled(view, 0));
        Assert.True(IsEnabled(view, 1));
        enabledAnswers[2] = true;
        Update(view);
        Assert.True(IsEnabled(view, 0));

        PerformAction(view, 0);
        Assert.Equal(new[] { 2 }, activated);

        PerformAction(app, Array.IndexOf(appTitles, "Quit Pulse"));
        Assert.Equal(new[] { 2, WpfMacMenuHost.QuitId }, activated);
    }

    private static PortableApplicationMenu Application(
        string name,
        bool customAbout = false,
        PortableMenuItem[]? items = null,
        PortableMenuItem[]? dock = null) =>
        new(name, "1.2.3", "(c) Test", customAbout, items ?? Array.Empty<PortableMenuItem>(), dock ?? Array.Empty<PortableMenuItem>());

    private static PortableMenuItem Plain(int id, string title) =>
        new(id, title, string.Empty, 0, false, true, false, 0, Array.Empty<PortableMenuItem>());

    [Fact]
    public void ApplicationItemsFollowAboutAndAboutAlwaysTargetsTheHost()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var activated = new List<int>();
        nint bar = WpfMacMenuHost.BuildMenuBar(
            Application("Pulse", customAbout: true, items: new[] { Plain(50, "Preferences") }),
            Array.Empty<PortableMenuItem>(),
            new PortableMenuCallbacks(activated.Add, _ => true));
        nint app = Submenu(bar, 0);
        string[] titles = Titles(app);
        Assert.Equal("About Pulse", titles[0]);
        Assert.Equal(string.Empty, titles[1]);
        Assert.Equal("Preferences", titles[2]);
        Assert.True(Array.IndexOf(titles, "Quit Pulse") > 2);

        // A custom About is reported (-2) and an application item by its own id.
        PerformAction(app, 0);
        PerformAction(app, 2);
        Assert.Equal(new[] { WpfMacMenuHost.AboutId, 50 }, activated);
    }

    [Fact]
    public void TheDockMenuIsAnsweredByTheApplicationDelegate()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var activated = new List<int>();
        nint bar = WpfMacMenuHost.BuildMenuBar(
            Application("Pulse", dock: new[] { Plain(60, "New Window"), Plain(61, "Open Recent") }),
            Array.Empty<PortableMenuItem>(),
            new PortableMenuCallbacks(activated.Add, _ => true));
        Assert.NotEqual(0, bar);

        nint shared = SendObject(NativeClass("NSApplication"), Sel("sharedApplication"));
        Assert.True(WpfMacMenuHost.InstallDockMenu(
            new[] { Plain(60, "New Window"), Plain(61, "Open Recent") }, shared));

        nint appDelegate = SendObject(shared, Sel("delegate"));
        Assert.NotEqual(0, appDelegate);
        nint dock = SendObjectObject(appDelegate, Sel("applicationDockMenu:"), shared);
        Assert.NotEqual(0, dock);
        Assert.Equal(new[] { "New Window", "Open Recent" }, Titles(dock));

        PerformAction(dock, 1);
        Assert.Equal(new[] { 61 }, activated);

        // Installing an empty Dock menu answers nil.
        Assert.True(WpfMacMenuHost.InstallDockMenu(Array.Empty<PortableMenuItem>(), shared));
        Assert.Equal(0, SendObjectObject(appDelegate, Sel("applicationDockMenu:"), shared));
    }

    [Fact]
    public void DisabledSubmenuHeadersStayDisabledAndNestedHeadersAreValidated()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        PortableMenuItem Header(int id, string title, bool enabled, params PortableMenuItem[] children) =>
            new(id, title, string.Empty, 0, false, enabled, false, 0, children);

        var answers = new Dictionary<int, bool> { [12] = false };
        var model = new[]
        {
            Header(1, "Off", enabled: false, Plain(2, "Inside")),
            Header(10, "On", enabled: true, Header(12, "Nested", enabled: true, Plain(13, "Leaf")), Plain(14, "Item")),
        };
        nint bar = WpfMacMenuHost.BuildMenuBar(
            Application("Pulse"),
            model,
            new PortableMenuCallbacks(_ => { }, id => !answers.TryGetValue(id, out bool answer) || answer));

        // A WPF-disabled top-level menu is not re-enabled by menu validation.
        Update(bar);
        Assert.False(IsEnabled(bar, 1));
        Assert.True(IsEnabled(bar, 2));

        // A nested header is validated like any item: the app can disable it when the menu opens.
        nint on = Submenu(bar, 2);
        Update(on);
        Assert.False(IsEnabled(on, 0));
        answers[12] = true;
        Update(on);
        Assert.True(IsEnabled(on, 0));
    }

    [Fact]
    public void InstallingOffTheMainThreadFailsClosedInsteadOfCrashingAppKit()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // The test runner's thread is not AppKit's main thread; setMainMenu: would throw there.
        Assert.False(WpfMacMenuHost.IsMainThread());
        var callbacks = new PortableMenuCallbacks(_ => { }, _ => true);
        Assert.False(WpfMacMenuHost.Install(Application("Pulse"), new[] { Plain(1, "One") }, callbacks));
    }

    private static nint NativeClass(string name) => objc_getClass(name);

    private static void Update(nint menu) => SendVoidNoArg(menu, Sel("update"));

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

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendObjectObject(nint receiver, nint selector, nint argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidNoArg(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendObjectLong(nint receiver, nint selector, long value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern long SendLong(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendVoidLong(nint receiver, nint selector, long value);
}
