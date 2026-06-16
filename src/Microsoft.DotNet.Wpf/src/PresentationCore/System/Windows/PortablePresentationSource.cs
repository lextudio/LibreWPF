// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media;
using System.Windows.Threading;

namespace System.Windows
{
    /// <summary>
    /// Presentation source for non-HWND hosts.
    /// </summary>
    internal sealed class PortablePresentationSource : PresentationSource, IDisposable
    {
        private readonly PortableCompositionTarget _compositionTarget;
        private Visual _rootVisual;
        private bool _contentRenderedQueued;
        private bool _isDisposed;

        internal PortablePresentationSource()
            : this(1.0, 1.0)
        {
        }

        internal PortablePresentationSource(double dpiScaleX, double dpiScaleY)
        {
            _compositionTarget = new PortableCompositionTarget(dpiScaleX, dpiScaleY);
            AddSource();
        }

        internal event EventHandler RenderRequested;

        internal event EventHandler Disposed;

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
            QueueContentRendered();
            RequestRender();
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            QueueContentRendered();
            RequestRender();
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
    }
}
