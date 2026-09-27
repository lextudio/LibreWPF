using ProGPU.Backend;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class NativeBackendWindowOptionsTests
{
    [Fact]
    public void UnspecifiedWindowBackendPreservesStartupDefaults()
        => Assert.Null(new ProGpuWpfWindowOptions().NativeBackendOptions);

    [Fact]
    public void OwnedContextWithoutAnOverrideUsesTheCoreEnvironmentPolicy()
    {
        using var context = ProGpuWpfCompositionTarget.CreateDeviceContext(null, null);
        Assert.Equal(WgpuNativeBackendOptions.FromEnvironment(), context.NativeBackendOptions);
    }

    [Theory]
    [InlineData(WgpuNativeBackend.Automatic)]
    [InlineData(WgpuNativeBackend.Vulkan)]
    [InlineData(WgpuNativeBackend.OpenGL)]
    [InlineData(WgpuNativeBackend.Metal)]
    [InlineData(WgpuNativeBackend.D3D12)]
    public void SourceWindowOptionsRetainTheExplicitChoiceInBothRenderers(WgpuNativeBackend backend)
    {
        var requested = new WgpuNativeBackendOptions(backend);
        foreach (var renderer in new[] { ProGpuWpfRendererMode.ManagedPortable, ProGpuWpfRendererMode.NativeMilWgpu })
        {
            var fallback = new ProGpuWpfWindowOptions { NativeBackendOptions = requested, RendererMode = renderer };
            // An unrecognized source has no reflected properties to replace the
            // real fallback configuration, matching the existing typed contract.
            var actual = WpfPortableWindowActivation.CreateHostOptions(new object(), fallback);
            Assert.NotSame(fallback, actual);
            Assert.Same(requested, actual.NativeBackendOptions);
            Assert.Equal(renderer, actual.RendererMode);
        }
    }

    [Theory]
    [InlineData(WgpuNativeBackend.Automatic)]
    [InlineData(WgpuNativeBackend.Vulkan)]
    [InlineData(WgpuNativeBackend.OpenGL)]
    [InlineData(WgpuNativeBackend.Metal)]
    [InlineData(WgpuNativeBackend.D3D12)]
    public void OwnedContextReceivesTheExactStartupChoiceBeforeInitialization(WgpuNativeBackend backend)
    {
        var requested = new WgpuNativeBackendOptions(backend);
        using var context = ProGpuWpfCompositionTarget.CreateDeviceContext(requested, null);
        Assert.Same(requested, context.NativeBackendOptions);
    }

    [Theory]
    [InlineData(WgpuNativeBackend.Automatic)]
    [InlineData(WgpuNativeBackend.Vulkan)]
    [InlineData(WgpuNativeBackend.OpenGL)]
    [InlineData(WgpuNativeBackend.Metal)]
    [InlineData(WgpuNativeBackend.D3D12)]
    public void UnspecifiedPopupOrSharedWindowInheritsItsOwnersConfiguration(WgpuNativeBackend backend)
    {
        var requested = new WgpuNativeBackendOptions(backend);
        using var owner = ProGpuWpfCompositionTarget.CreateDeviceContext(requested, null);
        using var borrower = ProGpuWpfCompositionTarget.CreateDeviceContext(null, owner);
        Assert.Same(owner.NativeBackendOptions, borrower.NativeBackendOptions);
    }

    [Fact]
    public void ExplicitSharedChoiceIsNotDiscardedBeforeCoreAdapterValidation()
    {
        using var owner = ProGpuWpfCompositionTarget.CreateDeviceContext(new(WgpuNativeBackend.Metal), null);
        var requested = new WgpuNativeBackendOptions(WgpuNativeBackend.Vulkan);
        using var borrower = ProGpuWpfCompositionTarget.CreateDeviceContext(requested, owner);
        Assert.Same(requested, borrower.NativeBackendOptions);
        Assert.Equal(WgpuNativeBackend.Metal, owner.NativeBackendOptions.Preference);
    }
}
