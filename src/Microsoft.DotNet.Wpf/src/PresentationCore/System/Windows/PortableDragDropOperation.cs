// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Input;
using System.Windows.Threading;

namespace System.Windows
{
    /// <summary>
    /// Portable (non-Windows, non-OLE) replacement for the source half of
    /// <see cref="DragDrop.DoDragDrop"/>. Real WPF's drag source blocks on a Win32 OLE modal loop
    /// (<c>OleServicesContext.OleDoDragDrop</c>) that pumps native mouse/keyboard messages and
    /// calls back into <see cref="OleDragSource"/>/<see cref="OleDropTarget"/> as the pointer moves
    /// over registered drop-target HWNDs. <see cref="PortablePresentationSource"/> has no HWND/OLE
    /// message pump to drive that loop, so <see cref="DragDrop.DoDragDrop"/> previously "failed
    /// closed" (returned <see cref="DragDropEffects.None"/> immediately) for portable sources -
    /// meaning no WPF drag-and-drop (e.g. a toolbox item dragged onto a design surface) ever
    /// actually ran off Windows.
    ///
    /// This reimplements the same source-side protocol without OLE: capture the mouse on the drag
    /// source, push a nested <see cref="DispatcherFrame"/> so the call stays synchronous exactly
    /// like the OLE path while still processing input, and on every mouse move hit-test the
    /// portable source's visual tree (<see cref="MouseDevice.LocalHitTest"/>, which is already
    /// source-agnostic) to find the current drop target - then drive the SAME
    /// DragEnter/DragOver/DragLeave/Drop routed events real portable drop targets already handle
    /// via <see cref="DragDrop.ProcessPortableDragDrop"/> (that half of the pipeline was already
    /// built; only this source-side driver was missing). QueryContinueDrag/GiveFeedback are raised
    /// with the same default fallback semantics <see cref="OleDragSource"/> uses on Windows, so a
    /// handler written against the public DragDrop routed events behaves identically on both.
    ///
    /// Scoped to <see cref="UIElement"/> drag sources only (matches every current caller -
    /// WpfToolbox's own drag source is a ListBox); <see cref="ContentElement"/>/
    /// <see cref="UIElement3D"/> sources fail closed the same way the whole portable path used to.
    /// </summary>
    internal sealed class PortableDragDropOperation
    {
        private readonly UIElement _dragSource;
        private readonly PortablePresentationSource _source;
        private readonly DataObject _dataObject;
        private readonly DragDropEffects _allowedEffects;

        private DependencyObject _currentTarget;
        private DragDropEffects _lastEffects = DragDropEffects.None;
        private DragAction _action = DragAction.Continue;
        private bool _dropped;

        private PortableDragDropOperation(UIElement dragSource, PortablePresentationSource source, DataObject dataObject, DragDropEffects allowedEffects)
        {
            _dragSource = dragSource;
            _source = source;
            _dataObject = dataObject;
            _allowedEffects = allowedEffects;
        }

        /// <summary>
        /// Runs a portable drag-and-drop operation for <paramref name="dragSource"/>, or returns
        /// <see cref="DragDropEffects.None"/> immediately if the source isn't a portable
        /// <see cref="UIElement"/> with a live root visual (mirrors the old fail-closed behavior
        /// for every case this doesn't (yet) support).
        /// </summary>
        internal static DragDropEffects Run(DependencyObject dragSource, DataObject dataObject, DragDropEffects allowedEffects)
        {
            if (dragSource is not UIElement dragElement)
                return DragDropEffects.None;

            if (PresentationSource.CriticalFromVisual(dragSource) is not PortablePresentationSource source || source.RootVisual == null)
                return DragDropEffects.None;

            return new PortableDragDropOperation(dragElement, source, dataObject, allowedEffects).RunCore();
        }

        private DragDropEffects RunCore()
        {
            if (!Mouse.Capture(_dragSource, CaptureMode.SubTree))
                return DragDropEffects.None;

            var frame = new DispatcherFrame();

            MouseEventHandler onPreviewMouseMove = (sender, e) => OnPointerUpdate(frame, e.GetPosition((IInputElement)_source.RootVisual));
            MouseButtonEventHandler onPreviewMouseButtonUp = (sender, e) => OnPointerUpdate(frame, e.GetPosition((IInputElement)_source.RootVisual));
            KeyEventHandler onPreviewKeyDown = (sender, e) =>
            {
                if (e.Key != Key.Escape)
                    return;
                _action = DragAction.Cancel;
                frame.Continue = false;
            };

            _dragSource.PreviewMouseMove += onPreviewMouseMove;
            _dragSource.PreviewMouseUp += onPreviewMouseButtonUp;
            _dragSource.PreviewKeyDown += onPreviewKeyDown;

            try
            {
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                _dragSource.PreviewMouseMove -= onPreviewMouseMove;
                _dragSource.PreviewMouseUp -= onPreviewMouseButtonUp;
                _dragSource.PreviewKeyDown -= onPreviewKeyDown;

                if (ReferenceEquals(Mouse.Captured, _dragSource))
                    Mouse.Capture(null);

                if (_currentTarget != null && !_dropped)
                {
                    DragDrop.ProcessPortableDragDrop(
                        _currentTarget, DragDrop.DragLeaveEvent, _dataObject,
                        GetCurrentKeyStates(), _allowedEffects, DragDropEffects.None, default);
                }
            }

            return _dropped ? _lastEffects : DragDropEffects.None;
        }

