// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Input;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public partial class PortableWindowActivationServiceTests
{
    [PortableInputFact]
    public void NativePointerReportsRegistrarPreservesUnhandledScrollWithoutLegacyWheel()
    {
        RunInUiApartment(() =>
        {
            var registrar = GetNativePointerRegistrar();
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement();
            host.RootVisual = root;
            host.SetClientSize(200, 100);
            int wheel = 0, routed = 0;
            root.MouseWheel += (_, _) => ++wheel;
            PortablePointerInput? expected = null;
            PortableScroll.AddScrollHandler(root, (_, value) =>
            {
                var scroll = Assert.IsType<PortableScrollEventArgs>(value);
                Assert.Same(expected, scroll.NativeInput);
                Assert.Equal(new Vector(0.25, -0.5), scroll.RemainingScroll);
                ++routed;
            });
            foreach (var unit in new[] { PortablePointerScrollUnit.Points, PortablePointerScrollUnit.Lines })
            {
                expected = new(PortablePointerEventKind.Scroll, PortablePointerScrollProtocol.AppKit,
                    10.25, 12.75, 1.25025, -1, 0, PortablePointerModifiers.Super, 0.25, -0.5, unit);
                Assert.True(registrar.TryProcessPresentationSourceNativePointerInputEvent(host,
                    expected, (int)PortableInputModifiers.Control, out bool handled));
                Assert.False(handled); // Accepted delivery is not successful consumption.
            }
            Assert.Equal(2, routed);
            Assert.Equal(0, wheel);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRegistrarRejectsForeignDetachedAndRetiredTargets()
    {
        RunInUiApartment(() =>
        {
            var registrar = GetNativePointerRegistrar();
            var packet = new PortablePointerInput(PortablePointerEventKind.Move, 1.25, 2.75, 1, -1, 0, 0);
            Assert.False(registrar.TryProcessNativePointerInputEvent(new object(), packet, 0, out bool handled));
            Assert.False(handled);
            Assert.False(registrar.TryProcessPresentationSourceNativePointerInputEvent(new object(), packet, 0, out handled));
            Assert.False(handled);
            var window = new Window();
            Assert.False(registrar.TryProcessNativePointerInputEvent(window, packet, 0, out handled));
            using var host = PortablePresentationSourceHost.Create();
            host.RootVisual = window;
            host.SetClientSize(200, 100);
            Assert.True(registrar.TryProcessNativePointerInputEvent(window, packet, 0, out _));
            host.RootVisual = new HitTestElement();
            Assert.False(registrar.TryProcessNativePointerInputEvent(window, packet, 0, out handled));
            Assert.False(handled);
            host.Dispose();
            Assert.False(registrar.TryProcessPresentationSourceNativePointerInputEvent(host, packet, 0, out handled));
            Assert.False(handled);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRegistrarRetainsOriginalGenerationAcrossRootReplacement()
    {
        RunInUiApartment(() =>
        {
            var registrar = GetNativePointerRegistrar();
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement();
            var replacement = new HitTestElement();
            host.RootVisual = root;
            host.SetClientSize(200, 100);
            int oldDown = 0, newDown = 0;
            root.MouseMove += (_, args) =>
            {
                if (PortableWindowActivationService.GetNativePointerInput(args) != null)
                    host.RootVisual = replacement;
            };
            root.MouseDown += (_, _) => ++oldDown;
            replacement.MouseDown += (_, _) => ++newDown;
            var packet = new PortablePointerInput(PortablePointerEventKind.Down, 10.25, 12.75, 1, 0, 2, 0);
            Assert.True(registrar.TryProcessPresentationSourceNativePointerInputEvent(host, packet, 0, out _));
            Assert.Equal(0, oldDown);
            Assert.Equal(0, newDown);
            Assert.True(registrar.TryProcessPresentationSourceNativePointerInputEvent(host, packet, 0, out _));
            Assert.Equal(1, newDown);
            DeliverNativePointer(host, new(PortablePointerEventKind.Cancel, 10.25, 12.75, 2, -1, 0, 0));
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRegistrarCancellationRetiresCaptureAndPreservesPacket()
    {
        RunInUiApartment(() =>
        {
            var registrar = GetNativePointerRegistrar();
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement();
            host.RootVisual = root;
            host.SetClientSize(200, 100);
            var down = new PortablePointerInput(PortablePointerEventKind.Down, 10.25, 12.75, 1, 0, 2, 0);
            var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10.25, 12.75, 2.125, -1, 0,
                PortablePointerModifiers.Shift);
            Assert.True(registrar.TryProcessPresentationSourceNativePointerInputEvent(host, down, 0, out _));
            int losses = 0, ups = 0;
            root.MouseUp += (_, _) => ++ups;
            root.LostMouseCapture += (_, args) =>
            {
                Assert.Same(cancel, PortableWindowActivationService.GetNativePointerInput(args));
                Assert.Equal(2125, args.Timestamp);
                Assert.Equal(ModifierKeys.Shift, Keyboard.Modifiers);
                Assert.Equal(MouseButtonState.Released, Mouse.LeftButton);
                ++losses;
            };
            try
            {
                Assert.True(Mouse.Capture(root));
                Assert.True(registrar.TryProcessPresentationSourceNativePointerInputEvent(host, cancel,
                    (int)PortableInputModifiers.Shift, out bool handled));
                Assert.True(handled);
                Assert.Null(Mouse.Captured);
                Assert.Equal(1, losses);
                Assert.Equal(0, ups);
            }
            finally { Mouse.Capture(null); }
        });
    }
}
