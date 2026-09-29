// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableWindowActivationServiceTests
{
    [Fact]
    public void NativePointerReportsPreserveLegacyConstructorIdentity()
    {
        var arguments = new object?[]
        {
            PortableInputEventKind.MouseWheel, "A", 17, 'a', 1.25, 2.75, -0.5, 3.5,
            PortableMouseButton.XButton1, PortableInputModifiers.Control
        };
        var legacy = (PortableInputEventArgs)Activator.CreateInstance(typeof(PortableInputEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, args: arguments, culture: null)!;
        Assert.Equal(PortableInputEventKind.MouseWheel, legacy.Kind);
        Assert.Equal("A", legacy.Key);
        Assert.Equal(17, legacy.ScanCode);
        Assert.Equal('a', legacy.Character);
        Assert.Equal(1.25, legacy.X); Assert.Equal(2.75, legacy.Y);
        Assert.Equal(-0.5, legacy.DeltaX); Assert.Equal(3.5, legacy.DeltaY);
        Assert.Equal(PortableMouseButton.XButton1, legacy.Button);
        Assert.Equal(PortableInputModifiers.Control, legacy.Modifiers);
        Assert.Null(legacy.NativePointer);
        Assert.False(legacy.Handled);

        var packet = new PortablePointerInput(PortablePointerEventKind.Move, 1.25, 2.75, 1, -1, 0, 0);
        var native = new PortableInputEventArgs(PortableInputEventKind.MouseMove,
            x: packet.X, y: packet.Y, nativePointer: packet);
        Assert.Same(packet, native.NativePointer);
        Assert.Equal(packet.X, native.X); Assert.Equal(packet.Y, native.Y);
        Assert.Null(native.Key); Assert.Null(native.Character);
    }

    [PortableInputFact]
    public void MenuEntryWithoutFocusUsesActiveVisibleUnblockedPortableWindow()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost firstHost = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost secondHost = PortablePresentationSourceHost.Create();
            var first = new Window { Width = 200, Height = 100 };
            var second = new Window { Width = 200, Height = 100 };
            PortableWindowActivationService.Register(activate: value => value,
                getHandle: value => ReferenceEquals(value, first) ? firstHost.Handle : secondHost.Handle);
            var firstScope = (IPortableAccessKeyScopeSource)first;
            var secondScope = (IPortableAccessKeyScopeSource)second;
            object? menuSource = null;
            KeyboardNavigation.EnterMenuModeEventHandler handler = (source, _) => { menuSource = source; return true; };
            KeyboardNavigation.Current.EnterMenuMode += handler;
            try
            {
                first.Show(); second.Show();
                firstHost.RootVisual = first; secondHost.RootVisual = second;
                firstHost.SetClientSize(200, 100); secondHost.SetClientSize(200, 100);
                PortableWindowActivationService.SetActivationState(second, true);
                firstScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                secondScope.IsPortableAccessKeyScopeActive.Should().BeTrue();
                Keyboard.ClearFocus();
                foreach (RoutedEvent routedEvent in new[] { Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice,
                        (PresentationSource)secondHost, 0, Key.F10) { RoutedEvent = routedEvent });
                menuSource.Should().BeSameAs(secondHost);
                using (PortableModalInputScope.Enter(first))
                    secondScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                second.Hide();
                secondScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                second.Show();
                PortableWindowActivationService.SetActivationState(second, false);
                secondScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                PortableWindowActivationService.SetActivationState(first, true);
                firstScope.IsPortableAccessKeyScopeActive.Should().BeTrue();
                first.Close();
                firstScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
            }
            finally
            {
                KeyboardNavigation.Current.EnterMenuMode -= handler;
                Keyboard.ClearFocus();
                firstHost.RootVisual = null; secondHost.RootVisual = null;
                if (!first.IsDisposed) first.Close();
                if (!second.IsDisposed) second.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    private const int MouseMoveInputKind = 3;
    private const int MouseDownInputKind = 4;
    private const int MouseUpInputKind = 5;
    private const int LeftMouseButton = 1;

    [PortableInputFact]
    public void DropActivationUsesTypedLiveOwnerWithoutFabricatingActivation()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var window = new Window { Width = 200, Height = 100 };
            int requests = 0;
            bool accept = true;
            PortableWindowActivationService.Register(activate: value => value,
                getHandle: _ => host.Handle,
                requestActivation: value => { value.Should().BeSameAs(window); requests++; return accept; });
            try
            {
                window.Show(); host.RootVisual = window; host.SetClientSize(200, 100);
                PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeTrue();
                window.IsActive.Should().BeFalse();
                accept = false;
                PortableWindowActivationService.TryActivateInputOwner(PresentationSource.FromVisual(window)).Should().BeFalse();
                requests.Should().Be(2);
                window.IsActive.Should().BeFalse();
                accept = true;
                window.IsEnabled = false;
                PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeFalse();
                window.IsEnabled = true;
                using (PortableModalInputScope.Enter(new object()))
                    PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeFalse();
                window.Hide();
                PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeFalse();
                window.Show();
                host.RootVisual = new HitTestElement();
                PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeFalse();
                host.RootVisual = window;
                window.Close();
                PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeFalse();
                host.Dispose();
                PortableWindowActivationService.TryActivateInputOwner((PresentationSource)host).Should().BeFalse();
                requests.Should().Be(2);
            }
            finally
            {
                if (!((PresentationSource)host).IsDisposed) host.RootVisual = null;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void FailedGateReleaseStillClosesAcceptedDialogAndDisposesItsHost()
    {
        RunInUiApartment(() =>
        {
            bool rejectEnable = false;
            int closes = 0, disposals = 0;
            using var gate = PortableModalInputScope.RegisterWindow(new object(), allowed =>
            {
                if (allowed && rejectEnable) throw new InvalidOperationException("Native gate release failed.");
            });
            var window = new Window { Width = 200, Height = 100 };
            PortableWindowActivationService.Register(activate: _ => window,
                getHandle: _ => new IntPtr(1234),
                close: _ => closes++, dispose: _ => disposals++,
                releaseDialog: (_, completed) => completed(),
                runDialog: (_, continuation) =>
                {
                    rejectEnable = true;
                    Action close = window.Close;
                    close.Should().Throw<AggregateException>();
                    window.IsDisposed.Should().BeTrue();
                    continuation().Should().BeFalse();
                    PortableModalInputScope.IsActive.Should().BeFalse();
                    PortableModalInputScope.IsNativeInputPolicySynchronized.Should().BeFalse();
                });
            try
            {
                window.ShowDialog().Should().BeFalse();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                rejectEnable = false;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
                using var restored = PortableModalInputScope.Enter(new object());
            }
        });
    }

    [PortableInputFact]
    public void DialogHideAndCloseKeepInputAndFocusBlockedUntilNativeCompletion()
    {
        RunInUiApartment(() =>
        {
            foreach (bool closeDialog in new[] { false, true })
            {
                using IPortablePresentationSourceHost ownerHost = PortablePresentationSourceHost.Create();
                var owner = new Window { Width = 200, Height = 100, Focusable = true };
                var dialog = new Window { Width = 100, Height = 100 };
                Action? completed = null;
                bool nativeEnded = false, ownerAllowed = true;
                int releaseRequests = 0, activationRequests = 0;
                using var gate = PortableModalInputScope.RegisterWindow(owner, allowed => ownerAllowed = allowed);
                PortableWindowActivationService.Register(activate: value => value,
                    getHandle: value => ReferenceEquals(value, owner) ? ownerHost.Handle : new IntPtr(5679),
                    requestActivation: value =>
                    {
                        value.Should().BeSameAs(owner);
                        nativeEnded.Should().BeTrue();
                        ownerAllowed.Should().BeTrue();
                        PortableModalInputScope.IsNativeInputPolicySynchronized.Should().BeTrue();
                        activationRequests++;
                        return true;
                    },
                    runDialog: (_, continuation) =>
                    {
                        PortableWindowActivationService.SetActivationState(owner, false);
                        if (closeDialog) dialog.Close(); else dialog.Hide();
                        continuation().Should().BeFalse();
                        ownerAllowed.Should().BeFalse();
                        activationRequests.Should().Be(0);
                        Keyboard.FocusedElement.Should().BeNull();
                    },
                    releaseDialog: (value, callback) =>
                    {
                        value.Should().BeSameAs(dialog);
                        releaseRequests++;
                        completed = callback;
                    });
                try
                {
                    owner.Show();
                    ownerHost.RootVisual = owner;
                    ownerHost.SetClientSize(200, 100);
                    Keyboard.Focus(owner).Should().BeSameAs(owner);
                    PortableWindowActivationService.SetActivationState(owner, true);
                    dialog.ShowDialog().Should().BeFalse();
                    releaseRequests.Should().Be(1); // Hide/Close plus finally transfer only once.
                    ownerAllowed.Should().BeFalse();
                    activationRequests.Should().Be(0);
                    dialog.IsDisposed.Should().Be(closeDialog);
                    if (!closeDialog)
                    {
                        Action reopen = () => dialog.ShowDialog();
                        reopen.Should().Throw<InvalidOperationException>().WithMessage("*still completing native release*");
                    }
                    nativeEnded = true;
                    completed.Should().NotBeNull();
                    completed!();
                    completed(); // An accidental duplicate cannot restore focus twice.
                    ownerAllowed.Should().BeTrue();
                    activationRequests.Should().Be(1);
                    Keyboard.FocusedElement.Should().BeSameAs(owner);
                    PortableModalInputScope.IsActive.Should().BeFalse();
                }
                finally
                {
                    nativeEnded = true;
                    completed?.Invoke();
                    Keyboard.ClearFocus();
                    if (!dialog.IsDisposed) dialog.Close();
                    ownerHost.RootVisual = null;
                    owner.Close();
                    PortableWindowActivationService.Clear();
                }
            }
        });
    }

    [PortableInputFact]
    public void DialogRestoresSourceFocusOnlyAfterInputAdmissionAndNativeActivation()
    {
        RunInUiApartment(() =>
        {
            foreach (bool accepted in new[] { true, false })
            {
                using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
                var window = new Window { Width = 200, Height = 100, Focusable = true };
                int requests = 0;
                PortableWindowActivationService.Register(activate: _ => window,
                    getHandle: _ => host.Handle,
                    requestActivation: value =>
                    {
                        value.Should().BeSameAs(window);
                        PortableModalInputScope.AllowsInput(window).Should().BeTrue();
                        PortableModalInputScope.IsNativeInputPolicySynchronized.Should().BeTrue();
                        requests++;
                        return accepted;
                    });
                try
                {
                    window.Show();
                    host.RootVisual = window;
                    host.SetClientSize(200, 100);
                    Keyboard.Focus(window).Should().BeSameAs(window);
                    PortableWindowActivationService.SetActivationState(window, true);
                    using (PortableWindowActivationService.CaptureModalInputRestoreState())
                    {
                        using (PortableModalInputScope.Enter(new object()))
                        {
                            PortableWindowActivationService.PrepareForModalInput();
                            PortableWindowActivationService.SetActivationState(window, false);
                            Keyboard.FocusedElement.Should().BeNull();
                            requests.Should().Be(0);
                        }
                    }
                    requests.Should().Be(1);
                    Keyboard.FocusedElement.Should().BeSameAs(accepted ? window : null);
                    window.IsActive.Should().BeFalse(); // Only actual host events publish IsActive.
                    PortableWindowActivationService.SetActivationState(window, true);
                    using (PortableWindowActivationService.CaptureModalInputRestoreState())
                    {
                        window.Hide();
                    }
                    requests.Should().Be(1); // Hidden windows are not reactivated.
                }
                finally
                {
                    Keyboard.ClearFocus();
                    host.RootVisual = null;
                    window.Close();
                    PortableWindowActivationService.Clear();
                }
            }
        });
    }

    [PortableInputFact]
    public void TypedOwnerChangesPreserveCollectionsWhenHostRejectsAndNeverUseOpaqueHandles()
    {
        RunInUiApartment(() =>
        {
            var owner = new Window();
            var secondOwner = new Window();
            var child = new Window { ShowInTaskbar = false };
            var updates = new List<Window?>();
            bool reject = false;
            PortableWindowActivationService.Register(
                activate: value => value, createHidden: value => value,
                getHandle: _ => new IntPtr(123),
                setOwner: (activation, value) =>
                {
                    activation.Should().BeSameAs(child);
                    if (reject) throw new PlatformNotSupportedException("Rejected owner.");
                    updates.Add((Window?)value);
                });
            try
            {
                new WindowInteropHelper(owner).EnsureHandle();
                new WindowInteropHelper(secondOwner).EnsureHandle();
                child.Owner = owner; // No native window or hidden taskbar-owner creation.
                updates.Should().BeEmpty();
                owner.OwnedWindows.Count.Should().Be(1);
                owner.OwnedWindows[0].Should().BeSameAs(child);
                new WindowInteropHelper(child).EnsureHandle();
                reject = true;
                Action replace = () => child.Owner = secondOwner;
                replace.Should().Throw<PlatformNotSupportedException>();
                child.Owner.Should().BeSameAs(owner);
                owner.OwnedWindows.Count.Should().Be(1);
                owner.OwnedWindows[0].Should().BeSameAs(child);
                secondOwner.OwnedWindows.Count.Should().Be(0);
                reject = false;
                child.Owner = secondOwner;
                updates.Should().ContainSingle().Which.Should().BeSameAs(secondOwner);
                Action rawOwner = () => new WindowInteropHelper(child).Owner = new IntPtr(456);
                rawOwner.Should().Throw<PlatformNotSupportedException>();
                child.Owner.Should().BeSameAs(secondOwner);
                child.Owner = null;
                updates.Should().HaveCount(2);
                updates[1].Should().BeNull();
                secondOwner.OwnedWindows.Count.Should().Be(0);
            }
            finally
            {
                child.Close(); secondOwner.Close(); owner.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void KnownPortableOwnerHandlesUseTypedOwnershipBeforeAndAfterChildCreation()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost firstHost = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost secondHost = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost childHost = PortablePresentationSourceHost.Create();
            var first = new Window();
            var second = new Window();
            var child = new Window { ShowInTaskbar = false };
            var updates = new List<Window?>();
            bool reject = false;
            PortableWindowActivationService.Register(activate: value => value, createHidden: value => value,
                getHandle: value => ReferenceEquals(value, first) ? firstHost.Handle :
                    ReferenceEquals(value, second) ? secondHost.Handle : childHost.Handle,
                setOwner: (activation, owner) =>
                {
                    activation.Should().BeSameAs(child);
                    if (reject) throw new PlatformNotSupportedException("Rejected native owner.");
                    updates.Add((Window?)owner);
                });
            try
            {
                first.Show(); second.Show();
                firstHost.RootVisual = first; secondHost.RootVisual = second;
                var interop = new WindowInteropHelper(child);
                interop.Owner = new WindowInteropHelper(first).EnsureHandle();
                child.Owner.Should().BeSameAs(first);
                interop.Owner.Should().Be(firstHost.Handle);
                first.OwnedWindows.Count.Should().Be(1);
                updates.Should().BeEmpty(); // The host applies this before its first Show.

                interop.EnsureHandle(); childHost.RootVisual = child;
                reject = true;
                Action replace = () => interop.Owner = secondHost.Handle;
                replace.Should().Throw<PlatformNotSupportedException>();
                child.Owner.Should().BeSameAs(first);
                interop.Owner.Should().Be(firstHost.Handle);
                first.OwnedWindows.Count.Should().Be(1);
                second.OwnedWindows.Count.Should().Be(0);
                reject = false;
                interop.Owner = secondHost.Handle;
                updates.Should().ContainSingle().Which.Should().BeSameAs(second);
                first.OwnedWindows.Count.Should().Be(0);
                second.OwnedWindows[0].Should().BeSameAs(child);

                IntPtr foreignHandle = secondHost.Handle;
                RunInUiApartment(() =>
                {
                    var foreignChild = new Window();
                    try
                    {
                        Action foreign = () => new WindowInteropHelper(foreignChild).Owner = foreignHandle;
                        foreign.Should().Throw<PlatformNotSupportedException>();
                        foreignChild.Owner.Should().BeNull();
                    }
                    finally { foreignChild.Close(); }
                });

                Action self = () => interop.Owner = childHost.Handle;
                self.Should().Throw<ArgumentException>();
                Action cycle = () => new WindowInteropHelper(second).Owner = childHost.Handle;
                cycle.Should().Throw<ArgumentException>();
                child.Owner.Should().BeSameAs(second);
                interop.Owner = IntPtr.Zero;
                child.Owner.Should().BeNull();
                interop.Owner.Should().Be(IntPtr.Zero);
                updates.Should().HaveCount(2);
                updates[1].Should().BeNull();
                second.OwnedWindows.Count.Should().Be(0);
            }
            finally
            {
                childHost.RootVisual = null; secondHost.RootVisual = null; firstHost.RootVisual = null;
                child.Close(); second.Close(); first.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void PortableOwnerHandleRejectsDetachedMismatchedAndDisposedSources()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost unrelated = PortablePresentationSourceHost.Create();
            var owner = new Window();
            var child = new Window();
            PortableWindowActivationService.Register(activate: value => value, createHidden: value => value,
                getHandle: _ => host.Handle);
            try
            {
                new WindowInteropHelper(owner).EnsureHandle();
                var interop = new WindowInteropHelper(child);
                Action assign = () => interop.Owner = host.Handle;
                assign.Should().Throw<PlatformNotSupportedException>(); // Hidden detached tree.
                host.RootVisual = new HitTestElement();
                assign.Should().Throw<PlatformNotSupportedException>(); // Not a Window.
                host.RootVisual = null;
                unrelated.RootVisual = owner;
                Action mismatch = () => interop.Owner = unrelated.Handle;
                mismatch.Should().Throw<PlatformNotSupportedException>(); // Wrong activation identity.
                unrelated.RootVisual = null;
                host.RootVisual = owner;
                interop.Owner = host.Handle;
                child.Owner.Should().BeSameAs(owner);
                interop.Owner = IntPtr.Zero;
                IntPtr staleHandle = host.Handle;
                host.RootVisual = null;
                owner.Close();
                Action closed = () => interop.Owner = staleHandle;
                closed.Should().Throw<PlatformNotSupportedException>();
                host.Dispose();
                closed.Should().Throw<PlatformNotSupportedException>();
                child.Owner.Should().BeNull();
            }
            finally
            {
                if (!((PresentationSource)host).IsDisposed) host.RootVisual = null;
                unrelated.RootVisual = null;
                child.Close();
                if (!owner.IsDisposed) owner.Close();
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

    private sealed class WindowsMilFactAttribute : FactAttribute
    {
        public WindowsMilFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (!OperatingSystem.IsWindows() || PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.WindowsMil)
                Skip = "Requires the independently selected native Windows WPF lane.";
        }
    }

    [WindowsMilFact]
    public void NativeWindowSystemCommandsKeepPostedHwndMessages()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
            var window = new Window { Width = 200, Height = 100 };
            var commands = new List<int>();
            HwndSource? source = null;
            HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x0112) // WM_SYSCOMMAND
                {
                    commands.Add(wParam.ToInt32());
                    handled = true; // Observe dispatch without showing/minimizing the test window.
                }
                return IntPtr.Zero;
            };
            try
            {
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                source = HwndSource.FromHwnd(handle);
                source.Should().NotBeNull();
                source!.AddHook(hook);
                SystemCommands.MaximizeWindow(window);
                SystemCommands.MinimizeWindow(window);
                SystemCommands.RestoreWindow(window);
                SystemCommands.CloseWindow(window);
                commands.Should().BeEmpty(); // These remain asynchronous Windows messages.
                window.WindowState.Should().Be(WindowState.Normal);
                window.IsDisposed.Should().BeFalse();
                window.Dispatcher.Invoke(() => { }, Threading.DispatcherPriority.ApplicationIdle);
                commands.Should().Equal(0xF030, 0xF020, 0xF120, 0xF060);
            }
            finally
            {
                source?.RemoveHook(hook);
                if (!window.IsDisposed) window.Close();
            }
        });
    }

    [PortableInputFact]
    public void PortableDeviceStateAndCommittedTextHaveOneOwnerOnEveryOs()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement { Focusable = true };
            host.RootVisual = root; host.SetClientSize(200, 100);
            Keyboard.Focus(root).Should().BeSameAs(root);
            int keyDowns = 0, keyUps = 0, texts = 0;
            root.KeyDown += (_, e) =>
            {
                e.Key.Should().Be(Key.A);
                e.InputSource.Should().BeSameAs(source);
                Keyboard.IsKeyDown(Key.A).Should().BeTrue();
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift);
                ++keyDowns;
            };
            root.KeyUp += (_, e) => { e.Key.Should().Be(Key.A); ++keyUps; };
            root.TextInput += (_, e) => { e.Text.Should().Be("A"); ++texts; };
            root.MouseDown += (_, e) => e.LeftButton.Should().Be(MouseButtonState.Pressed);
            try
            {
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.KeyDown, key: "A", modifiers: PortableInputModifiers.Shift));
                texts.Should().Be(0); // Key delivery must not fabricate a second text event.
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.TextInput, character: 'A', modifiers: PortableInputModifiers.Shift));
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.KeyUp, key: "A"));
                keyDowns.Should().Be(1); keyUps.Should().Be(1); texts.Should().Be(1);
                Keyboard.IsKeyDown(Key.A).Should().BeFalse(); Keyboard.Modifiers.Should().Be(ModifierKeys.None);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseDown, x: 10, y: 10, button: PortableMouseButton.Left));
                Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseUp, x: 10, y: 10, button: PortableMouseButton.Left));
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            }
            finally { Keyboard.ClearFocus(); Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void PointerSourceDisposalReleasesOnlyOwnedButtonsAndCaptureWithoutMouseUp()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            var firstRoot = new HitTestElement();
            var secondRoot = new HitTestElement();
            first.RootVisual = firstRoot; first.SetClientSize(200, 100);
            second.RootVisual = secondRoot; second.SetClientSize(200, 100);
            int ups = 0, captureLost = 0;
            firstRoot.MouseUp += (_, _) => ups++;
            secondRoot.MouseUp += (_, _) => ups++;
            firstRoot.LostMouseCapture += (_, _) => captureLost++;
            SendPointerButton(first, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            SendPointerButton(second, PortableInputEventKind.MouseDown, PortableMouseButton.Right);
            Mouse.Capture(firstRoot).Should().BeTrue();
            first.Dispose();
            Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            Mouse.RightButton.Should().Be(MouseButtonState.Pressed);
            Mouse.Captured.Should().BeNull();
            captureLost.Should().Be(1);
            ups.Should().Be(0);
            second.Dispose();
            Mouse.RightButton.Should().Be(MouseButtonState.Released);
            ups.Should().Be(0);
        });
    }

    [PortableInputFact]
    public void PointerSourceDisposalPreservesAnotherSourcesLaterPressAndCapture()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            first.RootVisual = new HitTestElement(); first.SetClientSize(200, 100);
            var root = new HitTestElement();
            second.RootVisual = root; second.SetClientSize(200, 100);
            SendPointerButton(first, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            SendPointerButton(second, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            Mouse.Capture(root).Should().BeTrue();
            first.Dispose();
            Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
            Mouse.Captured.Should().BeSameAs(root);
            SendPointerButton(second, PortableInputEventKind.MouseUp, PortableMouseButton.Left);
            Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            Mouse.Capture(null);
        });
    }

    [PortableInputFact]
    public void PointerSourceReplacementClearsButtonsButMovementAcrossSourcesDoesNot()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            var firstRoot = new HitTestElement();
            first.RootVisual = firstRoot; first.SetClientSize(200, 100);
            second.RootVisual = new HitTestElement(); second.SetClientSize(200, 100);
            SendPointerButton(first, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            PortableWindowActivationService.ProcessInput((PresentationSource)second,
                new PortableInputEventArgs(PortableInputEventKind.MouseMove, x: 10, y: 10));
            Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
            first.RootVisual = firstRoot;
            Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
            first.RootVisual = new HitTestElement();
            Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            SendPointerButton(first, PortableInputEventKind.MouseDown, PortableMouseButton.Right);
            first.RootVisual = null;
            Mouse.RightButton.Should().Be(MouseButtonState.Released);
        });
    }

    [PortableInputFact]
    public void PointerSourceCaptureRoutingRetainsOriginalAndRoutedPressOwnership()
    {
        RunInUiApartment(() =>
        {
            foreach (bool disposeOrigin in new[] { true, false })
            {
                using var origin = PortablePresentationSourceHost.Create();
                using var captured = PortablePresentationSourceHost.Create();
                var originRoot = new HitTestElement();
                var capturedRoot = new HitTestElement();
                origin.RootVisual = originRoot; origin.SetClientSize(200, 100);
                captured.RootVisual = capturedRoot; captured.SetClientSize(200, 100);
                int capturedDowns = 0, originDowns = 0;
                originRoot.MouseDown += (_, _) => originDowns++;
                capturedRoot.MouseDown += (_, _) => capturedDowns++;
                PortableWindowActivationService.ProcessInput((PresentationSource)captured,
                    new PortableInputEventArgs(PortableInputEventKind.MouseMove, x: 10, y: 10));
                Mouse.Capture(capturedRoot, CaptureMode.Element).Should().BeTrue();
                SendPointerButton(origin, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
                capturedDowns.Should().Be(1); originDowns.Should().Be(0);
                Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                (disposeOrigin ? origin : captured).Dispose();
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                if (disposeOrigin) Mouse.Captured.Should().BeSameAs(capturedRoot);
                else Mouse.Captured.Should().BeNull();
                Mouse.Capture(null);
            }
        });
    }

    [PortableInputFact]
    public void PointerSourceDisposalInsideDownCallbackDoesNotLeaveAPressedButton()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement();
            host.RootVisual = root; host.SetClientSize(200, 100);
            int downs = 0, ups = 0;
            root.MouseUp += (_, _) => ups++;
            root.MouseDown += (_, _) =>
            {
                Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                host.Dispose();
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                downs++;
            };
            SendPointerButton(host, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            downs.Should().Be(1); ups.Should().Be(0);
            Mouse.LeftButton.Should().Be(MouseButtonState.Released);
        });
    }

    private static void SendPointerButton(IPortablePresentationSourceHost host,
        PortableInputEventKind kind, PortableMouseButton button)
    {
        PortableWindowActivationService.ProcessInput((PresentationSource)host,
            new PortableInputEventArgs(kind, x: 10, y: 10, button: button));
    }

    [PortableInputFact]
    public void PointerModifiersSurviveNestedKeyAndTextInputWithoutRestoringReleasedKeys()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement { Focusable = true };
            host.RootVisual = root; host.SetClientSize(200, 100);
            Keyboard.Focus(root).Should().BeSameAs(root);
            int downs = 0, ups = 0, texts = 0, wheels = 0;
            root.KeyUp += (_, _) =>
            {
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
                Keyboard.IsKeyDown(Key.LeftCtrl).Should().BeFalse();
                ++ups;
            };
            root.TextInput += (_, _) =>
            {
                Keyboard.Modifiers.Should().Be(ModifierKeys.Alt);
                ++texts;
            };
            root.MouseDown += (_, _) =>
            {
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift | ModifierKeys.Control);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.KeyUp, key: "LeftCtrl"));
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift | ModifierKeys.Control);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.TextInput, character: 'x', modifiers: PortableInputModifiers.Alt));
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift | ModifierKeys.Control);
                Keyboard.IsKeyDown(Key.LeftCtrl).Should().BeFalse();
                ++downs;
            };
            root.MouseWheel += (_, _) =>
            {
                Keyboard.Modifiers.Should().Be(ModifierKeys.Control);
                ++wheels;
            };
            try
            {
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.KeyDown, key: "LeftCtrl", modifiers: PortableInputModifiers.Control));
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseDown, x: 10, y: 10, button: PortableMouseButton.Left,
                        modifiers: PortableInputModifiers.Shift | PortableInputModifiers.Control));
                downs.Should().Be(1); ups.Should().Be(1); texts.Should().Be(1);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseUp, x: 10, y: 10, button: PortableMouseButton.Left));
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseWheel, x: 10, y: 10, deltaY: 1,
                        modifiers: PortableInputModifiers.Control));
                wheels.Should().Be(1);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            }
            finally { Keyboard.ClearFocus(); Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void PointerModifierSnapshotUnwindsAfterSourceHandlerThrows()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement();
            host.RootVisual = root; host.SetClientSize(200, 100);
            var failure = new InvalidOperationException("Pointer handler failed.");
            root.MouseDown += (_, _) =>
            {
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift);
                throw failure;
            };
            try
            {
                Action deliver = () => PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseDown, x: 10, y: 10,
                        button: PortableMouseButton.Left, modifiers: PortableInputModifiers.Shift));
                deliver.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            }
            finally
            {
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseUp, x: 10, y: 10, button: PortableMouseButton.Left));
                Keyboard.ClearFocus(); Mouse.Capture(null);
            }
        });
    }

    [PortableInputFact]
    public void SystemMenuUsesTypedRegistrationAndDesktopCoordinatesWithoutSourceHandleAccess()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            int handleQueries = 0;
            bool accepted = true;
            var positions = new List<Point>();
            PortableWindowActivationService.RegisterPortableInteropService();
            PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework, out var registrar).Should().BeTrue();
            registrar!.Register(new PortableWindowActivationCallbacks(_ => activation,
                getHandle: _ => { handleQueries++; return new IntPtr(5678); })
            {
                CreateHidden = _ => activation,
                ShowSystemMenu = (owner, x, y) =>
                {
                    owner.Should().BeSameAs(activation);
                    positions.Add(new Point(x, y));
                    return accepted;
                }
            });
            var window = new Window { Width = 200, Height = 100 };
            try
            {
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                int initialHandleQueries = handleQueries;
                SystemCommands.ShowSystemMenu(window, new Point(-1234.5, 67.25));
                SystemCommands.ShowSystemMenuPhysicalCoordinates(window, new Point(-900, 450));
                positions.Should().Equal(new Point(-1234.5, 67.25), new Point(-900, 450));
                accepted = false;
                Action rejected = () => SystemCommands.ShowSystemMenu(window, new Point(12, 24));
                rejected.Should().Throw<PlatformNotSupportedException>().WithMessage("*system menu*");
                Action invalid = () => SystemCommands.ShowSystemMenu(window, new Point(double.NaN, 0));
                invalid.Should().Throw<ArgumentException>();
                positions.Count.Should().Be(3);

                // Registering a legacy host must clear the optional capability,
                // not retain the previous host's system-menu callback.
                registrar.Register(new PortableWindowActivationCallbacks(_ => activation));
                rejected.Should().Throw<PlatformNotSupportedException>();
                positions.Count.Should().Be(3);
                var failure = new InvalidOperationException("Host menu failure.");
                registrar.Register(new PortableWindowActivationCallbacks(_ => activation)
                    { ShowSystemMenu = (_, _, _) => throw failure });
                rejected.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                handleQueries.Should().Be(initialHandleQueries);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void SystemWindowCommandsUpdatePortableStateBeforeHostCreation()
    {
        RunInUiApartment(() =>
        {
            var window = new Window { Width = 200, Height = 100 };
            try
            {
                window.PortableWindowActivation.Should().BeNull();
                SystemCommands.MaximizeWindow(window);
                window.WindowState.Should().Be(WindowState.Maximized);
                SystemCommands.MinimizeWindow(window);
                window.WindowState.Should().Be(WindowState.Minimized);
                SystemCommands.RestoreWindow(window);
                window.WindowState.Should().Be(WindowState.Normal);
                window.PortableWindowActivation.Should().BeNull();
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
            }
        });
    }

    [PortableInputFact]
    public void PortableWindowBackdropNeverTreatsItsHostHandleAsWpfHwnd()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            int handleQueries = 0;
            PortableWindowActivationService.Register(
                activate: _ => activation,
                createHidden: _ => activation,
                getHandle: _ => { handleQueries++; return new IntPtr(5678); });
            var window = new Window { Width = 200, Height = 100 };
            try
            {
#pragma warning disable WPF0001 // This contract deliberately tests experimental theme/backdrop integration.
                window.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
                new WindowInteropHelper(window).EnsureHandle();
                int initialHandleQueries = handleQueries;
                Appearance.WindowBackdropManager.SetBackdrop(window, WindowBackdropType.MainWindow)
                    .Should().BeFalse();
                handleQueries.Should().Be(initialHandleQueries);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void SystemWindowCommandsUsePortableOwnerStateAndCancelableCloseOnEveryOs()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var states = new List<WindowState>();
            int handleQueries = 0, closes = 0, disposals = 0;
            PortableWindowActivationService.Register(
                activate: _ => activation, createHidden: _ => activation,
                getHandle: owner =>
                {
                    owner.Should().BeSameAs(activation);
                    handleQueries++;
                    return new IntPtr(5678); // Deliberately not an HWND.
                },
                setWindowState: (owner, state) =>
                {
                    owner.Should().BeSameAs(activation);
                    states.Add((WindowState)state);
                },
                close: owner => { owner.Should().BeSameAs(activation); closes++; },
                dispose: owner => { owner.Should().BeSameAs(activation); disposals++; });
            var window = new Window { Width = 200, Height = 100 };
            bool cancelClose = true;
            int closing = 0, closed = 0;
            window.Closing += (_, e) => { closing++; e.Cancel = cancelClose; };
            window.Closed += (_, _) => closed++;
            try
            {
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                int initialHandleQueries = handleQueries;
                SystemCommands.MaximizeWindow(window);
                window.WindowState.Should().Be(WindowState.Maximized);
                SystemCommands.MinimizeWindow(window);
                window.WindowState.Should().Be(WindowState.Minimized);
                SystemCommands.RestoreWindow(window);
                window.WindowState.Should().Be(WindowState.Normal);
                states.Should().Equal(WindowState.Maximized, WindowState.Minimized, WindowState.Normal);

                SystemCommands.CloseWindow(window);
                window.IsDisposed.Should().BeFalse();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                closing.Should().Be(1); closed.Should().Be(0);
                closes.Should().Be(0); disposals.Should().Be(0);
                cancelClose = false;
                SystemCommands.CloseWindow(window);
                window.IsDisposed.Should().BeTrue();
                window.PortableWindowActivation.Should().BeNull();
                closing.Should().Be(2); closed.Should().Be(1);
                closes.Should().Be(1); disposals.Should().Be(1);
                handleQueries.Should().Be(initialHandleQueries);
            }
            finally
            {
                cancelClose = false;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomChromeUsesPortableOwnerBeforeAndAfterSourceCreation(bool attachBeforeSource)
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var borders = new List<WindowStyle>();
            PortableWindowActivationService.Register(
                activate: _ => activation, createHidden: _ => activation,
                getHandle: _ => new IntPtr(5678),
                setWindowBorder: (owner, _, style) =>
                {
                    owner.Should().BeSameAs(activation);
                    borders.Add((WindowStyle)style);
                });
            var window = new Window { Width = 200, Height = 100, WindowStyle = WindowStyle.SingleBorderWindow };
            var chrome = new Shell.WindowChrome { CaptionHeight = 32, GlassFrameThickness = new Thickness(0) };
            try
            {
                if (attachBeforeSource) Shell.WindowChrome.SetWindowChrome(window, chrome);
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                if (!attachBeforeSource) Shell.WindowChrome.SetWindowChrome(window, chrome);
                var source = (IPortableWindowStateSource)window;
                source.TryGetPortableWindowState(out var custom).Should().BeTrue();
                custom.WindowStyle.Should().Be((int)WindowStyle.None);
                chrome.CaptionHeight = 40;
                Shell.WindowChrome.SetWindowChrome(window, null);
                source.TryGetPortableWindowState(out var standard).Should().BeTrue();
                standard.WindowStyle.Should().Be((int)WindowStyle.SingleBorderWindow);
                borders.Should().Contain(WindowStyle.SingleBorderWindow);
                if (!attachBeforeSource) borders.Should().Contain(WindowStyle.None);
                window.WindowStyle.Should().Be(WindowStyle.SingleBorderWindow);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureHandleCreatesOneHiddenSourceAndReusesItForShow(bool showAfterCreation)
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            int hiddenCreates = 0, ordinaryCreates = 0, shows = 0, closes = 0, disposals = 0;
            PortableWindowActivationService.Register(
                activate: _ => { ordinaryCreates++; return new object(); },
                createHidden: _ => { hiddenCreates++; return activation; },
                getHandle: value => ReferenceEquals(value, activation) ? new IntPtr(5678) : IntPtr.Zero,
                show: value => { value.Should().BeSameAs(activation); shows++; },
                close: _ => closes++,
                dispose: _ => disposals++);
            var window = new Window { Width = 200, Height = 100 };
            var interop = new WindowInteropHelper(window);
            int initialized = 0;
            window.SourceInitialized += (_, _) =>
            {
                initialized++;
                interop.Handle.Should().Be(new IntPtr(5678));
                window.IsVisible.Should().BeFalse();
                // Event reentrancy must see the published identity, not create a second source.
                interop.EnsureHandle().Should().Be(interop.Handle);
            };
            try
            {
                interop.Handle.Should().Be(IntPtr.Zero);
                Visibility originalVisibility = window.Visibility;
                interop.EnsureHandle().Should().Be(new IntPtr(5678));
                interop.EnsureHandle().Should().Be(new IntPtr(5678));
                window.Visibility.Should().Be(originalVisibility);
                window.IsVisible.Should().BeFalse();
                window.IsActive.Should().BeFalse();
                shows.Should().Be(0);
                if (showAfterCreation)
                {
                    window.Show();
                    interop.EnsureHandle().Should().Be(new IntPtr(5678));
                    window.Hide();
                    window.Show();
                    shows.Should().Be(2);
                }
                hiddenCreates.Should().Be(1);
                ordinaryCreates.Should().Be(0);
                initialized.Should().Be(1);
                window.Close();
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(0)] // Legacy callback set has no hidden creation capability.
    [InlineData(1)] // Registered hidden factory rejects this window.
    [InlineData(2)] // Factory returns a source without a usable identity.
    public void EnsureHandleFailsClosedForUnavailableHiddenSources(int failure)
    {
        RunInUiApartment(() =>
        {
            int ordinaryCreates = 0, closes = 0, disposals = 0, initialized = 0;
            PortableWindowActivationService.Register(
                activate: _ => { ordinaryCreates++; return new object(); },
                createHidden: failure == 0 ? null : _ => failure == 1 ? null! : new object(),
                getHandle: _ => IntPtr.Zero,
                close: _ => closes++, dispose: _ => disposals++);
            var window = new Window();
            window.SourceInitialized += (_, _) => initialized++;
            var interop = new WindowInteropHelper(window);
            try
            {
                Action ensure = () => interop.EnsureHandle();
                if (failure == 0)
                    ensure.Should().Throw<PlatformNotSupportedException>().WithMessage("*hidden window sources*");
                else
                    ensure.Should().Throw<InvalidOperationException>();
                ordinaryCreates.Should().Be(0);
                initialized.Should().Be(0);
                interop.Handle.Should().Be(IntPtr.Zero);
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(failure == 2 ? 1 : 0);
                disposals.Should().Be(failure == 2 ? 1 : 0);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ExplicitRegistrationRoutesWindowLifecycleOnEveryPlatform()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Clear();
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
            var activation = new object();
            int creates = 0, shows = 0, hides = 0, requests = 0, runs = 0, closes = 0, disposals = 0;
            string? title = null;
            PortableWindowActivationService.Register(
                activate: _ => { creates++; return activation; },
                show: value => { value.Should().BeSameAs(activation); shows++; },
                hide: value => { value.Should().BeSameAs(activation); hides++; },
                setTitle: (_, value) => title = value,
                close: _ => closes++,
                run: _ => runs++,
                dispose: _ => disposals++,
                getHandle: _ => new IntPtr(1234),
                requestActivation: _ => { requests++; return true; });
            var window = new Window { Width = 200, Height = 100 };
            int initialized = 0;
            window.SourceInitialized += (_, _) => initialized++;
            try
            {
                PortableWindowActivationService.IsEnabled.Should().BeTrue();
                window.Show();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                window.Title = "Portable title";
                title.Should().Be("Portable title");
                window.Activate().Should().BeTrue();
                PortableWindowActivationService.GetHandle(activation).Should().Be(new IntPtr(1234));
                PortableWindowActivationService.TryRun(window).Should().BeTrue();
                window.Hide();
                window.Show();
                creates.Should().Be(1);
                initialized.Should().Be(1);
                shows.Should().Be(2);
                hides.Should().Be(1);
                requests.Should().Be(1);
                runs.Should().Be(1);
                window.Close();
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                if (!window.IsDisposed)
                {
                    window.Close();
                }
                PortableWindowActivationService.Clear();
            }
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
        });
    }

    [PortableInputFact]
    public void PortableDialogHideReturnsAndReusesSourceWithCancelableResult()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var window = new Window { Width = 200, Height = 100 };
            int creates = 0, runs = 0, shows = 0, hides = 0, closes = 0, disposals = 0;
            int closingCalls = 0;
            window.Closing += (_, e) => e.Cancel = ++closingCalls == 1;
            PortableWindowActivationService.Register(
                activate: _ => { creates++; return activation; },
                show: _ => shows++,
                hide: _ => { PortableModalInputScope.IsActive.Should().BeFalse(); hides++; },
                close: _ => { PortableModalInputScope.IsActive.Should().BeFalse(); closes++; },
                dispose: _ => disposals++,
                getHandle: _ => new IntPtr(5678),
                run: _ => throw new InvalidOperationException("Application loop must not run a dialog."),
                releaseDialog: (_, completed) => completed(),
                runDialog: (owner, continuation) =>
                {
                    owner.Should().BeSameAs(activation);
                    PortableModalInputScope.AllowsInput(window).Should().BeTrue();
                    PortableModalInputScope.AllowsInput(activation).Should().BeFalse();
                    ComponentDispatcher.IsThreadModal.Should().BeTrue();
                    continuation().Should().BeTrue();
                    runs++;
                    if (runs == 1)
                    {
                        window.Hide();
                        window.IsDisposed.Should().BeFalse();
                    }
                    else
                    {
                        window.DialogResult = true;
                        window.DialogResult.Should().BeNull();
                        continuation().Should().BeTrue(); // Canceled close keeps pumping.
                        window.DialogResult = true;
                    }
                    continuation().Should().BeFalse();
                });
            try
            {
                window.ShowDialog().Should().Be(false);
                PortableModalInputScope.IsActive.Should().BeFalse();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                closes.Should().Be(0); disposals.Should().Be(0);
                window.ShowDialog().Should().Be(true);
                creates.Should().Be(1); runs.Should().Be(2); shows.Should().Be(2); hides.Should().Be(1);
                closes.Should().Be(1); disposals.Should().Be(1);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void PortableDialogRequiresCapabilityBeforeShowAndRejectsPrematureReturn()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var window = new Window { Width = 200, Height = 100 };
            int creates = 0, hides = 0;
            PortableWindowActivationService.Register(activate: _ => { creates++; return activation; });
            try
            {
                Action show = () => window.ShowDialog();
                show.Should().Throw<PlatformNotSupportedException>().WithMessage("*dialog run loop*");
                creates.Should().Be(0); window.IsVisible.Should().BeFalse();
                PortableWindowActivationService.Register(activate: _ => activation,
                    getHandle: _ => new IntPtr(5678), runDialog: (_, _) => { });
                show.Should().Throw<PlatformNotSupportedException>().WithMessage("*dialog release completion*");
                creates.Should().Be(0); window.IsVisible.Should().BeFalse();
                PortableWindowActivationService.Register(activate: _ => activation,
                    getHandle: _ => new IntPtr(5678), runDialog: (_, _) => { },
                    hide: _ => hides++,
                    releaseDialog: (_, completed) => completed());
                show.Should().Throw<InvalidOperationException>().WithMessage("*still open*");
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                window.IsVisible.Should().BeFalse();
                window.IsDisposed.Should().BeFalse();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                hides.Should().Be(1);
                var failure = new InvalidOperationException("Host event pump failure.");
                PortableWindowActivationService.Register(activate: _ => activation,
                    hide: _ => hides++,
                    runDialog: (_, _) => throw failure, releaseDialog: (_, completed) => completed());
                show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                window.IsVisible.Should().BeFalse();
                window.IsDisposed.Should().BeFalse();
                hides.Should().Be(2);
                var hideFailure = new InvalidOperationException("Host hide failure.");
                PortableWindowActivationService.Register(activate: _ => activation,
                    hide: _ => throw hideFailure,
                    runDialog: (_, _) => throw failure, releaseDialog: (_, completed) => completed());
                var combined = show.Should().Throw<AggregateException>().Which;
                combined.InnerExceptions.Should().Contain(failure).And.Contain(hideFailure);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                PortableModalInputScope.IsActive.Should().BeFalse();
                // Repair the deliberately rejected hide before the next case.
                PortableWindowActivationService.Register(activate: _ => activation, hide: _ => { });
                window.Hide();
                PortableWindowActivationService.Register(activate: _ => activation);
                show.Should().Throw<PlatformNotSupportedException>(); // Clear old optional callback.
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void NestedPortableDialogScopesRestoreTheOuterInvocation()
    {
        RunInUiApartment(() =>
        {
            var outer = new Window { Width = 200, Height = 100 };
            var inner = new Window { Width = 100, Height = 100 };
            int runs = 0;
            PortableWindowActivationService.Register(activate: window => window,
                getHandle: owner => ReferenceEquals(owner, outer) ? new IntPtr(5678) : new IntPtr(5679),
                releaseDialog: (_, completed) => completed(),
                runDialog: (owner, continuation) =>
                {
                    runs++;
                    ComponentDispatcher.IsThreadModal.Should().BeTrue();
                    continuation().Should().BeTrue();
                    if (ReferenceEquals(owner, outer))
                    {
                        inner.ShowDialog().Should().Be(false);
                        ComponentDispatcher.IsThreadModal.Should().BeTrue();
                        continuation().Should().BeTrue();
                    }
                    ((Window)owner).Hide();
                    continuation().Should().BeFalse();
                });
            try
            {
                outer.ShowDialog().Should().Be(false);
                runs.Should().Be(2);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
            }
            finally
            {
                inner.Close(); outer.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void ModalInputAdmissionPreservesEnabledStateAndRejectsOtherSourceReports()
    {
        RunInUiApartment(() =>
        {
            var owner = new Window { Width = 200, Height = 100, IsEnabled = false };
            var dialog = new Window { Width = 200, Height = 100 };
            using IPortablePresentationSourceHost ownerHost = PortablePresentationSourceHost.Create();
            ownerHost.RootVisual = owner;
            try
            {
                using (PortableModalInputScope.Enter(dialog))
                {
                    PortableWindowActivationService.IsModalInputAllowed(owner).Should().BeFalse();
                    PortableWindowActivationService.IsModalInputAllowed(dialog).Should().BeTrue();
                    PortableWindowActivationService.IsModalInputAllowed(new HitTestElement()).Should().BeFalse();
                    var input = new PortableInputEventArgs(PortableInputEventKind.MouseDown,
                        x: 10, y: 10, button: PortableMouseButton.Left);
                    PortableWindowActivationService.ProcessInput((PresentationSource)ownerHost, input);
                    input.Handled.Should().BeTrue();
                    owner.IsEnabled = true; // Application intent does not remove the modal restriction.
                    PortableWindowActivationService.IsModalInputAllowed(owner).Should().BeFalse();
                    PortableWindowActivationService.ProcessDragDropEvent(owner, 0,
                        Array.Empty<string>(), "blocked", 10, 10, 1, 1).Should().Be(0);
                }
                owner.IsEnabled.Should().BeTrue();
                PortableWindowActivationService.IsModalInputAllowed(owner).Should().BeTrue();
            }
            finally { ownerHost.RootVisual = null; owner.Close(); dialog.Close(); }
        });
    }

    [PortableInputFact]
    public void ModalEntryReleasesCaptureFromAnUnownedSource()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement { Focusable = true };
            host.RootVisual = root; host.SetClientSize(200, 100);
            Mouse.Capture(root, CaptureMode.Element).Should().BeTrue();
            using (PortableModalInputScope.Enter(new object()))
            {
                PortableWindowActivationService.PrepareForModalInput();
                Mouse.Captured.Should().BeNull();
            }
            Mouse.Capture(null);
        });
    }

    [PortableInputFact]
    public void ModalReportCannotBeRedirectedToBlockedCapture()
    {
        RunInUiApartment(() =>
        {
            using var capturedHost = PortablePresentationSourceHost.Create();
            using var dialogHost = PortablePresentationSourceHost.Create();
            var captured = new HitTestElement();
            var dialog = new Window { Width = 200, Height = 100 };
            capturedHost.RootVisual = captured; capturedHost.SetClientSize(200, 100);
            dialogHost.RootVisual = dialog;
            int moves = 0;
            captured.MouseMove += (_, _) => moves++;
            try
            {
                Mouse.Capture(captured, CaptureMode.Element).Should().BeTrue();
                using (PortableModalInputScope.Enter(dialog))
                {
                    var input = new PortableInputEventArgs(PortableInputEventKind.MouseMove, x: 10, y: 10);
                    PortableWindowActivationService.ProcessInput((PresentationSource)dialogHost, input);
                    input.Handled.Should().BeTrue();
                    moves.Should().Be(0);
                }
            }
            finally { Mouse.Capture(null); dialogHost.RootVisual = null; dialog.Close(); }
        });
    }

    [Fact]
    public void RegisteredHostRejectionDoesNotCreateAWindowsMilWindow()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(activate: _ => null!);
            var window = new Window();
            int initialized = 0;
            window.SourceInitialized += (_, _) => initialized++;
            try
            {
                Action show = window.Show;
                show.Should().Throw<InvalidOperationException>()
                    .WithMessage("*Falling back to Windows MIL is not permitted*");
                initialized.Should().Be(0);
                window.PortableWindowActivation.Should().BeNull();
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ActivePortableWindowRequiresItsHostRunLoop()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(activate: _ => new object());
            var window = new Window { Width = 200, Height = 100 };
            try
            {
                window.Show();
                Action run = () => PortableWindowActivationService.TryRun(window);
                run.Should().Throw<InvalidOperationException>()
                    .WithMessage("*no run-loop callback*");
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void RejectedActivationRequestDoesNotFabricateActiveState()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(
                activate: _ => new object(), requestActivation: _ => false);
            var window = new Window { Width = 200, Height = 100 };
            int activations = 0;
            window.Activated += (_, _) => activations++;
            try
            {
                window.Show();
                window.Activate().Should().BeFalse();
                window.IsActive.Should().BeFalse();
                activations.Should().Be(0);
                PortableWindowActivationService.SetActivationState(window, true);
                window.IsActive.Should().BeTrue();
                activations.Should().Be(1);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void CapturedElementReceivesMouseInputReportedByAnotherPresentationSource()
    {
        RunInUiApartment(VerifyCapturedElementReceivesMouseInputReportedByAnotherPresentationSource);
    }

    [Fact]
    public void SubtreeCapturePreservesReportedPresentationSourceAndCapture()
    {
        RunInUiApartment(VerifySubtreeCapturePreservesReportedPresentationSourceAndCapture);
    }

    [Fact]
    public void DispatcherIdleWorkNotificationExcludesRegularlyPumpedPrioritiesAndUnsubscribes()
    {
        RunInUiApartment(VerifyDispatcherIdleWorkNotification);
    }

    private static void VerifyDispatcherIdleWorkNotification()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();
        var window = new Window();
        int notificationCount = 0;

        activationService.TryRegisterDispatcherIdleWorkNotification(
            window,
            () => notificationCount++,
            out IDisposable? registration).Should().BeTrue();
        registration.Should().NotBeNull();

        DispatcherOperation background = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            static () => { });
        DispatcherOperation idle = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            static () => { });

        notificationCount.Should().Be(1);
        registration!.Dispose();
        DispatcherOperation afterDispose = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            static () => { });
        notificationCount.Should().Be(1);

        background.Abort();
        idle.Abort();
        afterDispose.Abort();
    }

    private static void VerifyCapturedElementReceivesMouseInputReportedByAnotherPresentationSource()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();

        using IPortablePresentationSourceHost captureSourceHost = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost reportedSourceHost = PortablePresentationSourceHost.Create();
        var captureSource = (PresentationSource)captureSourceHost;
        var reportedSource = (PresentationSource)reportedSourceHost;
        var captureRoot = new HitTestElement();
        var reportedRoot = new HitTestElement();
        captureSourceHost.RootVisual = captureRoot;
        reportedSourceHost.RootVisual = reportedRoot;
        captureSourceHost.SetClientSize(500.0, 500.0);
        reportedSourceHost.SetClientSize(500.0, 500.0);
        captureSourceHost.SetClientOrigin(100.0, 200.0);
        reportedSourceHost.SetClientOrigin(400.0, 500.0);

        ProcessInput(
            activationService,
            captureSource,
            MouseDownInputKind,
            x: 20.0,
            y: 30.0,
            button: LeftMouseButton);
        captureRoot.CaptureMouse().Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(captureRoot);

        int capturedMoveCount = 0;
        int capturedUpCount = 0;
        int reportedMoveCount = 0;
        int reportedUpCount = 0;
        int lostCaptureCount = 0;
        Point capturedMovePoint = default;
        captureRoot.MouseMove += (_, e) =>
        {
            capturedMoveCount++;
            capturedMovePoint = e.GetPosition(captureRoot);
        };
        captureRoot.MouseUp += (_, _) => capturedUpCount++;
        captureRoot.LostMouseCapture += (_, _) => lostCaptureCount++;
        reportedRoot.MouseMove += (_, _) => reportedMoveCount++;
        reportedRoot.MouseUp += (_, _) => reportedUpCount++;
        try
        {
            ProcessInput(
                activationService,
                reportedSource,
                MouseMoveInputKind,
                x: 5.0,
                y: 7.0);
            ProcessInput(
                activationService,
                reportedSource,
                MouseUpInputKind,
                x: 5.0,
                y: 7.0,
                button: LeftMouseButton);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(captureSource);
            Mouse.Captured.Should().BeSameAs(captureRoot);
            capturedMoveCount.Should().Be(1);
            capturedUpCount.Should().Be(1);
            capturedMovePoint.X.Should().BeApproximately(305.0, 0.000001);
            capturedMovePoint.Y.Should().BeApproximately(307.0, 0.000001);
            reportedMoveCount.Should().Be(0);
            reportedUpCount.Should().Be(0);
            lostCaptureCount.Should().Be(0);
        }
        finally
        {
            captureRoot.ReleaseMouseCapture();
        }

        Mouse.Captured.Should().BeNull();
        lostCaptureCount.Should().Be(1);
    }

    private static void VerifySubtreeCapturePreservesReportedPresentationSourceAndCapture()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();

        using IPortablePresentationSourceHost captureSourceHost = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost reportedSourceHost = PortablePresentationSourceHost.Create();
        var captureSource = (PresentationSource)captureSourceHost;
        var reportedSource = (PresentationSource)reportedSourceHost;
        var captureRoot = new HitTestElement();
        var reportedRoot = new HitTestElement(captureRoot);
        captureSourceHost.RootVisual = captureRoot;
        reportedSourceHost.RootVisual = reportedRoot;
        captureSourceHost.SetClientSize(500.0, 500.0);
        reportedSourceHost.SetClientSize(500.0, 500.0);

        ProcessInput(
            activationService,
            captureSource,
            MouseMoveInputKind,
            x: 20.0,
            y: 30.0);
        Mouse.Capture(captureRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(captureRoot);

        int reportedMoveCount = 0;
        object? originalSource = null;
        reportedRoot.MouseMove += (_, e) =>
        {
            reportedMoveCount++;
            originalSource = e.OriginalSource;
        };
        try
        {
            ProcessInput(
                activationService,
                reportedSource,
                MouseMoveInputKind,
                x: 5.0,
                y: 7.0);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(reportedSource);
            Mouse.Captured.Should().BeSameAs(captureRoot);
            reportedMoveCount.Should().Be(1);
            originalSource.Should().BeSameAs(reportedRoot);
        }
        finally
        {
            Mouse.Capture(null);
        }

        Mouse.Captured.Should().BeNull();
    }

    [PortableInputFact]
    public void NativePointerReportsPreserveFiveButtonsCoordinatesClicksAndEventTime()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement();
            host.RootVisual = root; host.SetClientSize(200, 100);
            PortablePointerInput? expected = null;
            int received = 0;
            void Check(object sender, MouseButtonEventArgs args)
            {
                ++received;
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(expected);
                args.Timestamp.Should().Be(1250);
                args.ClickCount.Should().Be(expected!.ClickCount);
                args.GetPosition(root).Should().Be(new Point(10.25, 12.75));
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift | ModifierKeys.Control);
                args.ButtonState.Should().Be(expected.Kind == PortablePointerEventKind.Down
                    ? MouseButtonState.Pressed : MouseButtonState.Released);
            }
            root.PreviewMouseDown += Check; root.MouseDown += Check;
            root.PreviewMouseUp += Check; root.MouseUp += Check;
            for (int button = 0; button < 5; button++)
                foreach (var kind in new[] { PortablePointerEventKind.Down, PortablePointerEventKind.Up })
                {
                    expected = new PortablePointerInput(kind, 10.25, 12.75, 1.25025, button, button + 3,
                        PortablePointerModifiers.Shift | PortablePointerModifiers.Super);
                    PortableWindowActivationService.TryProcessNativePointerInput(source, expected,
                        PortableInputModifiers.Shift | PortableInputModifiers.Control, out _).Should().BeTrue();
                }
            received.Should().Be(20);
            Keyboard.Modifiers.Should().Be(ModifierKeys.None);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsPreserveFractionalCaptureRouteAndOriginalPacket()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost captured = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost origin = PortablePresentationSourceHost.Create();
            var target = new HitTestElement();
            captured.RootVisual = target; captured.SetClientSize(500, 500); captured.SetClientOrigin(100, 200);
            origin.RootVisual = new HitTestElement(); origin.SetClientSize(500, 500); origin.SetClientOrigin(400, 500);
            target.CaptureMouse().Should().BeTrue();
            int moves = 0, ups = 0;
            bool observing = true;
            var drag = new PortablePointerInput(PortablePointerEventKind.Drag, -15.125, 17.75, 2.25, 0, 1, 0);
            var up = new PortablePointerInput(PortablePointerEventKind.Up, -14.875, 18.25, 2.5, 0, 4, 0);
            target.MouseMove += (_, args) =>
            {
                if (!observing) return; // Capture cleanup has its own ordinary synchronization.
                var packet = PortableWindowActivationService.GetNativePointerInput(args);
                packet.Should().NotBeNull();
                args.GetPosition(target).Should().Be(new Point(packet!.X + 300, packet.Y + 300));
                ++moves;
            };
            target.MouseUp += (_, args) =>
            {
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(up);
                args.Timestamp.Should().Be(2500); args.ClickCount.Should().Be(4);
                ++ups;
            };
            try
            {
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)origin, drag, 0, out _).Should().BeTrue();
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)origin, up, 0, out _).Should().BeTrue();
                moves.Should().Be(2); ups.Should().Be(1);
                Mouse.Captured.Should().BeSameAs(target);
            }
            finally { observing = false; Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsKeepOuterClickPacketThroughNestedDelivery()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            var outer = new PortablePointerInput(PortablePointerEventKind.Down, 10.25, 12.75, 1.25, 0, 7, 0);
            var inner = new PortablePointerInput(PortablePointerEventKind.Down, 22.125, 15.5, 2.5, 1, 2, 0);
            var counts = new List<int>();
            root.PreviewMouseDown += (sender, args) =>
            {
                if (args.ChangedButton != MouseButton.Left) return;
                PortableWindowActivationService.TryProcessNativePointerInput(source, inner, 0, out _).Should().BeTrue();
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(outer);
                args.Timestamp.Should().Be(1250); args.ClickCount.Should().Be(7);
            };
            root.MouseDown += (_, args) =>
            {
                var packet = args.ChangedButton == MouseButton.Left ? outer : inner;
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(packet);
                counts.Add(args.ClickCount);
            };
            PortableWindowActivationService.TryProcessNativePointerInput(source, outer, 0, out _).Should().BeTrue();
            counts.Should().Equal(2, 7);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRejectUnadmittedPacketsWithoutLegacyFallback()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            int events = 0;
            root.MouseDown += (_, _) => ++events; root.MouseWheel += (_, _) => ++events;
            var unsupported = new[]
            {
                new PortablePointerInput(PortablePointerEventKind.Down, 10, 10, 1, 5, 1, 0),
                new PortablePointerInput(PortablePointerEventKind.Down, 10, 10, double.MaxValue, 0, 1, 0),
                new PortablePointerInput(PortablePointerEventKind.Scroll, 10, 10, 1, -1, 0, 0, 0, 1),
                new PortablePointerInput(PortablePointerEventKind.Scroll, 10, 10, 1, -1, 0, 0, 1, 2, PortablePointerScrollUnit.Points)
            };
            foreach (var packet in unsupported)
            {
                PortableWindowActivationService.TryProcessNativePointerInput(source, packet, 0, out bool handled).Should().BeFalse();
                handled.Should().BeFalse();
            }
            var down = new PortablePointerInput(PortablePointerEventKind.Down, 10, 10, 1, 0, 1, 0);
            PortableWindowActivationService.TryProcessNativePointerInput(source, down, (PortableInputModifiers)16, out _).Should().BeFalse();
            events.Should().Be(0); Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            host.Dispose();
            PortableWindowActivationService.TryProcessNativePointerInput(source, down, 0, out _).Should().BeFalse();
        });
    }

    [PortableInputFact]
    public void NativePointerReportsUseWrappingNativeMillisecondsNotDispatchTime()
    {
        PortableWindowActivationService.NativePointerTimestamp(1.25025).Should().Be(1250);
        PortableWindowActivationService.NativePointerTimestamp(2147483.648).Should().Be(int.MinValue);
        PortableWindowActivationService.NativePointerTimestamp(4294968).Should().Be(704);
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue })
        {
            Action convert = () => PortableWindowActivationService.NativePointerTimestamp(invalid);
            convert.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [PortableInputFact]
    public void NativePointerReportsStopAfterMoveCallbackDisposesSource() =>
        VerifyNativePointerSourceRetirement(dispose: true);

    [PortableInputFact]
    public void NativePointerReportsDoNotDeliverOldDownToReplacementRoot() =>
        VerifyNativePointerSourceRetirement(dispose: false);

    private static void VerifyNativePointerSourceRetirement(bool dispose)
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var oldRoot = new HitTestElement();
            var replacement = new HitTestElement();
            host.RootVisual = oldRoot; host.SetClientSize(200, 100);
            int oldDowns = 0, newDowns = 0, moves = 0;
            MouseEventArgs? retired = null;
            oldRoot.MouseDown += (_, _) => ++oldDowns;
            replacement.MouseDown += (_, _) => ++newDowns;
            oldRoot.PreviewMouseMove += (_, args) =>
            {
                if (PortableWindowActivationService.GetNativePointerInput(args) == null) return;
                ++moves; retired = args;
                if (dispose) host.Dispose();
                else host.RootVisual = replacement;
            };
            var old = new PortablePointerInput(PortablePointerEventKind.Down, 10.25, 12.75, 1.25, 0, 3, 0);
            PortableWindowActivationService.TryProcessNativePointerInput(source, old, 0, out _).Should().BeTrue();
            moves.Should().Be(1); oldDowns.Should().Be(0); newDowns.Should().Be(0);
            Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            PortableWindowActivationService.GetNativePointerInput(retired!).Should().BeSameAs(old);
            if (!dispose)
            {
                var current = new PortablePointerInput(PortablePointerEventKind.Down, 11.25, 12.75, 2.5, 0, 1, 0);
                PortableWindowActivationService.TryProcessNativePointerInput(source, current, 0, out _).Should().BeTrue();
                newDowns.Should().Be(1); oldDowns.Should().Be(0);
            }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRetainFractionalPositionThroughSubtreeSynchronization()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            var position = new Point(10.25, 12.75);
            var move = new PortablePointerInput(PortablePointerEventKind.Move, position.X, position.Y, 1.25, -1, 0, 0);
            PortableWindowActivationService.TryProcessNativePointerInput(source, move, 0, out _).Should().BeTrue();
            root.MouseMove += (_, args) =>
            {
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeNull();
                args.GetPosition(root).Should().Be(position);
            };
            try
            {
                Mouse.Capture(root, CaptureMode.SubTree).Should().BeTrue();
                Mouse.GetPosition(root).Should().Be(position);
                Mouse.Synchronize();
                Mouse.GetPosition(root).Should().Be(position);
                Mouse.Capture(null);
                Mouse.GetPosition(root).Should().Be(position);
            }
            finally { Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCancelOwnedStateWithNativeMetadataEvenWhenModalBlocked()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            var root = new HitTestElement();
            first.RootVisual = root; first.SetClientSize(200, 100);
            second.RootVisual = new HitTestElement(); second.SetClientSize(200, 100);
            SendPointerButton(first, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            SendPointerButton(second, PortableInputEventKind.MouseDown, PortableMouseButton.Right);
            Mouse.Capture(root).Should().BeTrue();
            var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10.25, 12.75, 3.75025, -1, 0,
                PortablePointerModifiers.Shift);
            int lost = 0, ups = 0, moves = 0;
            root.MouseUp += (_, _) => ++ups;
            root.MouseMove += (_, _) => ++moves;
            root.LostMouseCapture += (_, args) =>
            {
                ++lost;
                Mouse.Captured.Should().BeNull(); root.IsMouseCaptured.Should().BeFalse();
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                Mouse.RightButton.Should().Be(MouseButtonState.Pressed);
                args.Timestamp.Should().Be(3750);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(cancel);
                Keyboard.Modifiers.Should().Be(ModifierKeys.Control);
            };
            using (PortableModalInputScope.Enter(new object()))
            {
                PortableWindowActivationService.IsModalInputAllowed(root).Should().BeFalse();
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)first,
                    cancel, PortableInputModifiers.Control, out bool handled).Should().BeTrue();
                handled.Should().BeTrue();
            }
            lost.Should().Be(1); ups.Should().Be(0); moves.Should().Be(0);
            Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            // Repeated cancellation is harmless and does not invent another loss.
            PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)first,
                cancel, 0, out _).Should().BeTrue();
            lost.Should().Be(1);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCancelPreservesAnotherProvidersCaptureAndLaterPress()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            var firstRoot = new HitTestElement(); var secondRoot = new HitTestElement();
            first.RootVisual = firstRoot; first.SetClientSize(200, 100);
            second.RootVisual = secondRoot; second.SetClientSize(200, 100);
            Mouse.Capture(firstRoot, CaptureMode.SubTree).Should().BeTrue();
            SendPointerButton(first, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            Mouse.Capture(secondRoot, CaptureMode.SubTree).Should().BeTrue();
            SendPointerButton(second, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            // Leave the stale provider active; activity must not imply capture ownership.
            PortableWindowActivationService.ProcessInput((PresentationSource)first,
                new PortableInputEventArgs(PortableInputEventKind.MouseMove, x: 10, y: 10));
            int lost = 0;
            secondRoot.LostMouseCapture += (_, _) => ++lost;
            try
            {
                var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10, 10, 2, -1, 0, 0);
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)first,
                    cancel, 0, out _).Should().BeTrue();
                Mouse.Captured.Should().BeSameAs(secondRoot);
                Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                first.Dispose();
                Mouse.Captured.Should().BeSameAs(secondRoot);
                lost.Should().Be(0);
            }
            finally { Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCancelRetainsReentrantSameProviderCaptureAndNewPress()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            SendPointerButton(host, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            Mouse.Capture(root).Should().BeTrue();
            var replacement = new PortablePointerInput(PortablePointerEventKind.Down, 20.25, 12.75, 4, 0, 1, 0);
            int got = 0, lost = 0, downs = 0;
            root.GotMouseCapture += (_, _) => ++got;
            root.MouseDown += (_, args) =>
            {
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(replacement);
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift);
                ++downs;
            };
            MouseEventHandler recapture = (sender, args) =>
            {
                ++lost;
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                Mouse.Capture(root).Should().BeTrue();
                PortableWindowActivationService.TryProcessNativePointerInput(source, replacement,
                    PortableInputModifiers.Shift, out _).Should().BeTrue();
                Keyboard.Modifiers.Should().Be(ModifierKeys.Control);
            };
            root.LostMouseCapture += recapture;
            try
            {
                var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10, 10, 3, -1, 0, 0);
                PortableWindowActivationService.TryProcessNativePointerInput(source, cancel,
                    PortableInputModifiers.Control, out _).Should().BeTrue();
                Mouse.Captured.Should().BeSameAs(root); root.IsMouseCaptured.Should().BeTrue();
                Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                lost.Should().Be(1); got.Should().Be(1); downs.Should().Be(1);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            }
            finally { root.LostMouseCapture -= recapture; Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCancelRetiresStateBeforeThrowingLostCaptureCallback()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            SendPointerButton(host, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            Mouse.Capture(root).Should().BeTrue();
            var failure = new InvalidOperationException("Capture callback failed.");
            MouseEventHandler fail = (_, _) => throw failure;
            root.LostMouseCapture += fail;
            try
            {
                var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10, 10, 3, -1, 0, 0);
                Action deliver = () => PortableWindowActivationService.TryProcessNativePointerInput(
                    (PresentationSource)host, cancel, PortableInputModifiers.Control, out _);
                deliver.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                Mouse.Captured.Should().BeNull(); root.IsMouseCaptured.Should().BeFalse();
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            }
            finally { root.LostMouseCapture -= fail; Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCancelCannotBeSuppressedByOrdinaryInputFiltering()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            SendPointerButton(host, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            Mouse.Capture(root).Should().BeTrue();
            int filtered = 0;
            PreProcessInputEventHandler filter = (_, args) => { ++filtered; args.Cancel(); };
            InputManager.Current.PreProcessInput += filter;
            try
            {
                var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10, 10, 3, -1, 0, 0);
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)host,
                    cancel, 0, out _).Should().BeTrue();
                filtered.Should().BeGreaterThan(0);
                Mouse.Captured.Should().BeNull(); root.IsMouseCaptured.Should().BeFalse();
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            }
            finally { InputManager.Current.PreProcessInput -= filter; Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCaptureWithinFailureStillPublishesAncestorAndDirectState()
    {
        RunInUiApartment(() =>
        {
            using var tree = new CapturePropertyTree();
            SendPointerButton(tree.Host, PortableInputEventKind.MouseDown, PortableMouseButton.Left);
            Mouse.Capture(tree.First).Should().BeTrue();
            var failure = new InvalidOperationException("Capture-within callback failed.");
            var packet = new PortablePointerInput(PortablePointerEventKind.Cancel, 10, 10, 3.25, -1, 0, 0);
            int ancestors = 0, lost = 0;
            DependencyPropertyChangedEventHandler fail = (_, args) => { if (!(bool)args.NewValue) throw failure; };
            DependencyPropertyChangedEventHandler parent = (_, args) => { if (!(bool)args.NewValue) ++ancestors; };
            MouseEventHandler loss = (_, args) =>
            {
                ++lost;
                args.Timestamp.Should().Be(3250);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(packet);
                Keyboard.Modifiers.Should().Be(ModifierKeys.Control);
                AssertCaptureProperties(tree.First, false, false);
                AssertCaptureProperties(tree.Parent, false, false);
            };
            tree.First.IsMouseCaptureWithinChanged += fail;
            tree.Parent.IsMouseCaptureWithinChanged += parent;
            tree.First.LostMouseCapture += loss;
            try
            {
                Action cancel = () => PortableWindowActivationService.TryProcessNativePointerInput(
                    (PresentationSource)tree.Host, packet, PortableInputModifiers.Control, out _);
                cancel.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                Mouse.Captured.Should().BeNull(); Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                AssertCaptureProperties(tree.First, false, false);
                AssertCaptureProperties(tree.Parent, false, false);
                ancestors.Should().Be(1); lost.Should().Be(1);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            }
            finally
            {
                tree.First.IsMouseCaptureWithinChanged -= fail;
                tree.Parent.IsMouseCaptureWithinChanged -= parent;
                tree.First.LostMouseCapture -= loss;
            }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsDirectCaptureFailureStillDeliversLoss()
    {
        RunInUiApartment(() =>
        {
            using var tree = new CapturePropertyTree();
            Mouse.Capture(tree.First).Should().BeTrue();
            var failure = new InvalidOperationException("Direct capture callback failed.");
            int lost = 0;
            DependencyPropertyChangedEventHandler fail = (_, args) => { if (!(bool)args.NewValue) throw failure; };
            MouseEventHandler loss = (_, _) => ++lost;
            tree.First.IsMouseCapturedChanged += fail;
            tree.First.LostMouseCapture += loss;
            try
            {
                Action cancel = () => CancelCaptureTree(tree);
                cancel.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                Mouse.Captured.Should().BeNull(); lost.Should().Be(1);
                AssertCaptureProperties(tree.First, false, false);
                AssertCaptureProperties(tree.Parent, false, false);
            }
            finally
            {
                tree.First.IsMouseCapturedChanged -= fail;
                tree.First.LostMouseCapture -= loss;
            }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsPropertyRecaptureSurvivesTheRetiredCallbackFailure()
    {
        RunInUiApartment(() =>
        {
            foreach (bool sameElement in new[] { false, true })
            {
                using var tree = new CapturePropertyTree();
                Mouse.Capture(tree.First).Should().BeTrue();
                UIElement target = sameElement ? tree.First : tree.Second;
                var failure = new InvalidOperationException("Retired capture-within callback failed.");
                int got = 0, lost = 0;
                bool recaptured = false;
                MouseEventHandler gain = (_, _) => ++got;
                MouseEventHandler loss = (_, _) => ++lost;
                DependencyPropertyChangedEventHandler recapture = (_, args) =>
                {
                    if ((bool)args.NewValue || recaptured) return;
                    recaptured = true;
                    Mouse.Capture(target).Should().BeTrue();
                    throw failure;
                };
                target.GotMouseCapture += gain;
                tree.First.LostMouseCapture += loss;
                tree.First.IsMouseCaptureWithinChanged += recapture;
                try
                {
                    Action cancel = () => CancelCaptureTree(tree);
                    cancel.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                    Mouse.Captured.Should().BeSameAs(target);
                    AssertCaptureProperties(tree.First, sameElement, sameElement);
                    AssertCaptureProperties(target, true, true);
                    AssertCaptureProperties(tree.Parent, false, true);
                    got.Should().Be(1); lost.Should().Be(sameElement ? 0 : 1);
                }
                finally
                {
                    target.GotMouseCapture -= gain;
                    tree.First.LostMouseCapture -= loss;
                    tree.First.IsMouseCaptureWithinChanged -= recapture;
                }
            }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCaptureCleanupRetainsEveryCallbackFailure()
    {
        RunInUiApartment(() =>
        {
            using var tree = new CapturePropertyTree();
            Mouse.Capture(tree.First).Should().BeTrue();
            var withinFailure = new InvalidOperationException("Child capture-within failed.");
            var parentFailure = new InvalidOperationException("Parent capture-within failed.");
            var directFailure = new InvalidOperationException("Direct capture failed.");
            var lostFailure = new InvalidOperationException("Capture loss failed.");
            DependencyPropertyChangedEventHandler within = (_, _) => throw withinFailure;
            DependencyPropertyChangedEventHandler parent = (_, _) => throw parentFailure;
            DependencyPropertyChangedEventHandler direct = (_, _) => throw directFailure;
            MouseEventHandler lost = (_, _) => throw lostFailure;
            tree.First.IsMouseCaptureWithinChanged += within;
            tree.Parent.IsMouseCaptureWithinChanged += parent;
            tree.First.IsMouseCapturedChanged += direct;
            tree.First.LostMouseCapture += lost;
            try
            {
                Action cancel = () => CancelCaptureTree(tree);
                var failures = cancel.Should().Throw<AggregateException>().Which.Flatten().InnerExceptions;
                failures.Should().HaveCount(4);
                failures.Should().Contain(withinFailure).And.Contain(parentFailure).And.Contain(directFailure).And.Contain(lostFailure);
                Mouse.Captured.Should().BeNull();
                AssertCaptureProperties(tree.First, false, false);
                AssertCaptureProperties(tree.Parent, false, false);
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
            }
            finally
            {
                tree.First.IsMouseCaptureWithinChanged -= within;
                tree.Parent.IsMouseCaptureWithinChanged -= parent;
                tree.First.IsMouseCapturedChanged -= direct;
                tree.First.LostMouseCapture -= lost;
            }
            CancelCaptureTree(tree); // A late cancel cannot replay a retired loss.
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCaptureAcquisitionFinishesBeforePropertyFailureEscapes()
    {
        RunInUiApartment(() =>
        {
            foreach (bool withinProperty in new[] { false, true })
            {
                using var tree = new CapturePropertyTree();
                var failure = new InvalidOperationException("Capture acquisition property failed.");
                DependencyPropertyChangedEventHandler fail = (_, args) => { if ((bool)args.NewValue) throw failure; };
                int got = 0;
                tree.First.GotMouseCapture += (_, _) => ++got;
                if (withinProperty) tree.First.IsMouseCaptureWithinChanged += fail;
                else tree.First.IsMouseCapturedChanged += fail;
                try
                {
                    Action acquire = () => Mouse.Capture(tree.First);
                    acquire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                    Mouse.Captured.Should().BeSameAs(tree.First); got.Should().Be(1);
                    AssertCaptureProperties(tree.First, true, true);
                    AssertCaptureProperties(tree.Parent, false, true);
                }
                finally
                {
                    tree.First.IsMouseCaptureWithinChanged -= fail;
                    tree.First.IsMouseCapturedChanged -= fail;
                }
            }
        });
    }

    private static void AssertCaptureProperties(UIElement element, bool captured, bool within)
    {
        element.IsMouseCaptured.Should().Be(captured);
        element.IsMouseCaptureWithin.Should().Be(within);
        element.GetValue(UIElement.IsMouseCaptureWithinProperty).Should().Be(within);
    }

    private static void CancelCaptureTree(CapturePropertyTree tree) =>
        PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)tree.Host,
            new(PortablePointerEventKind.Cancel, 10, 10, 3, -1, 0, 0), PortableInputModifiers.Control, out _).Should().BeTrue();

    private sealed class CapturePropertyTree : IDisposable
    {
        internal IPortablePresentationSourceHost Host { get; } = PortablePresentationSourceHost.Create();
        internal System.Windows.Controls.Grid Parent { get; } = new();
        internal HitTestElement First { get; } = new();
        internal HitTestElement Second { get; } = new();
        internal CapturePropertyTree()
        {
            Parent.Children.Add(First); Parent.Children.Add(Second);
            Host.RootVisual = Parent; Host.SetClientSize(200, 100);
        }
        public void Dispose()
        {
            try { Mouse.Capture(null); }
            finally { Host.Dispose(); }
        }
    }

    [PortableInputFact]
    public void NativePointerReportsOriginCancelRetiresCapturedDownWithoutReleasingOtherCapture()
    {
        RunInUiApartment(() =>
        {
            using var origin = PortablePresentationSourceHost.Create();
            using var target = PortablePresentationSourceHost.Create();
            origin.RootVisual = new HitTestElement(); origin.SetClientSize(200, 100);
            var root = new HitTestElement(); target.RootVisual = root; target.SetClientSize(200, 100);
            Mouse.Capture(root).Should().BeTrue();
            int downs = 0, cancellations = 0;
            root.MouseDown += (_, _) => ++downs;
            root.PreviewMouseMove += (sender, args) =>
            {
                if (PortableWindowActivationService.GetNativePointerInput(args) == null) return;
                var cancel = new PortablePointerInput(PortablePointerEventKind.Cancel, 10, 10, 3, -1, 0, 0);
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)origin,
                    cancel, 0, out _).Should().BeTrue();
                ++cancellations;
            };
            try
            {
                var down = new PortablePointerInput(PortablePointerEventKind.Down, 20.25, 12.75, 2, 0, 1, 0);
                PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)origin,
                    down, 0, out _).Should().BeTrue();
                cancellations.Should().Be(1); downs.Should().Be(0);
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
                Mouse.Captured.Should().BeSameAs(root);
            }
            finally { Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void WheelEventStateSurvivesNestedPreviewDelivery() =>
        VerifyNestedWheelEventState(handleNested: false, throwNested: false);

    [PortableInputFact]
    public void WheelEventStateSurvivesHandledNestedPreviewDelivery() =>
        VerifyNestedWheelEventState(handleNested: true, throwNested: false);

    [PortableInputFact]
    public void WheelEventStateSurvivesThrowingNestedPreviewDelivery() =>
        VerifyNestedWheelEventState(handleNested: false, throwNested: true);

    private static void VerifyNestedWheelEventState(bool handleNested, bool throwNested)
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement();
            host.RootVisual = root;
            host.SetClientSize(200, 100);
            bool nested = false;
            int nestedPreviews = 0;
            var bubbles = new List<int>();
            MouseWheelEventArgs? outer = null;
            void Send(double delta) => PortableWindowActivationService.ProcessInput(source,
                new PortableInputEventArgs(PortableInputEventKind.MouseWheel, x: 10, y: 10, deltaY: delta));

            root.PreviewMouseWheel += (_, args) =>
            {
                if (nested)
                {
                    ++nestedPreviews;
                    args.Delta.Should().Be(-240);
                    if (throwNested) throw new InvalidOperationException("Nested wheel callback failed.");
                    args.Handled = handleNested;
                    return;
                }

                outer = args;
                args.Delta.Should().Be(120);
                nested = true;
                try
                {
                    if (throwNested)
                    {
                        Action dispatch = () => Send(-2);
                        dispatch.Should().Throw<InvalidOperationException>().WithMessage("Nested wheel callback failed.");
                    }
                    else Send(-2);
                }
                finally { nested = false; }
                args.Delta.Should().Be(120);
                args.Handled.Should().BeFalse();
            };
            root.MouseWheel += (_, args) => bubbles.Add(args.Delta);

            Send(1);
            nestedPreviews.Should().Be(1);
            outer.Should().NotBeNull();
            outer!.Delta.Should().Be(120);
            bubbles.Should().Equal(handleNested || throwNested ? new[] { 120 } : new[] { -240, 120 });
        });
    }

    [PortableInputFact]
    public void NativePointerReportsEnterLeaveRetainNativeMetadataWithoutSyntheticMotion()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            var enter = new PortablePointerInput(PortablePointerEventKind.Enter, 10.25, 12.75, 1.25, -1, 0, 0);
            var leave = new PortablePointerInput(PortablePointerEventKind.Leave, 210.125, 12.75, 2.5, -1, 0, 0);
            int enters = 0, leaves = 0, moves = 0;
            root.MouseEnter += (_, args) =>
            {
                ++enters;
                args.Timestamp.Should().Be(1250);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(enter);
                args.GetPosition(root).Should().Be(new Point(10.25, 12.75));
                Keyboard.Modifiers.Should().Be(ModifierKeys.Control);
            };
            root.MouseLeave += (_, args) =>
            {
                ++leaves;
                args.Timestamp.Should().Be(2500);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(leave);
                args.GetPosition(root).Should().Be(new Point(210.125, 12.75));
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift);
            };
            root.MouseMove += (_, _) => ++moves;
            DeliverNativePointer(host, enter, PortableInputModifiers.Control);
            root.IsMouseOver.Should().BeTrue(); root.IsMouseDirectlyOver.Should().BeTrue();
            int movesBeforeLeave = moves;
            DeliverNativePointer(host, leave, PortableInputModifiers.Shift);
            Mouse.DirectlyOver.Should().BeNull(); root.IsMouseOver.Should().BeFalse();
            root.IsMouseDirectlyOver.Should().BeFalse();
            Mouse.Synchronize();
            DeliverNativePointer(host, leave, PortableInputModifiers.Shift);
            moves.Should().Be(movesBeforeLeave); leaves.Should().Be(1);
            Mouse.DirectlyOver.Should().BeNull();
            DeliverNativePointer(host, enter, PortableInputModifiers.Control);
            enters.Should().Be(2); root.IsMouseOver.Should().BeTrue();
            Keyboard.Modifiers.Should().Be(ModifierKeys.None);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsPopupLeavePreservesElementAndSubtreeOwnerCapture()
    {
        RunInUiApartment(() =>
        {
            foreach (var mode in new[] { CaptureMode.Element, CaptureMode.SubTree })
            {
                using var owner = PortablePresentationSourceHost.Create();
                using var popup = PortablePresentationSourceHost.Create();
                var ownerRoot = new HitTestElement(); var popupRoot = new HitTestElement(ownerRoot);
                owner.RootVisual = ownerRoot; owner.SetClientSize(200, 100);
                popup.RootVisual = popupRoot; popup.SetClientSize(200, 100);
                owner.SetClientOrigin(100, 200); popup.SetClientOrigin(400, 500);
                DeliverNativePointer(owner, new(PortablePointerEventKind.Down, 10, 10, 1, 0, 1, 0));
                Mouse.Capture(ownerRoot, mode).Should().BeTrue();
                try
                {
                    DeliverNativePointer(popup, new(PortablePointerEventKind.Enter, 20.25, 12.75, 2, -1, 0, 0));
                    int lost = 0, ups = 0;
                    ownerRoot.LostMouseCapture += (_, _) => ++lost;
                    ownerRoot.MouseUp += (_, _) => ++ups;
                    DeliverNativePointer(popup, new(PortablePointerEventKind.Leave, 210.25, 12.75, 3, -1, 0, 0));
                    Mouse.Captured.Should().BeSameAs(ownerRoot);
                    Mouse.DirectlyOver.Should().BeSameAs(ownerRoot);
                    Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                    Mouse.GetPosition(ownerRoot).Should().Be(new Point(510.25, 312.75));
                    Mouse.Synchronize();
                    Mouse.DirectlyOver.Should().BeSameAs(ownerRoot);
                    lost.Should().Be(0); ups.Should().Be(0);
                }
                finally { Mouse.Capture(null); }
            }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsLateLeaveCannotRetireAnotherSourcesHover()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            first.RootVisual = new HitTestElement(); first.SetClientSize(200, 100);
            var root = new HitTestElement(); second.RootVisual = root; second.SetClientSize(200, 100);
            DeliverNativePointer(first, new(PortablePointerEventKind.Enter, 10, 10, 1, -1, 0, 0));
            DeliverNativePointer(second, new(PortablePointerEventKind.Enter, 20.25, 12.75, 2, -1, 0, 0));
            int leaves = 0;
            root.MouseLeave += (_, _) => ++leaves;
            DeliverNativePointer(first, new(PortablePointerEventKind.Leave, 210, 10, 3, -1, 0, 0));
            Mouse.DirectlyOver.Should().BeSameAs(root); root.IsMouseDirectlyOver.Should().BeTrue();
            Mouse.GetPosition(root).Should().Be(new Point(20.25, 12.75));
            leaves.Should().Be(0);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsNestedEnterDuringLeaveOwnsFinalHover()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            var oldRoot = new HitTestElement(); var newRoot = new HitTestElement();
            first.RootVisual = oldRoot; first.SetClientSize(200, 100);
            second.RootVisual = newRoot; second.SetClientSize(200, 100);
            var leave = new PortablePointerInput(PortablePointerEventKind.Leave, 210, 10, 2, -1, 0, 0);
            var enter = new PortablePointerInput(PortablePointerEventKind.Enter, 30.25, 14.75, 3, -1, 0, 0);
            int enters = 0;
            newRoot.MouseEnter += (_, args) =>
            {
                ++enters;
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(enter);
                Keyboard.Modifiers.Should().Be(ModifierKeys.Control);
            };
            oldRoot.MouseLeave += (_, args) =>
            {
                DeliverNativePointer(second, enter, PortableInputModifiers.Control);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(leave);
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift);
            };
            DeliverNativePointer(first, new(PortablePointerEventKind.Enter, 10, 10, 1, -1, 0, 0));
            DeliverNativePointer(first, leave, PortableInputModifiers.Shift);
            enters.Should().Be(1); Mouse.DirectlyOver.Should().BeSameAs(newRoot);
            newRoot.IsMouseDirectlyOver.Should().BeTrue(); oldRoot.IsMouseDirectlyOver.Should().BeFalse();
            Mouse.GetPosition(newRoot).Should().Be(new Point(30.25, 14.75));
        });
    }

    [PortableInputFact]
    public void NativePointerReportsNestedLeaveDuringEnterSuppressesRetiredMotion()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            int moves = 0, leaves = 0;
            var enter = new PortablePointerInput(PortablePointerEventKind.Enter, 10, 10, 1, -1, 0, 0);
            var leave = new PortablePointerInput(PortablePointerEventKind.Leave, 210, 10, 2, -1, 0, 0);
            root.MouseMove += (_, _) => ++moves;
            root.MouseLeave += (_, args) =>
            {
                ++leaves;
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(leave);
            };
            root.MouseEnter += (_, args) =>
            {
                DeliverNativePointer(host, leave);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(enter);
            };
            DeliverNativePointer(host, enter);
            Mouse.Synchronize();
            leaves.Should().Be(1); moves.Should().Be(0);
            Mouse.DirectlyOver.Should().BeNull(); root.IsMouseOver.Should().BeFalse();
            root.IsMouseDirectlyOver.Should().BeFalse();
        });
    }

    [PortableInputFact]
    public void NativePointerReportsThrowingLeaveStillRetiresDirectHoverAndRestoresScope()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            DeliverNativePointer(host, new(PortablePointerEventKind.Enter, 10, 10, 1, -1, 0, 0));
            var failure = new InvalidOperationException("Leave callback failed.");
            MouseEventHandler fail = (_, _) => throw failure;
            root.MouseLeave += fail;
            try
            {
                Action leave = () => DeliverNativePointer(host,
                    new(PortablePointerEventKind.Leave, 210, 10, 2, -1, 0, 0), PortableInputModifiers.Shift);
                leave.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                Mouse.DirectlyOver.Should().BeNull(); root.IsMouseDirectlyOver.Should().BeFalse();
                Keyboard.Modifiers.Should().Be(ModifierKeys.None);
                Mouse.Synchronize(); root.IsMouseOver.Should().BeFalse();
            }
            finally { root.MouseLeave -= fail; }
            var current = new PortablePointerInput(PortablePointerEventKind.Enter, 20, 10, 3, -1, 0, 0);
            root.MouseEnter += (_, args) =>
            {
                args.Timestamp.Should().Be(3000);
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeSameAs(current);
            };
            DeliverNativePointer(host, current);
            Mouse.DirectlyOver.Should().BeSameAs(root);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCancelRetiresHoverAndPreservesBothCallbackFailures()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            DeliverNativePointer(host, new(PortablePointerEventKind.Down, 10, 10, 1, 0, 1, 0));
            Mouse.Capture(root).Should().BeTrue();
            var lostFailure = new InvalidOperationException("Lost capture failed.");
            var leaveFailure = new InvalidOperationException("Leave failed.");
            MouseEventHandler lose = (_, _) => throw lostFailure;
            MouseEventHandler leave = (_, _) => throw leaveFailure;
            root.LostMouseCapture += lose; root.MouseLeave += leave;
            try
            {
                Action cancel = () => DeliverNativePointer(host,
                    new(PortablePointerEventKind.Cancel, 10, 10, 2, -1, 0, 0));
                var failure = cancel.Should().Throw<AggregateException>().Which;
                failure.InnerExceptions.Should().Equal(lostFailure, leaveFailure);
                Mouse.Captured.Should().BeNull(); Mouse.DirectlyOver.Should().BeNull();
                root.IsMouseCaptured.Should().BeFalse(); root.IsMouseDirectlyOver.Should().BeFalse();
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            }
            finally { root.LostMouseCapture -= lose; root.MouseLeave -= leave; Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void NativePointerReportsCaptureOutsideUsesLogicalHoverWithoutPhysicalMotion()
    {
        RunInUiApartment(() =>
        {
            using var host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement(); host.RootVisual = root; host.SetClientSize(200, 100);
            DeliverNativePointer(host, new(PortablePointerEventKind.Enter, 10, 10, 1, -1, 0, 0));
            DeliverNativePointer(host, new(PortablePointerEventKind.Leave, 210, 10, 2, -1, 0, 0));
            int moves = 0, enters = 0;
            root.MouseMove += (_, _) => ++moves;
            root.MouseEnter += (_, args) =>
            {
                PortableWindowActivationService.GetNativePointerInput(args).Should().BeNull();
                ++enters;
            };
            try
            {
                Mouse.Capture(root).Should().BeTrue();
                Mouse.DirectlyOver.Should().BeSameAs(root);
                Mouse.Capture(null);
                Mouse.DirectlyOver.Should().BeNull();
                enters.Should().Be(1); moves.Should().Be(0);
            }
            finally { Mouse.Capture(null); }
        });
    }

    private static void DeliverNativePointer(IPortablePresentationSourceHost host, PortablePointerInput input,
        PortableInputModifiers modifiers = PortableInputModifiers.None) =>
        PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)host, input,
            modifiers, out _).Should().BeTrue();

    private static void RunInUiApartment(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
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
            throw new TimeoutException("Portable WPF input test did not complete within 30 seconds.");
        }

        if (exception != null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static void ProcessInput(
        IPortableWindowActivationServiceRegistrar activationService,
        PresentationSource source,
        int kind,
        double x,
        double y,
        int button = 0)
    {
        activationService.TryProcessPresentationSourceInputEvent(
            source,
            new PortableWindowInputEvent(kind, x: x, y: y, button: button)).Should().BeTrue();
    }

    private sealed class HitTestElement : UIElement
    {
        private readonly DependencyObject? _uiParent;

        public HitTestElement(DependencyObject? uiParent = null)
        {
            _uiParent = uiParent;
        }

        protected override DependencyObject GetUIParentCore()
        {
            return _uiParent ?? base.GetUIParentCore();
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }
    }
}