        private void OnPointerUpdate(DispatcherFrame frame, Point rootPoint)
        {
            if (_action != DragAction.Continue)
                return;

            var keyStates = GetCurrentKeyStates();
            var query = new QueryContinueDragEventArgs(escapePressed: false, keyStates);
            RaiseQueryContinueDrag(query);
            _action = query.Action;

            if (_action == DragAction.Cancel)
            {
                frame.Continue = false;
                return;
            }

            var hit = MouseDevice.LocalHitTest(rootPoint, _source) as DependencyObject;
            var target = ResolveDropTarget(hit);

            if (!ReferenceEquals(target, _currentTarget))
            {
                if (_currentTarget != null)
                {
                    DragDrop.ProcessPortableDragDrop(
                        _currentTarget, DragDrop.DragLeaveEvent, _dataObject,
                        keyStates, _allowedEffects, DragDropEffects.None, default);
                }

                _currentTarget = target;

                if (target != null)
                {
                    var targetPoint = InputElement.TranslatePoint(rootPoint, _source.RootVisual, target);
                    _lastEffects = DragDrop.ProcessPortableDragDrop(
                        target, DragDrop.DragEnterEvent, _dataObject,
                        keyStates, _allowedEffects, DragDropEffects.None, targetPoint);
                }
                else
                {
                    _lastEffects = DragDropEffects.None;
                }
            }
            else if (target != null)
            {
                var targetPoint = InputElement.TranslatePoint(rootPoint, _source.RootVisual, target);
                _lastEffects = DragDrop.ProcessPortableDragDrop(
                    target, DragDrop.DragOverEvent, _dataObject,
                    keyStates, _allowedEffects, DragDropEffects.None, targetPoint);
            }

            var feedback = new GiveFeedbackEventArgs(_lastEffects, useDefaultCursors: true);
            RaiseGiveFeedback(feedback);

            if (_action == DragAction.Drop)
            {
                if (_currentTarget != null)
                {
                    var targetPoint = InputElement.TranslatePoint(rootPoint, _source.RootVisual, _currentTarget);
                    _lastEffects = DragDrop.ProcessPortableDragDrop(
                        _currentTarget, DragDrop.DropEvent, _dataObject,
                        keyStates, _allowedEffects, DragDropEffects.None, targetPoint);
                }
                else
                {
                    _lastEffects = DragDropEffects.None;
                }

                _dropped = true;
                frame.Continue = false;
            }
        }

        // Mirrors DragDrop.GetCurrentTarget's single-hit check (no ancestor walk) so a portable
        // drag targets exactly what a real Windows/OLE drag would - see OleDropTarget.GetCurrentTarget.
        private static DependencyObject ResolveDropTarget(DependencyObject hit)
        {
            return hit switch
            {
                UIElement { AllowDrop: true } uiElement => uiElement,
                ContentElement { AllowDrop: true } contentElement => contentElement,
                UIElement3D { AllowDrop: true } uiElement3D => uiElement3D,
                _ => null
            };
        }

        private static DragDropKeyStates GetCurrentKeyStates()
        {
            DragDropKeyStates states = 0;

            if (Mouse.LeftButton == MouseButtonState.Pressed)
                states |= DragDropKeyStates.LeftMouseButton;
            if (Mouse.RightButton == MouseButtonState.Pressed)
                states |= DragDropKeyStates.RightMouseButton;
            if (Mouse.MiddleButton == MouseButtonState.Pressed)
                states |= DragDropKeyStates.MiddleMouseButton;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                states |= DragDropKeyStates.ControlKey;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                states |= DragDropKeyStates.ShiftKey;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                states |= DragDropKeyStates.AltKey;

            return states;
        }

        // Same shape and default fallback as OleDragSource.RaiseQueryContinueDragEvent/
        // OnDefaultQueryContinueDrag, just raised directly on the drag source element instead of
        // through an IOleDropSource COM callback.
        private void RaiseQueryContinueDrag(QueryContinueDragEventArgs args)
        {
            args.RoutedEvent = DragDrop.PreviewQueryContinueDragEvent;
            _dragSource.RaiseEvent(args);

            args.RoutedEvent = DragDrop.QueryContinueDragEvent;
            if (!args.Handled)
                _dragSource.RaiseEvent(args);

            if (args.Handled)
                return;

            int mouseButtonDownCount = 0;
            if ((args.KeyStates & DragDropKeyStates.LeftMouseButton) != 0)
                mouseButtonDownCount++;
            if ((args.KeyStates & DragDropKeyStates.MiddleMouseButton) != 0)
                mouseButtonDownCount++;
            if ((args.KeyStates & DragDropKeyStates.RightMouseButton) != 0)
                mouseButtonDownCount++;

            args.Action = DragAction.Continue;
            if (args.EscapePressed || mouseButtonDownCount >= 2)
                args.Action = DragAction.Cancel;
            else if (mouseButtonDownCount == 0)
                args.Action = DragAction.Drop;
        }

        private void RaiseGiveFeedback(GiveFeedbackEventArgs args)
        {
            args.RoutedEvent = DragDrop.PreviewGiveFeedbackEvent;
            _dragSource.RaiseEvent(args);

            args.RoutedEvent = DragDrop.GiveFeedbackEvent;
            if (!args.Handled)
                _dragSource.RaiseEvent(args);

            if (!args.Handled)
                args.UseDefaultCursors = true;
        }
    }
}
