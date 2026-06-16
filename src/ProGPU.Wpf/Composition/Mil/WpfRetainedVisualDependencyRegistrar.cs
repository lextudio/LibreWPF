namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfRetainedVisualDependencyRegistrar
{
    public static void Register(IWpfCompositionCommandSink sink, object? dependency)
    {
        if (dependency == null || sink is not IWpfRetainedVisualBranchSink retainedVisualBranchSink)
        {
            return;
        }

        var registered = false;
        foreach (var trackedDependency in WpfVisualInvalidationTracker.EnumerateTrackedDependencies(dependency))
        {
            retainedVisualBranchSink.RegisterVisualDependency(trackedDependency);
            registered = true;
        }

        if (!registered)
        {
            retainedVisualBranchSink.RegisterVisualDependency(dependency);
        }
    }

    public static void Register(IWpfCompositionCommandSink sink, params object?[] dependencies)
    {
        foreach (var dependency in dependencies)
        {
            Register(sink, dependency);
        }
    }

    public static void RegisterDirect(IWpfCompositionCommandSink sink, object? dependency)
    {
        if (dependency != null && sink is IWpfRetainedVisualBranchSink retainedVisualBranchSink)
        {
            retainedVisualBranchSink.RegisterVisualDependency(dependency);
        }
    }
}
