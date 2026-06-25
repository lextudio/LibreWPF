// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading;

namespace System.Windows
{
    /// <summary>
    /// Presentation source for non-HWND hosts.
    /// </summary>
    internal sealed class PortablePresentationSource : PresentationSource, IDisposable
    {
        private readonly PortableCompositionTarget _compositionTarget;
        private readonly PortableKeyboardInputProvider _keyboardInputProvider;
        private readonly PortableMouseInputProvider _mouseInputProvider;
        private readonly HwndSource _portableHwndSource;
        private readonly IntPtr _handle;
        private Visual _rootVisual;
        private Size _clientSize;
        private bool _hasClientSize;
        private bool _contentRenderedQueued;
        private bool _isDisposed;
        private static long s_nextPortableHandle = 0x505750460000;

        internal PortablePresentationSource()
            : this(1.0, 1.0)
        {
        }

        internal PortablePresentationSource(double dpiScaleX, double dpiScaleY)
        {
            _handle = new IntPtr(Interlocked.Increment(ref s_nextPortableHandle));
            _compositionTarget = new PortableCompositionTarget(dpiScaleX, dpiScaleY);
            _portableHwndSource = HwndSource.CreatePortable(this, _handle, dpiScaleX, dpiScaleY);
            _keyboardInputProvider = new PortableKeyboardInputProvider(this);
            _mouseInputProvider = new PortableMouseInputProvider(this);
            AddSource();
        }

        internal event EventHandler RenderRequested;

        internal event EventHandler Disposed;

        internal IntPtr Handle
        {
            get { return _isDisposed ? IntPtr.Zero : _handle; }
        }

        internal HwndSource HwndSource
        {
            get { return _isDisposed ? null : _portableHwndSource; }
        }

        public override bool IsDisposed
        {
            get { return _isDisposed; }
        }

        public override Visual RootVisual
        {
            get
            {
                if (_isDisposed)
                {
                    return null;
                }

                return _rootVisual;
            }
            set
            {
                VerifyNotDisposed();
                SetRootVisual(value);
            }
        }

        internal PortableCompositionTarget PortableCompositionTarget
        {
            get { return _compositionTarget; }
        }

        internal void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            VerifyNotDisposed();
            _compositionTarget.SetDeviceScale(dpiScaleX, dpiScaleY);
            RequestRender();
        }

        internal void SetClientSize(double width, double height)
        {
            VerifyNotDisposed();

            Size clientSize = new Size(
                ToPositiveFiniteClientSize(width),
                ToPositiveFiniteClientSize(height));
            if (_hasClientSize &&
                _clientSize.Width == clientSize.Width &&
                _clientSize.Height == clientSize.Height)
            {
                return;
            }

            _clientSize = clientSize;
            _hasClientSize = true;
            ApplyRootVisualLayout();
            RequestRender();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                VerifyAccess();
                SetRootVisual(null);
                RemoveSource();
                _portableHwndSource.Dispose();
                _mouseInputProvider.Dispose();
                _keyboardInputProvider.Dispose();
                _compositionTarget.Dispose();
                ClearContentRenderedListeners();
                Disposed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                RenderRequested = null;
                Disposed = null;
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        protected override CompositionTarget GetCompositionTargetCore()
        {
            return _isDisposed ? null : _compositionTarget;
        }

        internal override IInputProvider GetInputProvider(Type inputDevice)
        {
            if (inputDevice == typeof(MouseDevice))
            {
                return _mouseInputProvider;
            }

            if (inputDevice == typeof(KeyboardDevice))
            {
                return _keyboardInputProvider;
            }

            return null;
        }

        private void SetRootVisual(Visual rootVisual)
        {
            if (_rootVisual == rootVisual)
            {
                return;
            }

            Visual oldRootVisual = _rootVisual;
            if (oldRootVisual is UIElement oldRootUIElement)
            {
                oldRootUIElement.LayoutUpdated -= OnLayoutUpdated;
            }

            if (rootVisual != null)
            {
                _rootVisual = rootVisual;
                if (rootVisual is UIElement newRootUIElement)
                {
                    newRootUIElement.LayoutUpdated += OnLayoutUpdated;
                }

                _compositionTarget.RootVisual = rootVisual;
                UIElement.PropagateResumeLayout(null, rootVisual);
                ApplyRootVisualLayout();
            }
            else
            {
                _rootVisual = null;
                _compositionTarget.RootVisual = null;
            }

            if (oldRootVisual != null)
            {
                UIElement.PropagateSuspendLayout(oldRootVisual);
            }

            RootChanged(oldRootVisual, _rootVisual);
            _keyboardInputProvider.OnRootChanged(oldRootVisual, _rootVisual);
            QueueContentRendered();
            RequestRender();
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            QueueContentRendered();
            RequestRender();
        }

        private void ApplyRootVisualLayout()
        {
            if (!_hasClientSize || _rootVisual is not UIElement rootUIElement)
            {
                return;
            }

            rootUIElement.InvalidateMeasure();
            rootUIElement.Measure(_clientSize);
            rootUIElement.Arrange(new Rect(new Point(), _clientSize));
            rootUIElement.UpdateLayout();
        }

        private static double ToPositiveFiniteClientSize(double value)
        {
            return double.IsFinite(value) && value > 0.0 ? value : 1.0;
        }

        private void QueueContentRendered()
        {
            if (_rootVisual == null || _contentRenderedQueued || _isDisposed)
            {
                return;
            }

            _contentRenderedQueued = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new DispatcherOperationCallback(FireContentRenderedCallback),
                this);
        }

