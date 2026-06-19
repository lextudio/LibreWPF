// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Threading;

namespace System.Windows
{
    internal readonly struct PortableLaunchRequest
    {
        internal PortableLaunchRequest(Uri uri, string targetFrame, bool isTopLevel)
        {
            Uri = uri;
            TargetFrame = targetFrame;
            IsTopLevel = isTopLevel;
        }

        internal Uri Uri { get; }

        internal string TargetFrame { get; }

        internal bool IsTopLevel { get; }
    }

    internal static class PortableLauncherService
    {
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static Func<PortableLaunchRequest, bool> s_launch;

        internal static bool IsEnabled
        {
            get
            {
                return !s_isWindows && Volatile.Read(ref s_launch) != null;
            }
        }

        internal static IDisposable Register(Func<object, bool> launch)
        {
            ArgumentNullException.ThrowIfNull(launch);

            if (s_isWindows)
            {
                return EmptyRegistration.Instance;
            }

            Func<PortableLaunchRequest, bool> typedLaunch = request => launch(request);
            Volatile.Write(ref s_launch, typedLaunch);
            return new Registration(typedLaunch);
        }

        internal static void Clear()
        {
            Volatile.Write(ref s_launch, null);
        }

        internal static bool TryLaunch(Uri uri, string targetFrame, bool isTopLevel, out bool launched)
        {
            launched = false;

            if (s_isWindows)
            {
                return false;
            }

            Func<PortableLaunchRequest, bool> launch = Volatile.Read(ref s_launch);
            if (launch == null)
            {
                return true;
            }

            launched = launch(new PortableLaunchRequest(uri, targetFrame, isTopLevel));
            return true;
        }

        private sealed class Registration : IDisposable
        {
            private Func<PortableLaunchRequest, bool> _launch;

            internal Registration(Func<PortableLaunchRequest, bool> launch)
            {
                _launch = launch;
            }

            public void Dispose()
            {
                Func<PortableLaunchRequest, bool> launch = _launch;
                if (launch == null)
                {
                    return;
                }

                _launch = null;

                if (ReferenceEquals(Volatile.Read(ref s_launch), launch))
                {
                    Volatile.Write(ref s_launch, null);
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
