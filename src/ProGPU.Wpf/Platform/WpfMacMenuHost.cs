using System.Runtime.InteropServices;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Platform;

/// <summary>
/// Builds and installs the macOS application menu bar from the neutral PortableMenuItem model
/// (docs/macos-window-integration.md section 4.6). The generated application menu (About, Services,
/// Hide, Hide Others, Show All, Quit) is always present because installing a main menu replaces
/// the one the host created. Item activation is reported by id through one callback; Quit reports
/// QuitId so the WPF layer can close windows through their cancellable path.
/// </summary>
internal static unsafe partial class WpfMacMenuHost
{
    /// <summary>The id reported when the generated Quit item is activated.</summary>
    internal const int QuitId = -1;

    private const ulong MaskShift = 1UL << 17;
    private const ulong MaskControl = 1UL << 18;
    private const ulong MaskOption = 1UL << 19;
    private const ulong MaskCommand = 1UL << 20;

    private static nint s_target;
    private static Action<int>? s_activate;

    /// <summary>NSEventModifierFlags for the neutral modifier flags (1 Command, 2 Shift, 4 Option, 8 Control).</summary>
    internal static ulong MapModifierMask(int modifiers)
    {
        ulong mask = 0;
        if ((modifiers & 1) != 0) mask |= MaskCommand;
        if ((modifiers & 2) != 0) mask |= MaskShift;
        if ((modifiers & 4) != 0) mask |= MaskOption;
        if ((modifiers & 8) != 0) mask |= MaskControl;
        return mask;
    }

