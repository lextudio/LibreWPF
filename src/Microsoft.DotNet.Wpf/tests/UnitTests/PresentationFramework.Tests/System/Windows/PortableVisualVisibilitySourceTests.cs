// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public sealed class PortableVisualVisibilitySourceTests
{
    [PortableVisibilityTheory]
    [InlineData(Visibility.Hidden, PortableVisualVisibility.Hidden)]
    [InlineData(Visibility.Collapsed, PortableVisualVisibility.Collapsed)]
    public void LocalVisibilitySurvivesOpacityZeroAndRetainedMaskedChild(
        Visibility hidden, PortableVisualVisibility expected)
    {
        RunInUiApartment(() =>
        {
            var child = new Border { Width = 8, Height = 6, Background = Brushes.White, OpacityMask = Brushes.Black };
            var ancestor = new Border { Child = child, Padding = new Thickness(20) };
            Arrange(ancestor);
            var contentSource = (IPortableDrawingContentSource)child;
            Assert.True(contentSource.TryGetPortableDrawingContent(out object? content));
            Assert.NotNull(content);
            Assert.False(ancestor.IsVisible); // Detached, but still a visible brush source.
            Assert.Equal(PortableVisualVisibility.Visible, Read(ancestor).Visibility);

            ancestor.Visibility = hidden;
            Arrange(ancestor);
            PortableVisualState state = Read(ancestor);
            Assert.Equal(expected, state.Visibility);
            Assert.Equal(0, state.Opacity);
            Assert.Equal(PortableVisualVisibility.Visible, Read(child).Visibility);
            Assert.True(contentSource.TryGetPortableDrawingContent(out object? retained));
            Assert.Same(content, retained);
            Assert.Same(Brushes.Black, Read(child).OpacityMask);

            ancestor.Visibility = Visibility.Visible;
            Arrange(ancestor);
            Assert.Equal(PortableVisualVisibility.Visible, Read(ancestor).Visibility);
            ancestor.Opacity = 0;
            state = Read(ancestor);
            Assert.Equal(0, state.Opacity);
            Assert.Equal(PortableVisualVisibility.Visible, state.Visibility);
        });
    }

    [PortableVisibilityTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedVisibleBrushRootsDoNotInheritPresentationConnection(bool cache)
    {
        RunInUiApartment(() =>
        {
            var target = new Border { Background = Brushes.Red, Width = 20, Height = 10 };
            Arrange(target);
            Assert.False(target.IsVisible);
            Assert.Equal(PortableVisualVisibility.Visible, Read(target).Visibility);
            if (cache)
            {
                var brush = new BitmapCacheBrush(target);
                Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var state));
                Assert.Same(target, state.InternalTarget);
            }
            else
            {
                var brush = new VisualBrush(target);
                Assert.Same(target, brush.Visual);
            }
            var drawing = new DrawingVisual { Opacity = 0 };
            Assert.Equal(PortableVisualVisibility.Visible, Read(drawing).Visibility);
            Assert.Equal(0, Read(drawing).Opacity);
        });
    }

    private static PortableVisualState Read(Visual source)
    {
        Assert.True(((IPortableVisualStateSource)source).TryGetPortableVisualState(out PortableVisualState state));
        Assert.True(state.HasVisibility);
        return state;
    }

    private static void Arrange(FrameworkElement source)
    {
        source.Measure(new Size(80, 60));
        source.Arrange(new Rect(0, 0, 80, 60));
        source.UpdateLayout();
    }

    private sealed class PortableVisibilityTheoryAttribute : TheoryAttribute
    {
        public PortableVisibilityTheoryAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before source layout initialization.";
        }
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Source visibility fixture timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
