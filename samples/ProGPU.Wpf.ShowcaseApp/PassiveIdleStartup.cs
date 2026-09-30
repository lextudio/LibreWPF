namespace ProGPU.Wpf.ShowcaseApp;

// Loaded admits the first-frame wait; it is not proof of a native presentation.
internal static class PassiveIdleStartup
{
    internal static bool TryStart(bool isLoaded, ref bool started)
    {
        if (!isLoaded || started) return false;
        started = true;
        return true;
    }
}
