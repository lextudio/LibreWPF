// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace System.Windows
{
    internal static class PortableWindowActivationService
    {
        private static Func<object, object> _activate;
        private static Action<object> _show;
        private static Action<object> _hide;
        private static Action<object, object> _setWindowState;
        private static Action<object, string> _setTitle;
        private static Action<object, double, double> _setClientSize;
        private static Action<object> _close;
        private static Action<object> _run;
        private static Action<object> _dispose;

        internal static bool IsEnabled
        {
            get
            {
                return !OperatingSystem.IsWindows() && Volatile.Read(ref _activate) != null;
            }
        }

        internal static void Register(
            Func<object, object> activate,
            Action<object> show = null,
            Action<object> hide = null,
            Action<object, object> setWindowState = null,
            Action<object, string> setTitle = null,
            Action<object, double, double> setClientSize = null,
            Action<object> close = null,
            Action<object> run = null,
            Action<object> dispose = null)
        {
            ArgumentNullException.ThrowIfNull(activate);

            Volatile.Write(ref _activate, activate);
            Volatile.Write(ref _show, show);
            Volatile.Write(ref _hide, hide);
            Volatile.Write(ref _setWindowState, setWindowState);
            Volatile.Write(ref _setTitle, setTitle);
            Volatile.Write(ref _setClientSize, setClientSize);
            Volatile.Write(ref _close, close);
            Volatile.Write(ref _run, run);
            Volatile.Write(ref _dispose, dispose);
        }

        internal static void Clear()
        {
            Volatile.Write(ref _activate, null);
            Volatile.Write(ref _show, null);
            Volatile.Write(ref _hide, null);
            Volatile.Write(ref _setWindowState, null);
            Volatile.Write(ref _setTitle, null);
            Volatile.Write(ref _setClientSize, null);
            Volatile.Write(ref _close, null);
            Volatile.Write(ref _run, null);
            Volatile.Write(ref _dispose, null);
        }

        internal static bool TryActivate(Window window, out object activation)
        {
            activation = null;

            if (OperatingSystem.IsWindows())
            {
                return false;
            }

            Func<object, object> activate = Volatile.Read(ref _activate);
            if (activate == null)
            {
                return false;
            }

            activation = activate(window);
            return activation != null;
        }

        internal static void Show(object activation)
        {
            Volatile.Read(ref _show)?.Invoke(activation);
        }

        internal static void Hide(object activation)
        {
            Volatile.Read(ref _hide)?.Invoke(activation);
        }

        internal static void SetWindowState(object activation, WindowState windowState)
        {
            Volatile.Read(ref _setWindowState)?.Invoke(activation, windowState);
        }

        internal static void SetTitle(object activation, string title)
        {
            Volatile.Read(ref _setTitle)?.Invoke(activation, title);
        }

        internal static void SetClientSize(object activation, double width, double height)
        {
            Volatile.Read(ref _setClientSize)?.Invoke(activation, width, height);
        }

        internal static void SetActivationState(Window window, bool isActive)
        {
            if (OperatingSystem.IsWindows() || window == null)
            {
                return;
            }

            window.HandleActivate(isActive);
        }

        internal static void Close(object activation)
        {
            Volatile.Read(ref _close)?.Invoke(activation);
        }

        internal static bool TryRun(Window window)
        {
            if (OperatingSystem.IsWindows() || window == null)
            {
                return false;
            }

            object activation = window.PortableWindowActivation;
            if (activation == null)
            {
                return false;
            }

            Action<object> run = Volatile.Read(ref _run);
            if (run == null)
            {
                return false;
            }

            run(activation);
            return true;
        }

        internal static void Dispose(object activation)
        {
            Action<object> dispose = Volatile.Read(ref _dispose);
            if (dispose != null)
            {
                dispose(activation);
            }
            else if (activation is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
