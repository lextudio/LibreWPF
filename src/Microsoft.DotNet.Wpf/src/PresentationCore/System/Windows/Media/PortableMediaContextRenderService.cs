// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace System.Windows.Media
{
    internal static class PortableMediaContextRenderService
    {
        private static readonly object s_lock = new object();
        private static readonly List<Action<object, TimeSpan>> s_renderRequests = new List<Action<object, TimeSpan>>();
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal static bool IsEnabled
        {
            get
            {
                if (s_isWindows)
                {
                    return false;
                }

                lock (s_lock)
                {
                    return s_renderRequests.Count != 0;
                }
            }
        }

        internal static IDisposable Register(Action requestRender)
        {
            ArgumentNullException.ThrowIfNull(requestRender);
            return Register((_, _) => requestRender());
        }

        internal static IDisposable Register(Action<TimeSpan> requestRender)
        {
            ArgumentNullException.ThrowIfNull(requestRender);
            return Register((_, delay) => requestRender(delay));
        }

        internal static IDisposable Register(Action<object, TimeSpan> requestRender)
        {
            ArgumentNullException.ThrowIfNull(requestRender);

            if (s_isWindows)
            {
                return EmptyRegistration.Instance;
            }

            lock (s_lock)
            {
                s_renderRequests.Add(requestRender);
            }

            return new Registration(requestRender);
        }

        internal static void RequestRender()
        {
            RequestRender(null, TimeSpan.Zero);
        }

        internal static void RequestRender(TimeSpan delay)
        {
            RequestRender(null, delay);
        }

        internal static void RequestRender(object invalidatedSource)
        {
            RequestRender(invalidatedSource, TimeSpan.Zero);
        }

        internal static void RequestRender(object invalidatedSource, TimeSpan delay)
        {
            if (s_isWindows)
            {
                return;
            }

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            Action<object, TimeSpan>[] renderRequests;
            lock (s_lock)
            {
                if (s_renderRequests.Count == 0)
                {
                    return;
                }

                renderRequests = s_renderRequests.ToArray();
            }

            for (int i = 0; i < renderRequests.Length; i++)
            {
                renderRequests[i](invalidatedSource, delay);
            }
        }

        private sealed class Registration : IDisposable
        {
            private Action<object, TimeSpan> _requestRender;

            public Registration(Action<object, TimeSpan> requestRender)
            {
                _requestRender = requestRender;
            }

            public void Dispose()
            {
                Action<object, TimeSpan> requestRender = _requestRender;
                if (requestRender == null)
                {
                    return;
                }

                _requestRender = null;
                lock (s_lock)
                {
                    s_renderRequests.Remove(requestRender);
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
    }
}
