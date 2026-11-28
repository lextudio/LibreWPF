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
