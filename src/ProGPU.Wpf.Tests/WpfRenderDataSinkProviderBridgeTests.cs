using System.Windows.Media;
using System.Windows.Media.ProGPU;
using Xunit;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;

namespace ProGPU.Wpf.Tests;

public sealed class WpfRenderDataSinkProviderBridgeTests
{
    [Fact]
    public void TryRegisterDrawingContextFactoryAdaptsObjectFactoryToProviderDelegate()
    {
        FakeRenderDataDrawingContextSinkProvider.Reset();
        using var expectedContext = new DrawingContext(new ProGPU.Scene.DrawingContext());
        var ownerVisual = new FakeVisual();
        object? capturedOwner = null;

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterDrawingContextFactory(
            typeof(FakeRenderDataDrawingContextSinkProvider),
            owner =>
            {
                capturedOwner = owner;
                return expectedContext;
            },
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(FakeRenderDataDrawingContextSinkProvider.LastFactory);
        Assert.Same(expectedContext, FakeRenderDataDrawingContextSinkProvider.LastFactory(ownerVisual));
        Assert.Same(ownerVisual, capturedOwner);

        registration.Dispose();

        Assert.True(FakeRenderDataDrawingContextSinkProvider.LastScope!.IsDisposed);
    }

    [Fact]
    public void TryRegisterDrawingFrameUsesFrameDrawingContextFactory()
    {
        FakeRenderDataDrawingContextSinkProvider.Reset();
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 100, 50);

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterDrawingContextFactory(
            typeof(FakeRenderDataDrawingContextSinkProvider),
            frame.CreateDrawingContextFactory(),
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);

        using (var context = FakeRenderDataDrawingContextSinkProvider.LastFactory!(new FakeVisual()))
        {
            context.DrawRectangle(Brushes.Red, null, new System.Windows.Rect(1, 2, 3, 4));
        }

        Assert.Single(root.Context.Commands);
    }

    [Fact]
    public void TryRegisterReturnsFalseWhenProviderShapeIsUnavailable()
    {
        var registered = WpfRenderDataSinkProviderBridge.TryRegisterDrawingContextFactory(
            typeof(object),
            _ => new DrawingContext(new ProGPU.Scene.DrawingContext()),
            out var registration);

        Assert.False(registered);
        Assert.Null(registration);
    }

    [Fact]
    public void TryRegisterReturnsFalseWhenProviderDelegateReturnTypeIsIncompatible()
    {
        var registered = WpfRenderDataSinkProviderBridge.TryRegisterDrawingContextFactory(
            typeof(IncompatibleRenderDataDrawingContextSinkProvider),
            _ => new DrawingContext(new ProGPU.Scene.DrawingContext()),
            out var registration);

        Assert.False(registered);
        Assert.Null(registration);
    }

    [Fact]
    public void CompositionTargetDirectReplayUsesFrameScopedProviderRegistration()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs"));

        Assert.Contains("ProGpuWpfDrawingFrame drawingFrame = BeginDrawingFrame(pixelWidth, pixelHeight);", source, StringComparison.Ordinal);
        Assert.Contains("drawingFrame.TryRegisterRenderDataSinkProvider(out IDisposable? registration)", source, StringComparison.Ordinal);
        Assert.Contains("using var drawingContext = drawingFrame.OpenDrawingContext();", source, StringComparison.Ordinal);
    }

    private sealed class FakeVisual
    {
    }

    private static class FakeRenderDataDrawingContextSinkProvider
    {
        public static Func<FakeVisual, DrawingContext>? LastFactory { get; private set; }

        public static FakeScope? LastScope { get; private set; }

        public static IDisposable PushDrawingContextFactory(Func<FakeVisual, DrawingContext> drawingContextFactory)
        {
            LastFactory = drawingContextFactory;
            LastScope = new FakeScope();
            return LastScope;
        }

        public static void Reset()
        {
            LastFactory = null;
            LastScope = null;
        }
    }

    private static class IncompatibleRenderDataDrawingContextSinkProvider
    {
        public static IDisposable PushDrawingContextFactory(Func<FakeVisual, string> drawingContextFactory)
        {
            return new FakeScope();
        }
    }

    private sealed class FakeScope : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
