// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows
{
    /// <summary>
    /// Portable (non-Windows) equivalent of the real HWND-per-Popup behavior on Windows:
    /// hands each <see cref="Controls.Primitives.Popup"/> a genuine, separate native window
    /// (registered by the hosting platform via <see cref="PortableWpfServiceRegistry"/>),
    /// rather than rendering popups as an overlay inside the owning window.
    /// </summary>
    internal static class PortablePopupActivationService
    {
        private static readonly PopupActivationServiceRegistrar s_registrar = new PopupActivationServiceRegistrar();
        private static IDisposable s_registrarRegistration;
        private static Func<double, double, bool, bool, object> _create;
        private static Func<object, object> _getPresentationSource;
        private static Action<object> _show;
        private static Action<object> _hide;
        private static Action<object, bool, double, double, bool, double, double> _setPosition;
        private static Action<object> _dispose;
        private static GetScreenOriginCallback _getScreenOrigin;
        private static GetMonitorBoundsCallback _getMonitorBounds;

        internal static bool IsEnabled
        {
            get { return !OperatingSystem.IsWindows() && Volatile.Read(ref _create) != null; }
        }

        internal static void RegisterPortableInteropService()
        {
            s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterPopupActivationService(s_registrar);
        }

        internal static void Register(
            Func<double, double, bool, bool, object> create,
            Func<object, object> getPresentationSource = null,
            Action<object> show = null,
            Action<object> hide = null,
            Action<object, bool, double, double, bool, double, double> setPosition = null,
            Action<object> dispose = null,
            GetScreenOriginCallback getScreenOrigin = null,
            GetMonitorBoundsCallback getMonitorBounds = null)
        {
            ArgumentNullException.ThrowIfNull(create);

            Volatile.Write(ref _create, create);
            Volatile.Write(ref _getPresentationSource, getPresentationSource);
            Volatile.Write(ref _show, show);
            Volatile.Write(ref _hide, hide);
            Volatile.Write(ref _setPosition, setPosition);
            Volatile.Write(ref _dispose, dispose);
            Volatile.Write(ref _getScreenOrigin, getScreenOrigin);
            Volatile.Write(ref _getMonitorBounds, getMonitorBounds);
        }

        internal static void Clear()
        {
            Volatile.Write(ref _create, null);
            Volatile.Write(ref _getPresentationSource, null);
            Volatile.Write(ref _show, null);
            Volatile.Write(ref _hide, null);
            Volatile.Write(ref _setPosition, null);
            Volatile.Write(ref _dispose, null);
            Volatile.Write(ref _getScreenOrigin, null);
            Volatile.Write(ref _getMonitorBounds, null);
        }

        /// <summary>
        /// Resolves the logical (DIP) screen origin of the window that hosts the given presentation
        /// source, so a separate popup window can be placed relative to it.
        /// </summary>
        internal static bool TryGetScreenOrigin(PresentationSource source, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (source == null)
            {
                return false;
            }

            GetScreenOriginCallback getScreenOrigin = Volatile.Read(ref _getScreenOrigin);
            return getScreenOrigin != null && getScreenOrigin(source, out x, out y);
        }

        internal static bool TryGetMonitorBounds(double screenX, double screenY, out double left, out double top, out double width, out double height)
        {
            left = top = width = height = 0;
            GetMonitorBoundsCallback getMonitorBounds = Volatile.Read(ref _getMonitorBounds);
            return getMonitorBounds != null && getMonitorBounds(screenX, screenY, out left, out top, out width, out height);
        }

        internal static object TryCreate(double x, double y, bool transparent, bool useSharedWindow)
        {
            Func<double, double, bool, bool, object> create = Volatile.Read(ref _create);
            return create?.Invoke(x, y, transparent, useSharedWindow);
        }

        internal static PresentationSource TryGetPresentationSource(object activation)
        {
            if (activation == null)
            {
                return null;
            }

            Func<object, object> getPresentationSource = Volatile.Read(ref _getPresentationSource);
            return getPresentationSource?.Invoke(activation) as PresentationSource;
        }

        internal static void TryShow(object activation)
        {
            if (activation == null)
            {
                return;
            }

            Volatile.Read(ref _show)?.Invoke(activation);
        }

        internal static void TryHide(object activation)
        {
            if (activation == null)
            {
                return;
            }

            Volatile.Read(ref _hide)?.Invoke(activation);
        }

        internal static void TrySetPosition(object activation, bool position, double x, double y, bool size, double width, double height)
        {
            if (activation == null)
            {
                return;
            }

            Volatile.Read(ref _setPosition)?.Invoke(activation, position, x, y, size, width, height);
        }

        internal static void TryDispose(object activation)
        {
            if (activation == null)
            {
                return;
            }

            Volatile.Read(ref _dispose)?.Invoke(activation);
        }

        private sealed class PopupActivationServiceRegistrar : IPortablePopupActivationServiceRegistrar
        {
            public PortableWpfServiceKey ServiceKey
            {
                get { return PortableWpfServiceKey.PresentationFramework; }
            }

            public GetScreenOriginCallback GetScreenOrigin
            {
                get { return Volatile.Read(ref _getScreenOrigin); }
            }

            public void Register(PortablePopupActivationCallbacks callbacks)
            {
                ArgumentNullException.ThrowIfNull(callbacks);

                PortablePopupActivationService.Register(
                    callbacks.Create,
                    callbacks.GetPresentationSource,
                    callbacks.Show,
                    callbacks.Hide,
                    callbacks.SetPosition,
                    callbacks.Dispose,
                    callbacks.GetScreenOrigin,
                    callbacks.GetMonitorBounds);
            }

            public void Clear()
            {
                PortablePopupActivationService.Clear();
            }
        }
    }
}