    /// <summary>
    /// Builds the menu bar (an NSMenu with a retained reference owned by the caller) without
    /// installing it. Returns 0 off macOS.
    /// </summary>
    internal static nint BuildMenuBar(string appName, PortableMenuItem[] items, Action<int> activate)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return 0;
        }

        // AppKit is already loaded in a windowed process; a headless caller (tests) needs it loaded.
        if (!NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _))
        {
            return 0;
        }

        s_activate = activate;
        nint target = EnsureTarget();
        nint bar = NewMenu(string.Empty);
        nint application = NewMenu(appName);

        AddApplicationMenu(application, appName, target);
        AddSubmenuItem(bar, application, appName);

        foreach (PortableMenuItem item in items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            nint submenu = NewMenu(item.Title);
            AddChildren(submenu, item.Children, target);
            AddSubmenuItem(bar, submenu, item.Title);
            if (item.Role == 2 || item.Role == 3)
            {
                nint shared = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
                SendVoidObject(shared, Selector(item.Role == 2 ? "setWindowsMenu:" : "setHelpMenu:"), submenu);
            }
        }

        return bar;
    }

    /// <summary>Builds and installs the menu bar as the application's main menu.</summary>
    internal static bool Install(string appName, PortableMenuItem[] items, Action<int> activate)
    {
        nint bar = BuildMenuBar(appName, items, activate);
        if (bar == 0)
        {
            return false;
        }

        nint application = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
        SendVoidObject(application, Selector("setMainMenu:"), bar);
        return true;
    }

    private static void AddApplicationMenu(nint menu, string appName, nint target)
    {
        AddItem(menu, "About " + appName, "orderFrontStandardAboutPanel:", string.Empty, 0, 0, null);
        AddSeparator(menu);

        nint services = NewMenu("Services");
        AddSubmenuItem(menu, services, "Services");
        nint application = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
        SendVoidObject(application, Selector("setServicesMenu:"), services);
        AddSeparator(menu);

        AddItem(menu, "Hide " + appName, "hide:", "h", MaskCommand, 0, null);
        AddItem(menu, "Hide Others", "hideOtherApplications:", "h", MaskCommand | MaskOption, 0, null);
        AddItem(menu, "Show All", "unhideAllApplications:", string.Empty, 0, 0, null);
        AddSeparator(menu);

        // Quit reports through the target so the WPF layer closes windows (cancellably) first.
        AddItem(menu, "Quit " + appName, "menuAction:", "q", MaskCommand, QuitId, target);
    }

    private static void AddChildren(nint menu, PortableMenuItem[] children, nint target)
    {
        foreach (PortableMenuItem child in children)
        {
            if (child.IsSeparator)
            {
                AddSeparator(menu);
            }
            else if (child.Children.Length > 0)
            {
                nint submenu = NewMenu(child.Title);
                AddChildren(submenu, child.Children, target);
                nint item = AddSubmenuItem(menu, submenu, child.Title);
                SendVoidBool(item, Selector("setEnabled:"), child.IsEnabled);
            }
            else
            {
                nint item = AddItem(
                    menu,
                    child.Title,
                    "menuAction:",
                    child.KeyEquivalent,
                    MapModifierMask(child.Modifiers),
                    child.Id,
                    target);
                SendVoidBool(item, Selector("setEnabled:"), child.IsEnabled);
                SendVoidLong(item, Selector("setState:"), child.IsChecked ? 1 : 0);
            }
        }
    }

    private static nint AddItem(nint menu, string title, string action, string key, ulong mask, int tag, nint? target)
    {
        nint item = SendObject3(
            SendObject(GetClass("NSMenuItem"), Selector("alloc")),
            Selector("initWithTitle:action:keyEquivalent:"),
            NsString(title),
            Selector(action),
            NsString(key));
        if (key.Length > 0)
        {
            SendVoidUnsigned(item, Selector("setKeyEquivalentModifierMask:"), mask);
        }

        if (target is { } owner)
        {
            SendVoidObject(item, Selector("setTarget:"), owner);
            SendVoidLong(item, Selector("setTag:"), tag);
        }

        SendVoidObject(menu, Selector("addItem:"), item);
        SendVoid(item, Selector("release"));
        return item;
    }

    private static void AddSeparator(nint menu)
    {
        nint separator = SendObject(GetClass("NSMenuItem"), Selector("separatorItem"));
        SendVoidObject(menu, Selector("addItem:"), separator);
    }

    private static nint AddSubmenuItem(nint parent, nint submenu, string title)
    {
        nint item = SendObject3(
            SendObject(GetClass("NSMenuItem"), Selector("alloc")),
            Selector("initWithTitle:action:keyEquivalent:"),
            NsString(title),
            0,
            NsString(string.Empty));
        SendVoidObject(item, Selector("setSubmenu:"), submenu);
        SendVoidObject(parent, Selector("addItem:"), item);
        SendVoid(item, Selector("release"));
        return item;
    }

    private static nint NewMenu(string title)
    {
        nint menu = SendObjectObject(SendObject(GetClass("NSMenu"), Selector("alloc")), Selector("initWithTitle:"), NsString(title));
        SendVoidBool(menu, Selector("setAutoenablesItems:"), false);
        return menu;
    }

    private static nint NsString(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return SendObjectObject(GetClass("NSString"), Selector("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>The NSObject subclass that receives menu actions, created once.</summary>
    private static nint EnsureTarget()
    {
        if (s_target != 0)
        {
            return s_target;
        }

        nint cls = AllocateClassPair(GetClass("NSObject"), "ProGPUWpfMenuTarget", 0);
        if (cls == 0)
        {
            // Registered earlier in this process (another load of the assembly): reuse it.
            cls = GetClass("ProGPUWpfMenuTarget");
        }
        else
        {
            AddMethod(cls, Selector("menuAction:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&OnMenuAction, "v@:@");
            RegisterClassPair(cls);
        }

        s_target = SendObject(SendObject(cls, Selector("alloc")), Selector("init"));
        return s_target;
    }

    [UnmanagedCallersOnly]
    private static void OnMenuAction(nint self, nint command, nint sender)
    {
        try
        {
            s_activate?.Invoke((int)SendLongResult(sender, Selector("tag")));
        }
        catch
        {
            // An exception must not unwind into AppKit; the activation callback owns its own reporting.
        }
    }

    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static nint Selector(string name) => RegisterName(name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterName(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint AllocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_registerClassPair")]
    private static partial void RegisterClassPair(nint cls);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AddMethod(nint cls, nint selector, nint implementation, string types);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObjectObject(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject3(nint receiver, nint selector, nint first, nint second, nint third);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidObject(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidLong(nint receiver, nint selector, long value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidUnsigned(nint receiver, nint selector, ulong value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial long SendLongResult(nint receiver, nint selector);
}
