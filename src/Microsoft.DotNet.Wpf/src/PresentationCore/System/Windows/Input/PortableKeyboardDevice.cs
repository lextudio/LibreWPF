// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Windows.Input
{
    internal sealed class PortableKeyboardDevice : KeyboardDevice
    {
        private readonly Dictionary<Key, KeyStates> _keyStates = new Dictionary<Key, KeyStates>();
        private ModifierKeys? _eventModifiers;
        private long _eventModifierScope;
        private long _nextEventModifierScope;

        internal PortableKeyboardDevice(InputManager inputManager)
            : base(inputManager)
        {
        }

        internal void SetKeyStates(Key key, KeyStates keyStates)
        {
            if (keyStates == KeyStates.None)
            {
                _keyStates.Remove(key);
            }
            else
            {
                _keyStates[key] = keyStates;
            }
        }

        internal override ModifierKeys GetModifiers() => _eventModifiers ?? base.GetModifiers();

        // A queued pointer event may predate the current keyboard cache. Its
        // aggregate belongs only to synchronous source delivery, including nested
        // input. Never restore old key states over real key-up events delivered
        // by a nested callback, or invent left/right keys from aggregate flags.
        internal EventModifierScope PushEventModifiers(ModifierKeys modifiers)
        {
            VerifyAccess();
            const ModifierKeys supported = ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift;
            if ((modifiers & ~supported) != 0)
                throw new ArgumentOutOfRangeException(nameof(modifiers));

            long scope = checked(_nextEventModifierScope + 1);
            var result = new EventModifierScope(this, scope, _eventModifierScope, _eventModifiers);
            _nextEventModifierScope = scope;
            _eventModifierScope = scope;
            _eventModifiers = modifiers;
            return result;
        }

        internal ref struct EventModifierScope
        {
            private PortableKeyboardDevice _device;
            private readonly long _scope;
            private readonly long _previousScope;
            private readonly ModifierKeys? _previousModifiers;

            internal EventModifierScope(PortableKeyboardDevice device, long scope,
                long previousScope, ModifierKeys? previousModifiers)
            {
                _device = device;
                _scope = scope;
                _previousScope = previousScope;
                _previousModifiers = previousModifiers;
            }

            public void Dispose()
            {
                if (_device == null) return;
                _device.VerifyAccess();
                if (_device._eventModifierScope != _scope)
                    throw new InvalidOperationException("Input modifier scopes must unwind in source callback order.");
                _device._eventModifiers = _previousModifiers;
                _device._eventModifierScope = _previousScope;
                _device = null;
            }
        }

        protected override KeyStates GetKeyStatesFromSystem(Key key)
        {
            return _keyStates.TryGetValue(key, out KeyStates keyStates)
                ? keyStates
                : KeyStates.None;
        }
    }
}
