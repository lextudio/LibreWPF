// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MS.Internal;
using ProGPU.Wpf.Interop;

namespace System.Windows.Input
{
    // Retains occurrences of the actual source default handler, never a second
    // ancestor walk, input hit test or replay of application event handlers.
    internal sealed class PortableScrollRoute
    {
        private readonly WeakReference<PortablePresentationSource> _origin;
        private readonly ulong _generation;
        private readonly PortablePointerInput _input;
        private readonly PortableScrollLifetime _lifetime, _sequence;
        private readonly Matrix _originToDesktop;
        private readonly bool _validFrame;
        private readonly List<Node> _nodes = new();
        private Queue<(Node Owner, Vector Motion)> _pending;
        private bool _sealed, _abandoned, _draining;
        internal Matrix OriginToDesktop => _originToDesktop;

        internal PortableScrollRoute(PortablePresentationSource origin, PortablePointerInput input,
            PortableScrollLifetime lifetime, PortableScrollLifetime sequence)
        {
            _origin = new(origin); _generation = origin.PointerInputGeneration;
            _input = input; _lifetime = lifetime; _sequence = sequence;
            PortableDesktopTransform desktop = origin.DesktopTransform;
            if (origin.RootVisual != null && desktop.IsValid)
            {
                Matrix frame = PointUtil.GetVisualTransform(origin.RootVisual);
                frame.Scale(desktop.ScaleX, desktop.ScaleY);
                frame.Translate(desktop.OriginX, desktop.OriginY);
                _validFrame = frame.HasInverse && double.IsFinite(frame.M11) && double.IsFinite(frame.M12) &&
                    double.IsFinite(frame.M21) && double.IsFinite(frame.M22) &&
                    double.IsFinite(frame.OffsetX) && double.IsFinite(frame.OffsetY);
                _originToDesktop = frame;
            }
        }

        private bool TryGetOrigin(out PortablePresentationSource origin)
        {
            origin = null;
            return !_abandoned && (_input.ScrollUnit == PortablePointerScrollUnit.Lines || _validFrame) &&
                !_lifetime.IsCancelled && _origin.TryGetTarget(out origin) && !origin.IsDisposed &&
                origin.PointerInputGeneration == _generation &&
                PortableWindowActivationService.IsModalInputAllowed(origin.RootVisual as UIElement) &&
                !origin.IsDisposed && origin.PointerInputGeneration == _generation;
        }

        internal IPortableScrollContinuation Capture(ScrollViewer viewer, bool eligible)
        {
            if (_sealed || _abandoned) return null;
            // Reuse the source route's existing element bound. No input-specific
            // walk or unbounded retained chain is introduced.
            if (_nodes.Count > UIElement.MAX_ELEMENTS_IN_ROUTE)
                throw new InvalidOperationException("The native scroll continuation exceeds its source route.");
            var node = new Node(this, _nodes.Count, viewer, eligible);
            _nodes.Add(node);
            return node;
        }

        internal void Seal()
        {
            if (_abandoned) return;
            _sealed = true;
            Drain();
        }

        internal void Abandon()
        {
            _abandoned = true;
            _pending?.Clear();
        }

        private void Continue(Node owner, Vector motion)
        {
            if (motion == default || !owner.IsCurrent) return;
            if (!double.IsFinite(motion.X) || !double.IsFinite(motion.Y))
                throw new InvalidOperationException("Native scroll continuation motion must be finite.");
            // A completed command may be observed by reentrant layout before the
            // bubble route finishes. Publish only after its successful seal.
            _pending ??= new();
            if (_pending.Count >= 2 * (UIElement.MAX_ELEMENTS_IN_ROUTE + 1))
                throw new InvalidOperationException("The native scroll continuation queue is full.");
            _pending.Enqueue((owner, motion));
            Drain();
        }

        private void Drain()
        {
            if (!_sealed || _abandoned || _draining) return;
            _draining = true;
            try
            {
                while (_pending is { Count: > 0 })
                {
                    // Remove before any provider callback; failure must not
                    // replay already attempted motion on a later layout pass.
                    (Node owner, Vector remaining) = _pending.Dequeue();
                    if (!owner.IsCurrent || !TryGetOrigin(out var origin)) continue;
                    for (int i = owner.Index + 1; i < _nodes.Count && remaining != default; i++)
                    {
                        Node next = _nodes[i];
                        if (!next.Eligible || !next.TryGetViewer(out var viewer)) continue;
                        if (!viewer.TryGetPortableScrollSession(_sequence, out var session) || !next.IsCurrent) continue;
                        if (session.TryQueueRemaining(_input, _lifetime, remaining, origin, next,
                            out Vector unclaimed, out bool queueFull, _originToDesktop))
                            remaining = unclaimed;
                        else if (queueFull)
                            throw new InvalidOperationException("The native scroll ancestor queue is full.");
                        if (!owner.IsCurrent || !TryGetOrigin(out origin)) break;
                    }
                }
            }
            catch
            {
                // This route is the failure owner, not a newer reentrant route
                // or every packet sharing the still-live gesture lease.
                Abandon();
                throw;
            }
            finally { _draining = false; }
        }

        private sealed class Node : IPortableScrollContinuation
        {
            private readonly PortableScrollRoute _route;
            private readonly WeakReference<ScrollViewer> _viewer;
            private readonly WeakReference<PortablePresentationSource> _source;
            private readonly WeakReference<IScrollInfo> _provider;
            private readonly ulong _generation;
            internal int Index { get; }
            internal bool Eligible { get; }

            internal Node(PortableScrollRoute route, int index, ScrollViewer viewer, bool eligible)
            {
                _route = route; Index = index; Eligible = eligible;
                _viewer = new(viewer);
                if (PresentationSource.CriticalFromVisual(viewer) is PortablePresentationSource source &&
                    viewer.ScrollInfo is IScrollInfo provider)
                {
                    _source = new(source); _generation = source.PointerInputGeneration;
                    _provider = new(provider);
                }
            }

            internal bool TryGetViewer(out ScrollViewer viewer)
            {
                viewer = null;
                return _route.TryGetOrigin(out _) && _source != null && _provider != null &&
                    _source.TryGetTarget(out var source) && !source.IsDisposed &&
                    source.PointerInputGeneration == _generation && _viewer.TryGetTarget(out viewer) &&
                    _provider.TryGetTarget(out var provider) && ReferenceEquals(viewer.ScrollInfo, provider) &&
                    ReferenceEquals(PresentationSource.CriticalFromVisual(viewer), source) &&
                    viewer.IsEnabled && viewer.IsVisible;
            }

            public bool IsCurrent => TryGetViewer(out _);
            public void Continue(Vector originalSourceDelta) => _route.Continue(this, originalSourceDelta);
        }
    }
}
