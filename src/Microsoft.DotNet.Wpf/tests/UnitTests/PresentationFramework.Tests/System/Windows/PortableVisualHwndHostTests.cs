// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableVisualHwndHostTests
{
    [PortableHostFact]
    public void OptInKeepsActualVisualLogicalAndLayoutOwnership()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            using var host = new VisualHost();
            var root = Attach(source, host);
            Assert.NotEqual(IntPtr.Zero, source.Handle);
            Assert.Equal(IntPtr.Zero, host.Handle);
            Assert.Same(host, VisualTreeHelper.GetParent(host.Child));
            Assert.Same(host, LogicalTreeHelper.GetParent(host.Child));
            Assert.Same(root, VisualTreeHelper.GetParent(host));
            Assert.Same(PresentationSource.FromVisual(root), PresentationSource.FromVisual(host.Child));
            Assert.Equal(1, VisualTreeHelper.GetChildrenCount(host));
            Assert.Same(host.Child, VisualTreeHelper.GetChild(host, 0));
            host.Measure(new Size(80, 40));
            host.Arrange(new Rect(0, 0, 80, 40));
            Assert.Equal(new Size(80, 40), host.Child.RenderSize);
            Assert.Equal(0, host.BuildCalls);
            Assert.Equal(0, host.DestroyCalls);
        });
    }

    [PortableHostFact]
    public void RepeatedLoadedDoesNotBuildAChildWindowOrDuplicateVisuals()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            using var host = new VisualHost();
            Attach(source, host);
            for (int index = 0; index < 3; index++) Loaded(host);
            Assert.Equal(0, host.BuildCalls);
            Assert.Equal(0, host.DestroyCalls);
            Assert.Equal(1, VisualTreeHelper.GetChildrenCount(host));
        });
    }

    [PortableHostFact]
    public void ReparentingKeepsTheDerivedVisualInTheNewSource()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost first = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost second = PortablePresentationSourceHost.Create();
            using var host = new VisualHost();
            var root = Attach(first, host);
            first.RootVisual = null;
            Loaded(host);
            Assert.Null(PresentationSource.FromVisual(host));
            Assert.Same(host, VisualTreeHelper.GetParent(host.Child));
            second.RootVisual = root;
            Loaded(host);
            Assert.Same(PresentationSource.FromVisual(root), PresentationSource.FromVisual(host.Child));
            Assert.Equal(IntPtr.Zero, host.Handle);
            Assert.Equal(0, host.BuildCalls);
            Assert.Equal(0, host.DestroyCalls);
        });
    }

    [PortableHostFact]
    public void DetachAndReattachPreserveTheSameChild()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            using var host = new VisualHost();
            var root = Attach(source, host);
            root.Children.Remove(host);
            Loaded(host);
            Assert.Null(PresentationSource.FromVisual(host));
            Assert.Same(host, VisualTreeHelper.GetParent(host.Child));
            root.Children.Add(host);
            Loaded(host);
            Assert.Same(PresentationSource.FromVisual(root), PresentationSource.FromVisual(host.Child));
            Assert.Equal(0, host.BuildCalls);
            Assert.Equal(0, host.DestroyCalls);
        });
    }

    [PortableHostFact]
    public void DisposeDoesNotTakeOwnershipOfDerivedChildrenOrCallWindowCleanup()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            var host = new VisualHost();
            Attach(source, host);
            host.Dispose();
            host.Dispose();
            Assert.Same(host, VisualTreeHelper.GetParent(host.Child));
            Assert.Same(host, LogicalTreeHelper.GetParent(host.Child));
            Loaded(host);
            Assert.Equal(0, host.BuildCalls);
            Assert.Equal(0, host.DestroyCalls);
        });
    }

    [PortableHostFact]
    public void DefaultModeStillRejectsAZeroHandle()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            using var host = new InvalidHandleHost(returnParent: false);
            Assert.Throws<InvalidOperationException>(() => Attach(source, host));
            Assert.Equal(1, host.BuildCalls);
            Assert.Equal(0, host.DestroyCalls);
            Assert.Equal(IntPtr.Zero, host.Handle);
        });
    }

    [PortableHostFact]
    public void DefaultModeRejectsParentHandleWithoutStealingTheSourceRoot()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            using var host = new InvalidHandleHost(returnParent: true);
            var root = new Grid();
            root.Children.Add(host);
            Assert.Throws<InvalidOperationException>(() =>
            {
                source.RootVisual = root;
                Loaded(host);
            });
            Assert.Same(root, source.RootVisual);
            Assert.Same(root, VisualTreeHelper.GetParent(host));
            Assert.NotEqual(IntPtr.Zero, source.Handle);
            Assert.Equal(IntPtr.Zero, host.Handle);
            Assert.Equal(0, host.DestroyCalls);
        });
    }

    [PortableHostFact]
    public void DefaultModeStillOwnsAndDestroysARealPortableChildSource()
    {
        Run(() =>
        {
            using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
            var host = new ChildSourceHost();
            Attach(source, host);
            Loaded(host);
            Assert.Equal(1, host.BuildCalls);
            Assert.NotEqual(IntPtr.Zero, host.Handle);
            Assert.NotEqual(source.Handle, host.Handle);
            Assert.Same(host.Child, VisualTreeHelper.GetChild(host, 0));
            host.Dispose();
            host.Dispose();
            Assert.Equal(1, host.DestroyCalls);
            Assert.Null(VisualTreeHelper.GetParent(host.Child));
            Assert.Equal(IntPtr.Zero, host.Handle);
        });
    }

    private static Grid Attach(IPortablePresentationSourceHost source, HwndHost host)
    {
        var root = new Grid();
        root.Children.Add(host);
        source.RootVisual = root;
        Loaded(host);
        return root;
    }

    private static void Loaded(HwndHost host) => host.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, host));

    private sealed class VisualHost : HwndHost
    {
        public Border Child { get; } = new();
        public int BuildCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        public VisualHost()
        {
            AddVisualChild(Child);
            AddLogicalChild(Child);
        }
        protected override bool UsesPortableVisualHosting => true;
        protected internal override System.Collections.IEnumerator LogicalChildren => new[] { Child }.GetEnumerator();
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? Child : throw new ArgumentOutOfRangeException(nameof(index));
        protected override Size MeasureOverride(Size available)
        {
            Child.Measure(available);
            return Child.DesiredSize;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            Child.Arrange(new Rect(finalSize));
            return finalSize;
        }
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            BuildCalls++;
            throw new InvalidOperationException("A visual-only host must not create a native child.");
        }
        protected override void DestroyWindowCore(HandleRef child)
        {
            DestroyCalls++;
            throw new InvalidOperationException("A visual-only host has no native child to destroy.");
        }
    }

    private sealed class InvalidHandleHost(bool returnParent) : HwndHost
    {
        public int BuildCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            BuildCalls++;
            return returnParent ? parent : new HandleRef(this, IntPtr.Zero);
        }
        protected override void DestroyWindowCore(HandleRef child) => DestroyCalls++;
    }

    private sealed class ChildSourceHost : HwndHost
    {
        private HwndSource? _source;
        public Border Child { get; } = new();
        public int BuildCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            BuildCalls++;
            _source = new HwndSource(new HwndSourceParameters("Owned portable child")
            {
                ParentWindow = parent.Handle,
                WindowStyle = 0x40000000, // WS_CHILD remains required by the child-source contract.
                Width = 80,
                Height = 40,
            }) { RootVisual = Child };
            return new HandleRef(this, _source.Handle);
        }
        protected override void DestroyWindowCore(HandleRef child)
        {
            DestroyCalls++;
            _source!.Dispose();
            _source = null;
        }
    }

    private sealed class PortableHostFactAttribute : FactAttribute
    {
        public PortableHostFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before source initialization, including Windows.";
        }
    }

    private static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Portable visual host test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
