// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
using ProGPU.Wpf.Interop;

namespace System.Windows
{
    internal readonly struct PortableMessageBoxRequest
    {
        internal PortableMessageBoxRequest(
            object owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
        {
            Owner = owner;
            MessageBoxText = messageBoxText;
            Caption = caption;
            Button = button;
            Icon = icon;
            DefaultResult = defaultResult;
            Options = options;
        }

        internal object Owner { get; }

        internal string MessageBoxText { get; }

        internal string Caption { get; }

        internal MessageBoxButton Button { get; }

        internal MessageBoxImage Icon { get; }

        internal MessageBoxResult DefaultResult { get; }

        internal MessageBoxOptions Options { get; }

        internal MessageBoxResult FallbackResult
        {
            get
            {
                return MessageBox.GetPortableFallbackResult(DefaultResult, Button);
            }
        }
    }

    internal static class PortableMessageBoxService
    {
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly MessageBoxServiceRegistrar s_registrar = new MessageBoxServiceRegistrar();
        private static IDisposable s_registrarRegistration;
        private static Func<PortableMessageBoxRequest, MessageBoxResult> s_show;

        internal static bool IsEnabled
        {
            get
            {
                return !s_isWindows && Volatile.Read(ref s_show) != null;
            }
        }

        internal static void RegisterPortableInteropService()
        {
            s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterMessageBoxService(s_registrar);
        }

        internal static IDisposable Register(Func<object, object> show)
        {
            ArgumentNullException.ThrowIfNull(show);

            return Register(request => ConvertResult(request, show(request)));
        }

        internal static IDisposable Register(Func<PortableMessageBoxRequest, MessageBoxResult> show)
        {
            ArgumentNullException.ThrowIfNull(show);

            if (s_isWindows)
            {
                return EmptyRegistration.Instance;
            }

            Volatile.Write(ref s_show, show);
            return new Registration(show);
        }

        internal static void Clear()
        {
            Volatile.Write(ref s_show, null);
        }

        internal static bool TryShow(
            object owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options,
            out MessageBoxResult result)
        {
            result = MessageBoxResult.None;

            if (s_isWindows)
            {
                return false;
            }

            Func<PortableMessageBoxRequest, MessageBoxResult> show = Volatile.Read(ref s_show);
            if (show == null)
            {
                return false;
            }

            var request = new PortableMessageBoxRequest(
                owner,
                messageBoxText,
                caption,
                button,
                icon,
                defaultResult,
                options);
            result = show(request);
            return true;
        }

        private static MessageBoxResult ConvertResult(PortableMessageBoxRequest request, object result)
        {
            if (result == null)
            {
                return request.FallbackResult;
            }

            if (result is MessageBoxResult messageBoxResult)
            {
                return messageBoxResult;
            }

            if (result is string resultName &&
                Enum.TryParse(resultName, ignoreCase: false, out MessageBoxResult parsedResult))
            {
                return parsedResult;
            }

            throw new InvalidOperationException($"Portable message box handler returned an invalid result '{result}'.");
        }

        private sealed class Registration : IDisposable
        {
            private Func<PortableMessageBoxRequest, MessageBoxResult> _show;

            public Registration(Func<PortableMessageBoxRequest, MessageBoxResult> show)
            {
                _show = show;
            }

            public void Dispose()
            {
                Func<PortableMessageBoxRequest, MessageBoxResult> show = _show;
                if (show == null)
                {
                    return;
                }

                _show = null;
                if (ReferenceEquals(Volatile.Read(ref s_show), show))
                {
                    Volatile.Write(ref s_show, null);
                }
            }
        }

        private sealed class EmptyRegistration : IDisposable
        {
            internal static readonly EmptyRegistration Instance = new EmptyRegistration();

            public void Dispose()
            {
            }
        }

        private sealed class MessageBoxServiceRegistrar : IPortableMessageBoxServiceRegistrar
        {
            public Assembly SourceAssembly
            {
                get
                {
                    return typeof(PortableMessageBoxService).Assembly;
                }
            }

            public IDisposable Register(Func<object, object> show)
            {
                return PortableMessageBoxService.Register(show);
            }

            public void Clear()
            {
                PortableMessageBoxService.Clear();
            }
        }
    }
}
