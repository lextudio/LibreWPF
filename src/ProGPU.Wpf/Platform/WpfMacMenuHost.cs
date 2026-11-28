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

    /// <summary>The id reported when a custom About is activated; the standard panel is shown by the host.</summary>
    internal const int AboutId = -2;

    private const ulong MaskShift = 1UL << 17;
    private const ulong MaskControl = 1UL << 18;
    private const ulong MaskOption = 1UL << 19;
    private const ulong MaskCommand = 1UL << 20;

    private static nint s_target;
    private static PortableMenuCallbacks? s_callbacks;
    private static PortableApplicationMenu? s_application;
    private static nint s_dockMenu;
    private static nint s_extendedDelegateClass;

    /// <summary>Whether the last install could make the application delegate answer the Dock menu (diagnostics).</summary>
    internal static bool DockMenuInstalled { get; private set; } = true;

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
    internal static nint BuildMenuBar(PortableApplicationMenu application, PortableMenuItem[] items, PortableMenuCallbacks callbacks)
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

        s_callbacks = callbacks;
        s_application = application;
        nint target = EnsureTarget();
        nint bar = NewMenu(string.Empty, autoenable: false);
        nint applicationMenu = NewMenu(application.ApplicationName);

        AddApplicationMenu(applicationMenu, application, target);
        AddSubmenuItem(bar, applicationMenu, application.ApplicationName);

        foreach (PortableMenuItem item in items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            nint submenu = NewMenu(item.Title);
            AddChildren(submenu, item.Children, target);
            nint header = AddSubmenuItem(bar, submenu, item.Title);
            SendVoidBool(header, Selector("setEnabled:"), item.IsEnabled);
            if (item.Role == 2 || item.Role == 3)
            {
                nint shared = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
                SendVoidObject(shared, Selector(item.Role == 2 ? "setWindowsMenu:" : "setHelpMenu:"), submenu);
            }
        }

        return bar;
    }

    /// <summary>Builds and installs the menu bar as the application's main menu.</summary>
    internal static bool Install(PortableApplicationMenu application, PortableMenuItem[] items, PortableMenuCallbacks callbacks)
    {
        // AppKit throws (and the process dies) when the main menu is set off the main thread.
        if (!IsMainThread())
        {
            return false;
        }

        nint bar = BuildMenuBar(application, items, callbacks);
        if (bar == 0)
        {
            return false;
        }

        nint shared = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
        SendVoidObject(shared, Selector("setMainMenu:"), bar);
        // NSApplication retains the bar it was given; drop the reference BuildMenuBar returned.
        SendVoid(bar, Selector("release"));
        DockMenuInstalled = InstallDockMenu(application.DockItems, shared);
        return true;
    }

    /// <summary>
    /// Builds the Dock icon menu and makes the application delegate answer applicationDockMenu: with
    /// it. The delegate belongs to the windowing host, so the method is added to its class (it has
    /// none); with no delegate at all, the menu target becomes the delegate. Returns false when the
    /// delegate already implements applicationDockMenu: and cannot be extended.
    /// </summary>
    internal static bool InstallDockMenu(PortableMenuItem[] items, nint sharedApplication)
    {
        // Every install builds a new menu; the previous one is no longer returned by the delegate.
        nint previous = s_dockMenu;
        s_dockMenu = 0;
        if (previous != 0)
        {
            SendVoid(previous, Selector("release"));
        }

        if (items.Length == 0)
        {
            return true;
        }

        nint target = EnsureTarget();
        nint menu = NewMenu(string.Empty);
        AddChildren(menu, items, target);
        s_dockMenu = menu;

        nint delegateObject = SendObject(sharedApplication, Selector("delegate"));
        if (delegateObject == 0)
        {
            SendVoidObject(sharedApplication, Selector("setDelegate:"), target);
            return true;
        }

        nint delegateClass = SendObject(delegateObject, Selector("class"));
        if (SendBool(delegateObject, Selector("respondsToSelector:"), Selector("applicationDockMenu:")))
        {
            // Ours: the target's own class, or a delegate class a previous install extended.
            return delegateClass == SendObject(target, Selector("class")) || delegateClass == s_extendedDelegateClass;
        }

        if (AddMethod(delegateClass, Selector("applicationDockMenu:"), (nint)(delegate* unmanaged<nint, nint, nint, nint>)&OnDockMenu, "@@:@"))
        {
            s_extendedDelegateClass = delegateClass;
            return true;
        }

        return false;
    }

    /// <summary>Whether the caller is on AppKit's main thread (false off macOS).</summary>
    internal static bool IsMainThread() =>
        OperatingSystem.IsMacOS() &&
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _) &&
        SendBoolNoArg(GetClass("NSThread"), Selector("isMainThread"));

    private static void AddApplicationMenu(nint menu, PortableApplicationMenu application, nint target)
    {
        string appName = application.ApplicationName;
        // About always targets us: the app's command (reported as AboutId) or the host's standard panel.
        AddItem(menu, "About " + appName, "menuAction:", string.Empty, 0, AboutId, target);
        if (application.ApplicationItems.Length > 0)
        {
            AddSeparator(menu);
            AddChildren(menu, application.ApplicationItems, target);
        }

        AddSeparator(menu);

        nint services = NewMenu("Services");
        AddSubmenuItem(menu, services, "Services");
        nint shared = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
        SendVoidObject(shared, Selector("setServicesMenu:"), services);
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
                // An action and tag make AppKit ask validateMenuItem: for the header like any item
                // (opening the submenu does not fire it).
                SendVoidObject(item, Selector("setTarget:"), target);
                SendVoidObject(item, Selector("setAction:"), Selector("menuAction:"));
                SendVoidLong(item, Selector("setTag:"), child.Id);
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

    private static nint NewMenu(string title, bool autoenable = true)
    {
        nint menu = SendObjectObject(SendObject(GetClass("NSMenu"), Selector("alloc")), Selector("initWithTitle:"), NsString(title));
        // Items targeting us are validated when the menu opens (validateMenuItem:); the standard
        // selectors (hide:, services) are validated by AppKit itself. The bar itself is not
        // autoenabled: its items are submenu headers with no action, which AppKit would otherwise
        // re-enable, so their model state is authoritative.
        SendVoidBool(menu, Selector("setAutoenablesItems:"), autoenable);
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
            AddMethod(cls, Selector("validateMenuItem:"), (nint)(delegate* unmanaged<nint, nint, nint, byte>)&OnValidateMenuItem, "c@:@");
            AddMethod(cls, Selector("applicationDockMenu:"), (nint)(delegate* unmanaged<nint, nint, nint, nint>)&OnDockMenu, "@@:@");
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
            int id = (int)SendLongResult(sender, Selector("tag"));
            if (id == AboutId && s_application is { HasCustomAbout: false } application)
            {
                ShowStandardAboutPanel(application);
                return;
            }

            s_callbacks?.Activate(id);
        }
        catch
        {
            // An exception must not unwind into AppKit; the activation callback owns its own reporting.
        }
    }

    [UnmanagedCallersOnly]
    private static byte OnValidateMenuItem(nint self, nint command, nint item)
    {
        try
        {
            int id = (int)SendLongResult(item, Selector("tag"));
            if (id == QuitId || id == AboutId || s_callbacks == null)
            {
                return 1;
            }

            return s_callbacks.Validate(id) ? (byte)1 : (byte)0;
        }
        catch
        {
            return 1;
        }
    }

    [UnmanagedCallersOnly]
    private static nint OnDockMenu(nint self, nint command, nint application) => s_dockMenu;

    /// <summary>The standard About panel with the name, version and copyright an unbundled process has no bundle for.</summary>
    internal static void ShowStandardAboutPanel(PortableApplicationMenu application)
    {
        nint options = SendObject(SendObject(GetClass("NSMutableDictionary"), Selector("alloc")), Selector("init"));
        SetOption(options, "ApplicationName", application.ApplicationName);
        SetOption(options, "ApplicationVersion", application.ApplicationVersion);
        SetOption(options, "Version", string.Empty);
        SetOption(options, "Copyright", application.Copyright);
        nint shared = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
        SendVoidObject(shared, Selector("orderFrontStandardAboutPanelWithOptions:"), options);
        SendVoid(options, Selector("release"));
    }

    private static void SetOption(nint dictionary, string key, string value)
    {
        if (value.Length > 0 || key == "Version")
        {
            SendVoidObjectObject(dictionary, Selector("setObject:forKey:"), NsString(value), NsString(key));
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
    private static partial void SendVoidObjectObject(nint receiver, nint selector, nint first, nint second);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SendBool(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SendBoolNoArg(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidLong(nint receiver, nint selector, long value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidUnsigned(nint receiver, nint selector, ulong value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial long SendLongResult(nint receiver, nint selector);
}
