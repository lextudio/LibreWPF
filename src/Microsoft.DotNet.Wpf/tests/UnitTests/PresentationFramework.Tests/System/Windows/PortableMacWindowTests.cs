// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Platform.MacOS;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableMacWindowTests
{
    [Fact]
    public void DefaultsLeaveAnOrdinaryWindowUntouched()
    {
        RunInUiApartment(() =>
        {
            var window = new Window();

            Assert.False(MacWindow.GetExtendsContentIntoTitleBar(window));
            Assert.True(double.IsNaN(MacWindow.GetTitleBarHeight(window)));
            Assert.Equal(MacBackdrop.None, MacWindow.GetBackdrop(window));
            Assert.Equal(MacAppearance.System, MacWindow.GetAppearance(window));
            Assert.Equal(MacBackdrop.None, MacWindow.GetActualBackdrop(window));
            Assert.Equal(default, MacWindow.GetCaptionInsets(window));
        });
    }

    [Fact]
    public void StateCarriesTheRequestedBackdropForFramebufferCreation()
    {
        RunInUiApartment(() =>
        {
            var window = new Window();
            MacWindow.SetBackdrop(window, MacBackdrop.UnderWindowBackground);

            Assert.True(((IPortableWindowStateSource)window).TryGetPortableWindowState(out PortableWindowState state));
            Assert.True(state.HasMacWindow);
            Assert.Equal((int)MacBackdrop.UnderWindowBackground, state.MacBackdrop);
        });
    }

    [Fact]
    public void AReadOnlyResultIsPublishedAfterTheHostApplies()
    {
        RunInUiApartment(() =>
        {
            PortableMacWindowRequest? seen = null;
            PortableWindowActivationService.Register(
                activate: value => value,
                applyMacWindow: (activation, request) =>
                {
                    seen = request;
                    return new PortableMacWindowResult(request.Backdrop, 71d, 28d);
                });
            try
            {
                var window = new Window { Width = 200, Height = 100 };
                MacWindow.SetExtendsContentIntoTitleBar(window, true);
                MacWindow.SetBackdrop(window, MacBackdrop.ContentBackground);
                MacWindow.SetAppearance(window, MacAppearance.Dark);
                MacWindow.SetTitleBarHeight(window, 52d);
                window.Show();

                Assert.NotNull(seen);
                Assert.True(seen!.Value.ExtendsContentIntoTitleBar);
                Assert.Equal((int)MacBackdrop.ContentBackground, seen.Value.Backdrop);
                Assert.Equal((int)MacAppearance.Dark, seen.Value.Appearance);
                Assert.Equal(52d, seen.Value.TitleBarHeight);
                Assert.Equal(MacBackdrop.ContentBackground, MacWindow.GetActualBackdrop(window));
                Assert.Equal(new Thickness(71d, 28d, 0d, 0d), MacWindow.GetCaptionInsets(window));
            }
            finally
            {
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void AHostWithoutAMacWindowReportsNoBackdropAndZeroInsets()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(
                activate: value => value,
                applyMacWindow: (activation, request) => null);
            try
            {
                var window = new Window { Width = 200, Height = 100 };
                MacWindow.SetBackdrop(window, MacBackdrop.UnderWindowBackground);
                MacWindow.SetExtendsContentIntoTitleBar(window, true);
                window.Show();

                Assert.Equal(MacBackdrop.None, MacWindow.GetActualBackdrop(window));
                Assert.Equal(default, MacWindow.GetCaptionInsets(window));
            }
            finally
            {
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ChromeHitTestVisibilityIsInheritedFromAncestorsAndSurvivesContentElements()
    {
        RunInUiApartment(() =>
        {
            var marked = new System.Windows.Controls.Border();
            var inner = new System.Windows.Controls.Border();
            marked.Child = inner;
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(marked, true);
            var unmarked = new System.Windows.Controls.Border { Child = new System.Windows.Controls.Border() };

            Assert.True(Window.IsInHitTestVisibleChrome(marked));
            Assert.True(Window.IsInHitTestVisibleChrome(inner));
            Assert.False(Window.IsInHitTestVisibleChrome(unmarked));
            Assert.False(Window.IsInHitTestVisibleChrome(unmarked.Child));
            Assert.False(Window.IsInHitTestVisibleChrome(null));

            // A Run is a ContentElement: it has no visual parent, so the walk must use the logical tree.
            var run = new System.Windows.Documents.Run("x");
            var block = new System.Windows.Controls.TextBlock();
            block.Inlines.Add(run);
            Assert.False(Window.IsInHitTestVisibleChrome(run));
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(block, true);
            Assert.True(Window.IsInHitTestVisibleChrome(run));
        });
    }

    [PortableInputFact]
    public void PressInTheFusedBarStartsAWindowDragOnlyInsideTheBar()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            int drags = 0;
            PortableWindowActivationService.Register(
                activate: value => value,
                getHandle: value => host.Handle,
                dragMove: activation => { drags++; return true; },
                applyMacWindow: (activation, request) => new PortableMacWindowResult(0, 71d, 52d));
            try
            {
                var window = new Window { Width = 400, Height = 300 };
                MacWindow.SetExtendsContentIntoTitleBar(window, true);
                window.Show();
                host.RootVisual = window;
                host.SetClientSize(400, 300);

                Assert.Equal(52d, MacWindow.GetCaptionInsets(window).Top);
                Assert.True(window.TryBeginPortableChromeDrag(new Point(300, 10)));
                Assert.Equal(1, drags);
                Assert.False(window.TryBeginPortableChromeDrag(new Point(300, 80)));
                Assert.False(window.TryBeginPortableChromeDrag(new Point(-1, 10)));
                Assert.False(window.TryBeginPortableChromeDrag(new Point(401, 10)));
                Assert.Equal(1, drags);

                MacWindow.SetExtendsContentIntoTitleBar(window, false);
                Assert.False(window.TryBeginPortableChromeDrag(new Point(300, 10)));
                Assert.Equal(1, drags);
            }
            finally
            {
                PortableWindowActivationService.Clear();
            }
        });
    }

    private sealed class PortableInputFactAttribute : FactAttribute
    {
        public PortableInputFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input-manager construction, including on Windows.";
        }
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        })
        {
            IsBackground = true
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Portable macOS window test did not complete within 30 seconds.");
        }

        if (exception != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
