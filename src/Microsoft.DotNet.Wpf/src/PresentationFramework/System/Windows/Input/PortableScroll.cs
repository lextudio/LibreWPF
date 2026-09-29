// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows.Input
{
    /// <summary>Lossless source scrolling, separate from integer wheel-notch events.</summary>
    public sealed class PortableScrollEventArgs : RoutedEventArgs
    {
        private readonly PortableScroll.RouteState _state;
        private readonly ulong _revision;
        private readonly ulong _generation;
        private readonly IInputElement _target;
        internal PortableScrollLifetime Lifetime { get; }
        internal PortableScrollLifetime Sequence { get; }
        internal bool IsCancellation { get; }

        internal PortableScrollEventArgs(PortableScroll.RouteState state, IInputElement target,
            PortablePointerInput input, PortableScrollLifetime lifetime, PortableScrollLifetime sequence, bool cancellation)
        {
            _state = state; _revision = state.Revision; _generation = state.Source.PointerInputGeneration;
            _target = target; NativeInput = input; Lifetime = lifetime; IsCancellation = cancellation;
            Sequence = sequence;
            RemainingScroll = new Vector(input.ScrollX, input.ScrollY);
            Timestamp = PortableWindowActivationService.NativePointerTimestamp(input.Timestamp);
            Modifiers = Keyboard.Modifiers;
            Source = target;
        }

        public PortablePointerInput NativeInput { get; }
        /// <summary>Unconsumed motion, in the original source frame and native units/sign.</summary>
        public Vector RemainingScroll { get; private set; }
        internal bool HasConsumedMotion { get; private set; }
        internal void AcceptRemaining(Vector remaining)
        {
            HasConsumedMotion |= remaining != RemainingScroll;
            RemainingScroll = remaining;
            if (remaining == default) Handled = true;
        }
        public int Timestamp { get; }
        public ModifierKeys Modifiers { get; }
        internal PortablePresentationSource PresentationSource => _state.Source;
        internal bool IsCurrent => !_state.Source.IsDisposed && _state.Revision == _revision &&
            _state.Source.PointerInputGeneration == _generation &&
            (!Lifetime.IsCancelled || IsCancellation) && PortableScroll.BelongsTo(_target, _state.Source) &&
            PortableWindowActivationService.IsModalInputAllowed(_state.Source.RootVisual as UIElement) &&
            _state.Revision == _revision && _state.Source.PointerInputGeneration == _generation;
    }

    /// <summary>Routed point/line scrolling with original native event metadata.</summary>
    public static class PortableScroll
    {
        public static readonly RoutedEvent PreviewScrollEvent = EventManager.RegisterRoutedEvent(
            "PreviewScroll", RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(PortableScroll));
        public static readonly RoutedEvent ScrollEvent = EventManager.RegisterRoutedEvent(
            "Scroll", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PortableScroll));
        private static readonly ConditionalWeakTable<PortablePresentationSource, RouteState> s_sources = new();

        static PortableScroll() => EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollEvent,
            new RoutedEventHandler(OnScrollViewer));

        public static void AddPreviewScrollHandler(UIElement element, RoutedEventHandler handler) => element.AddHandler(PreviewScrollEvent, handler);
        public static void RemovePreviewScrollHandler(UIElement element, RoutedEventHandler handler) => element.RemoveHandler(PreviewScrollEvent, handler);
        public static void AddScrollHandler(UIElement element, RoutedEventHandler handler) => element.AddHandler(ScrollEvent, handler);
        public static void RemoveScrollHandler(UIElement element, RoutedEventHandler handler) => element.RemoveHandler(ScrollEvent, handler);

        private static void OnScrollViewer(object sender, RoutedEventArgs value)
        {
            var input = (PortableScrollEventArgs)value;
            if (!input.IsCurrent) return;
            if (input.IsCancellation) { input.Handled = true; return; }
            var viewer = (ScrollViewer)sender;
            if (viewer.TryGetPortableScrollSession(input.Sequence, out var session) &&
                ReferenceEquals(session.Source, input.PresentationSource) && input.IsCurrent)
            {
                if (session.TryQueueRemaining(input.NativeInput, input.Lifetime, input.RemainingScroll,
                    out Vector remaining, out bool queueFull)) input.AcceptRemaining(remaining);
                else if (queueFull) throw new InvalidOperationException("The native scroll source queue is full.");
            }
        }

        internal static bool TryProcess(PortablePresentationSource source, PortablePointerInput input, out bool handled)
        {
            handled = false;
            if (!TryPhase(input, out uint phase, out bool momentum)) return false;
            RouteState state = s_sources.GetValue(source, static value => new RouteState(value));
            state.Refresh();
            unchecked { ++state.Revision; }
            ulong revision = state.Revision;
            ulong generation = source.PointerInputGeneration;
            if (source.RootVisual is not UIElement root || !PortableWindowActivationService.IsModalInputAllowed(root))
            {
                state.Cancel(); handled = true; return true;
            }

            IInputElement target;
            PortableScrollLifetime lifetime;
            PortableScrollLifetime sequence;
            if (momentum)
            {
                if (phase == 1) // AppKit momentum begins at the current pointer target.
                {
                    state.Momentum?.Cancel();
                    state.Momentum = new PortableScrollLifetime();
                    // Preserve deferred fractional state across the handoff, but
                    // keep cancellation of momentum separate from accepted touch motion.
                    state.MomentumSequence = state.CompletedSequence is { IsCancelled: false } completed ? completed :
                        state.Normal is { IsCancelled: false } normal ? normal : state.Momentum;
                    state.CompletedSequence = state.Normal = null;
                    target = HitTarget(state, input, revision, generation);
                    if (!IsDispatchCurrent(state, revision, generation)) { handled = true; return true; }
                    state.MomentumTarget = target == null ? null : new WeakReference<IInputElement>(target);
                }
                else if (state.MomentumTarget == null || !state.MomentumTarget.TryGetTarget(out target) ||
                    state.Momentum == null || state.Momentum.IsCancelled || !BelongsTo(target, source))
                {
                    // Never attach a late/retired momentum tail to a new target.
                    state.Momentum?.Cancel(); state.Momentum = state.MomentumSequence = null; state.MomentumTarget = null;
                    handled = true; return true;
                }
                lifetime = state.Momentum;
                sequence = state.MomentumSequence;
            }
            else
            {
                state.Momentum?.Cancel(); state.Momentum = state.MomentumSequence = null; state.MomentumTarget = null;
                if (state.Normal == null || phase is 1 or 32) state.Normal = new PortableScrollLifetime();
                state.CompletedSequence = null;
                lifetime = state.Normal;
                sequence = state.Normal;
                target = HitTarget(state, input, revision, generation);
            }

            if (!IsDispatchCurrent(state, revision, generation)) { handled = true; return true; }

            bool cancellation = phase == 16;
            if (cancellation) lifetime.Cancel();
            try
            {
                if (target == null) return true;
                var args = new PortableScrollEventArgs(state, target, input, lifetime, sequence, cancellation)
                { RoutedEvent = PreviewScrollEvent };
                target.RaiseEvent(args);
                if (args.IsCurrent)
                {
                    args.RoutedEvent = ScrollEvent;
                    target.RaiseEvent(args);
                }
                // A partial default consumption must not replay the original
                // complete packet through an independent host fallback.
                handled = args.Handled || args.HasConsumedMotion;
                return true;
            }
            catch
            {
                // Do not retire newer nested input from a failing older callback.
                if (revision == state.Revision) lifetime.Cancel();
                throw;
            }
            finally
            {
                if (revision == state.Revision && phase is 8 or 16)
                {
                    // End retains already accepted queued motion. Cancel has
                    // retired the shared lease, including earlier target queues.
                    if (momentum) { state.Momentum = state.MomentumSequence = null; state.MomentumTarget = null; }
                    else { state.CompletedSequence = cancellation ? null : sequence; state.Normal = null; }
                }
            }
        }

        private static bool TryPhase(PortablePointerInput input, out uint phase, out bool momentum)
        {
            phase = 0; momentum = false;
            if (input.Kind != PortablePointerEventKind.Scroll) return false;
            if (input.ScrollProtocol == PortablePointerScrollProtocol.Unspecified)
                return input.ScrollPhase == 0 && input.MomentumPhase == 0;
            if (input.ScrollProtocol != PortablePointerScrollProtocol.AppKit ||
                input.ScrollPhase != 0 && input.MomentumPhase != 0) return false;
            // Admit explicit AppKit scroll phases, never unknown flags or a
            // contradictory combination. Raw fields remain intact on the event.
            if (input.ScrollPhase is not (0 or 1 or 2 or 4 or 8 or 16 or 32) ||
                input.MomentumPhase is not (0 or 1 or 2 or 4 or 8 or 16)) return false;
            momentum = input.MomentumPhase != 0;
            phase = momentum ? input.MomentumPhase : input.ScrollPhase;
            return true;
        }

        private static bool IsDispatchCurrent(RouteState state, ulong revision, ulong generation) =>
            state.Revision == revision && !state.Source.IsDisposed && state.Source.PointerInputGeneration == generation;

        private static IInputElement HitTarget(RouteState state, PortablePointerInput input,
            ulong revision, ulong generation)
        {
            try { return MouseDevice.LocalHitTest(false, new Point(input.X, input.Y), state.Source); }
            catch
            {
                if (IsDispatchCurrent(state, revision, generation)) state.Cancel();
                throw;
            }
        }

        internal static bool BelongsTo(IInputElement target, PortablePresentationSource source)
        {
            DependencyObject visual = target is DependencyObject element ? InputElement.GetContainingVisual(element) : null;
            return visual != null && target.IsEnabled && ReferenceEquals(PresentationSource.CriticalFromVisual(visual), source);
        }

        internal sealed class RouteState
        {
            internal readonly PortablePresentationSource Source;
            internal ulong Revision;
            internal PortableScrollLifetime Normal, Momentum;
            internal PortableScrollLifetime CompletedSequence, MomentumSequence;
            internal WeakReference<IInputElement> MomentumTarget;
            private ulong _generation;
            internal RouteState(PortablePresentationSource source) { Source = source; _generation = source.PointerInputGeneration; }
            internal void Refresh()
            {
                if (_generation == Source.PointerInputGeneration) return;
                Cancel(); _generation = Source.PointerInputGeneration;
            }
            internal void Cancel()
            {
                Normal?.Cancel(); Momentum?.Cancel(); CompletedSequence?.Cancel(); MomentumSequence?.Cancel();
                Normal = Momentum = CompletedSequence = MomentumSequence = null; MomentumTarget = null;
            }
        }
    }
}
