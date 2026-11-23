using System;

namespace System.Windows.Media.ProGPU;

/// <summary>
/// One-time, explicit registration of Silk.NET's windowing platforms for an application that cannot let Silk discover them (NativeAOT:
/// no dynamic code, so no loading of "Silk.NET.Windowing.Glfw" by name and no reflective creation of the platform).
///
/// An application WITH dynamic code (every JIT application) never registers: <see cref="EnsureRegistered"/> returns before it touches
/// anything, so Silk's own discovery and its choice of platform stay exactly as they always were. Registration is serialized and the
/// "registered" state is published only after the registration succeeded: a second thread that creates its first window at the same
/// time waits until the platforms are registered instead of racing ahead of them, and a registration that throws can be retried by the
/// next window instead of leaving Silk's opaque discovery error forever.
/// </summary>
internal sealed class SilkPlatformRegistration
{
    private readonly object _gate = new object();
    private bool _registered;

    /// <summary>Whether the platforms have been registered by this instance.</summary>
    internal bool IsRegistered
    {
        get { lock (_gate) { return _registered; } }
    }

    /// <summary>Runs <paramref name="register"/> once (retrying after a failure) when <paramref name="dynamicCodeSupported"/> is false.</summary>
    /// <returns>True when this call performed the registration.</returns>
    internal bool EnsureRegistered(bool dynamicCodeSupported, Action register)
    {
        ArgumentNullException.ThrowIfNull(register);

        if (dynamicCodeSupported)
        {
            return false;
        }

        lock (_gate)
        {
            if (_registered)
            {
                return false;
            }

            register();
            _registered = true;
            return true;
        }
    }
}
