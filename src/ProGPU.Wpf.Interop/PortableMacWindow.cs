namespace ProGPU.Wpf.Interop;

/// <summary>
/// macOS window integration requested by the source Window (MacWindow attached properties).
/// Integer codes keep this seam free of WPF types: Backdrop 0 None, 1 Transparent,
/// 2 WindowBackground, 3 ContentBackground, 4 UnderWindowBackground, 5 UnderPageBackground;
/// Appearance 0 System, 1 Light, 2 Dark. TitleBarHeight is NaN for the system default.
/// </summary>
public readonly record struct PortableMacWindowRequest(
    bool ExtendsContentIntoTitleBar,
    double TitleBarHeight,
    int Backdrop,
    int Appearance);

/// <summary>
/// What the host actually applied. ActualBackdrop uses the same codes as the request and is 0
/// when the material could not be applied. Insets are logical units: Left is the space the
/// traffic lights occupy, Top is the fused title bar height (both 0 when not fused or full screen).
/// </summary>
public readonly record struct PortableMacWindowResult(
    int ActualBackdrop,
    double CaptionInsetLeft,
    double CaptionInsetTop);
