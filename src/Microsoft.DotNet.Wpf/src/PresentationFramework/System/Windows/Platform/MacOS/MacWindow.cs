// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Platform.MacOS
{
    /// <summary>The system material drawn behind a window (macOS NSVisualEffectView materials).</summary>
    public enum MacBackdrop
    {
        /// <summary>An ordinary opaque window.</summary>
        None = 0,

        /// <summary>A transparent window with no material; the app paints everything.</summary>
        Transparent = 1,

        /// <summary>The window background material.</summary>
        WindowBackground = 2,

        /// <summary>The content background material.</summary>
        ContentBackground = 3,

        /// <summary>The material under the window (the classic Mica-like look).</summary>
        UnderWindowBackground = 4,

        /// <summary>The material under the page.</summary>
        UnderPageBackground = 5,
    }

    /// <summary>The appearance of a window, independent of the app's own theme.</summary>
    public enum MacAppearance
    {
        /// <summary>Follow the user's macOS appearance.</summary>
        System = 0,

        /// <summary>Always Light.</summary>
        Light = 1,

        /// <summary>Always Dark.</summary>
        Dark = 2,
    }

    /// <summary>
    /// macOS window integration: title bar fusion, backdrop material and appearance.
    /// </summary>
    /// <remarks>
    /// Every property is a no-op on other platforms and never changes the behaviour of a window
    /// that does not set it. See docs/macos-window-integration.md for the design and the
    /// per-platform behaviour table.
    /// </remarks>
    public static class MacWindow
    {
        /// <summary>
        /// Extends the window content under a transparent title bar. The close, minimise and zoom
        /// buttons stay and float over the content; the system title text is hidden. Use
        /// <see cref="CaptionInsetsProperty"/> to keep content clear of the buttons.
        /// </summary>
        public static readonly DependencyProperty ExtendsContentIntoTitleBarProperty =
            DependencyProperty.RegisterAttached(
                "ExtendsContentIntoTitleBar",
                typeof(bool),
                typeof(MacWindow),
                new FrameworkPropertyMetadata(false, OnWindowRequestChanged));

        /// <summary>
        /// The height of the fused title bar in logical units; NaN (default) uses the system height.
        /// </summary>
        public static readonly DependencyProperty TitleBarHeightProperty =
            DependencyProperty.RegisterAttached(
                "TitleBarHeight",
                typeof(double),
                typeof(MacWindow),
                new FrameworkPropertyMetadata(double.NaN, OnWindowRequestChanged));

        /// <summary>
        /// The material requested behind the window. A value other than <see cref="MacBackdrop.None"/>
        /// needs a transparent window, which is decided when the native window is created.
        /// </summary>
        public static readonly DependencyProperty BackdropProperty =
            DependencyProperty.RegisterAttached(
                "Backdrop",
                typeof(MacBackdrop),
                typeof(MacWindow),
                new FrameworkPropertyMetadata(MacBackdrop.None, OnWindowRequestChanged));

        /// <summary>The appearance of the window.</summary>
        public static readonly DependencyProperty AppearanceProperty =
            DependencyProperty.RegisterAttached(
                "Appearance",
                typeof(MacAppearance),
                typeof(MacWindow),
                new FrameworkPropertyMetadata(MacAppearance.System, OnWindowRequestChanged));

        private static readonly DependencyPropertyKey s_actualBackdropPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "ActualBackdrop",
                typeof(MacBackdrop),
                typeof(MacWindow),
                new FrameworkPropertyMetadata(MacBackdrop.None));

        /// <summary>
        /// The material the host actually applied; <see cref="MacBackdrop.None"/> when the request
        /// could not be honoured (other platform, or the window was not created transparent).
        /// </summary>
        public static readonly DependencyProperty ActualBackdropProperty = s_actualBackdropPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey s_captionInsetsPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "CaptionInsets",
                typeof(Thickness),
                typeof(MacWindow),
                new FrameworkPropertyMetadata(default(Thickness)));

        /// <summary>
        /// The space taken by the system window controls: <c>Left</c> is the width of the traffic
        /// lights, <c>Top</c> the fused title bar height. Zero when the window is not fused or is
        /// full screen.
        /// </summary>
        public static readonly DependencyProperty CaptionInsetsProperty = s_captionInsetsPropertyKey.DependencyProperty;

        public static bool GetExtendsContentIntoTitleBar(Window window) =>
            (bool)ValidateWindow(window).GetValue(ExtendsContentIntoTitleBarProperty);

        public static void SetExtendsContentIntoTitleBar(Window window, bool value) =>
            ValidateWindow(window).SetValue(ExtendsContentIntoTitleBarProperty, value);

        public static double GetTitleBarHeight(Window window) =>
            (double)ValidateWindow(window).GetValue(TitleBarHeightProperty);

        public static void SetTitleBarHeight(Window window, double value) =>
            ValidateWindow(window).SetValue(TitleBarHeightProperty, value);

        public static MacBackdrop GetBackdrop(Window window) =>
            (MacBackdrop)ValidateWindow(window).GetValue(BackdropProperty);

        public static void SetBackdrop(Window window, MacBackdrop value) =>
            ValidateWindow(window).SetValue(BackdropProperty, value);

        public static MacAppearance GetAppearance(Window window) =>
            (MacAppearance)ValidateWindow(window).GetValue(AppearanceProperty);

        public static void SetAppearance(Window window, MacAppearance value) =>
            ValidateWindow(window).SetValue(AppearanceProperty, value);

        public static MacBackdrop GetActualBackdrop(Window window) =>
            (MacBackdrop)ValidateWindow(window).GetValue(ActualBackdropProperty);

        public static Thickness GetCaptionInsets(Window window) =>
            (Thickness)ValidateWindow(window).GetValue(CaptionInsetsProperty);

        internal static void SetActual(Window window, MacBackdrop actualBackdrop, Thickness captionInsets)
        {
            window.SetValue(s_actualBackdropPropertyKey, actualBackdrop);
            window.SetValue(s_captionInsetsPropertyKey, captionInsets);
        }

        private static Window ValidateWindow(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            return window;
        }

        private static void OnWindowRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window)
            {
                window.UpdatePortableMacWindow();
            }
        }
    }
}