        private object FireContentRenderedCallback(object arg)
        {
            if (_isDisposed)
            {
                return null;
            }

            _contentRenderedQueued = false;
            return FireContentRendered(arg);
        }

        private void RequestRender()
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        private void VerifyNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            VerifyAccess();
        }

        private bool HasRootVisual
        {
            get { return !_isDisposed && _rootVisual != null; }
        }

        private bool ProvidesInputForRootVisual(Visual visual)
        {
            return !_isDisposed && _rootVisual == visual;
        }

        private sealed class PortableKeyboardInputProvider : IKeyboardInputProvider, IDisposable
        {
            private readonly PortablePresentationSource _source;
            private InputProviderSite _site;
            private bool _active;

            internal PortableKeyboardInputProvider(PortablePresentationSource source)
            {
                _source = source;
                _site = InputManager.Current.RegisterInputProvider(this);
            }

            public void Dispose()
            {
                _active = false;
                _site?.Dispose();
                _site = null;
            }

            internal void OnRootChanged(Visual oldRoot, Visual newRoot)
            {
                if (_active && newRoot != null)
                {
                    Keyboard.Focus(null);
                }
            }

            bool IInputProvider.ProvidesInputForRootVisual(Visual v)
            {
                return _source.ProvidesInputForRootVisual(v);
            }

            void IInputProvider.NotifyDeactivate()
            {
                _active = false;
            }

            bool IKeyboardInputProvider.AcquireFocus(bool checkOnly)
            {
                bool acquired = _source.HasRootVisual;
                if (acquired && !checkOnly)
                {
                    _active = true;
                }

                return acquired;
            }
        }

        private sealed class PortableMouseInputProvider : IMouseInputProvider, IDisposable
        {
            private readonly PortablePresentationSource _source;
            private InputProviderSite _site;
            private bool _haveCapture;

            internal PortableMouseInputProvider(PortablePresentationSource source)
            {
                _source = source;
                _site = InputManager.Current.RegisterInputProvider(this);
            }

            public void Dispose()
            {
                ReleaseMouseCapture(reportInput: true);
                _site?.Dispose();
                _site = null;
            }

            bool IInputProvider.ProvidesInputForRootVisual(Visual v)
            {
                return _source.ProvidesInputForRootVisual(v);
            }

            void IInputProvider.NotifyDeactivate()
            {
                ReleaseMouseCapture(reportInput: true);
            }

            bool IMouseInputProvider.SetCursor(Cursor cursor)
            {
                return _source.HasRootVisual;
            }

            bool IMouseInputProvider.CaptureMouse()
            {
                if (!_source.HasRootVisual)
                {
                    return false;
                }

                _haveCapture = true;
                return true;
            }

            void IMouseInputProvider.ReleaseMouseCapture()
            {
                ReleaseMouseCapture(reportInput: true);
            }

            int IMouseInputProvider.GetIntermediatePoints(IInputElement relativeTo, Point[] points)
            {
                return -1;
            }

            private void ReleaseMouseCapture(bool reportInput)
            {
                if (!_haveCapture)
                {
                    return;
                }

                _haveCapture = false;

                if (reportInput && _site != null && !_site.IsDisposed)
                {
                    RawMouseInputReport report = new RawMouseInputReport(
                        InputMode.Foreground,
                        Environment.TickCount,
                        _source,
                        RawMouseActions.Activate | RawMouseActions.CancelCapture,
                        0,
                        0,
                        0,
                        IntPtr.Zero);

                    _site.ReportInput(report);
                }
            }
        }
    }
}
