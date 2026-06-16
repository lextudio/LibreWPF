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
        private static readonly List<Action> s_renderRequests = new List<Action>();
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
            if (s_isWindows)
            {
                return;
            }

            Action[] renderRequests;
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
                renderRequests[i]();
            }
        }

        private sealed class Registration : IDisposable
        {
            private Action _requestRender;

            public Registration(Action requestRender)
            {
                _requestRender = requestRender;
            }

            public void Dispose()
            {
                Action requestRender = _requestRender;
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
