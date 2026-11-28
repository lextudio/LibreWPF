namespace ProGPU.Wpf.Interop;

/// <summary>
/// One entry of a native application menu, free of WPF types. Modifiers is a flag set:
/// 1 Command, 2 Shift, 4 Option, 8 Control. Role is 0 None, 1 Edit, 2 Window, 3 Help.
/// KeyEquivalent is empty or one lower-case character. Id is unique within one model and is what
/// the host reports back when the item is activated.
/// </summary>
public sealed record PortableMenuItem(
    int Id,
    string Title,
    string KeyEquivalent,
    int Modifiers,
    bool IsSeparator,
    bool IsEnabled,
    bool IsChecked,
    int Role,
    PortableMenuItem[] Children);

/// <summary>
/// The application-level parts of the menu bar around the mapped items. HasCustomAbout selects who
/// handles About: the app (the host reports AboutId) or the host's standard panel filled from the
/// name, version and copyright. ApplicationItems go into the application menu after About;
/// DockItems form the Dock icon menu.
/// </summary>
public sealed record PortableApplicationMenu(
    string ApplicationName,
    string ApplicationVersion,
    string Copyright,
    bool HasCustomAbout,
    PortableMenuItem[] ApplicationItems,
    PortableMenuItem[] DockItems);

/// <summary>
/// Callbacks from the native menu. Activate reports an item id (QuitId -1 for Quit, AboutId -2 for a
/// custom About) and may run on the native UI thread. Validate is asked, when a menu opens, whether
/// the item with that id is enabled; it is called synchronously on the native UI thread.
/// </summary>
public sealed record PortableMenuCallbacks(Action<int> Activate, Func<int, bool> Validate);
