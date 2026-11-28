// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using ProGPU.Wpf.Interop;

namespace System.Windows.Platform.MacOS
{
    /// <summary>
    /// Maps a WPF Menu/MenuItem tree to the neutral PortableMenuItem model (docs/macos-window-integration.md
    /// section 4.6). Pure: nothing here touches a native window.
    /// </summary>
    internal sealed class MacMenuMapper
    {
        internal const int ModifierCommand = 1;
        internal const int ModifierShift = 2;
        internal const int ModifierOption = 4;
        internal const int ModifierControl = 8;

        private readonly Dictionary<int, MenuItem> _items = new Dictionary<int, MenuItem>();
        private int _nextId = 1;

        /// <summary>The WPF item behind an Id of the last mapped model, or null.</summary>
        internal MenuItem GetItem(int id) => _items.TryGetValue(id, out MenuItem item) ? item : null;

        internal PortableMenuItem[] Map(Menu menu)
        {
            _items.Clear();
            _nextId = 1;
            return menu == null ? Array.Empty<PortableMenuItem>() : MapChildren(menu.Items, topLevel: true);
        }

        private PortableMenuItem[] MapChildren(ItemCollection items, bool topLevel)
        {
            var result = new List<PortableMenuItem>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                object entry = items[i];
                if (entry is Separator separator)
                {
                    if (separator.Visibility == Visibility.Visible)
                    {
                        result.Add(new PortableMenuItem(_nextId++, string.Empty, string.Empty, 0, true, false, false, 0, Array.Empty<PortableMenuItem>()));
                    }
                }
                else if (entry is MenuItem item && item.Visibility == Visibility.Visible)
                {
                    result.Add(MapItem(item, topLevel));
                }
            }

            return result.ToArray();
        }

        private PortableMenuItem MapItem(MenuItem item, bool topLevel)
        {
            int id = _nextId++;
            _items[id] = item;
            string title = StripAccessKey(HeaderText(item.Header));
            TryGetKeyEquivalent(item, out string key, out int modifiers);
            return new PortableMenuItem(
                id,
                title,
                key,
                modifiers,
                false,
                item.IsEnabled,
                item.IsCheckable && item.IsChecked,
                topLevel ? RoleFromHeader(title) : 0,
                MapChildren(item.Items, topLevel: false));
        }

        /// <summary>A header is text only when it is a string or a non-visual value; an element has no title.</summary>
        private static string HeaderText(object header) =>
            header is string text ? text :
            header == null || header is UIElement || header is ContentElement ? string.Empty :
            header.ToString() ?? string.Empty;

        /// <summary>Removes the WPF access-key underscore (macOS has none); "__" is a literal underscore.</summary>
        internal static string StripAccessKey(string header)
        {
            if (string.IsNullOrEmpty(header) || header.IndexOf('_') < 0)
            {
                return header ?? string.Empty;
            }

            var builder = new StringBuilder(header.Length);
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] == '_')
                {
                    if (i + 1 < header.Length && header[i + 1] == '_')
                    {
                        builder.Append('_');
                        i++;
                    }

                    continue;
                }

                builder.Append(header[i]);
            }

            return builder.ToString();
        }

        /// <summary>Header fallback only; an explicit role attached property is a later step.</summary>
        internal static int RoleFromHeader(string title) => title switch
        {
            "Edit" => 1,
            "Window" => 2,
            "Help" => 3,
            _ => 0,
        };

        internal static bool TryGetKeyEquivalent(MenuItem item, out string key, out int modifiers)
        {
            key = string.Empty;
            modifiers = 0;

            if (item.Command is RoutedCommand routed)
            {
                foreach (InputGesture gesture in routed.InputGestures)
                {
                    if (gesture is KeyGesture keyGesture &&
                        TryMapKey(keyGesture.Key, out key) &&
                        TryMapModifiers(keyGesture.Modifiers, out modifiers) &&
                        modifiers != 0)
                    {
                        return true;
                    }
                }
            }

            key = string.Empty;
            modifiers = 0;
            return TryParseGestureText(item.InputGestureText, out key, out modifiers);
        }

        /// <summary>Parses "Cmd+Shift+1" style display text (Cmd/Command/Win/Windows, Ctrl/Control, Alt/Option, Shift).</summary>
        internal static bool TryParseGestureText(string text, out string key, out int modifiers)
        {
            key = string.Empty;
            modifiers = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "cmd":
                    case "command":
                    case "win":
                    case "windows":
                        modifiers |= ModifierCommand;
                        break;
                    case "ctrl":
                    case "control":
                        modifiers |= ModifierControl;
                        break;
                    case "alt":
                    case "option":
                        modifiers |= ModifierOption;
                        break;
                    case "shift":
                        modifiers |= ModifierShift;
                        break;
                    default:
                        modifiers = 0;
                        return false;
                }
            }

            string last = parts[parts.Length - 1].Trim();
            // A key equivalent without a modifier would fire on plain typing.
            if (modifiers != 0 && last.Length == 1 && char.IsAsciiLetterOrDigit(last[0]))
            {
                key = last.ToLowerInvariant();
                return true;
            }

            modifiers = 0;
            return false;
        }

        private static bool TryMapKey(Key key, out string text)
        {
            text = string.Empty;
            if (key >= Key.A && key <= Key.Z)
            {
                text = ((char)('a' + (key - Key.A))).ToString();
                return true;
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                text = ((char)('0' + (key - Key.D0))).ToString();
                return true;
            }

            return false;
        }

        private static bool TryMapModifiers(ModifierKeys keys, out int modifiers)
        {
            modifiers = 0;
            if ((keys & ModifierKeys.Windows) != 0) modifiers |= ModifierCommand;
            if ((keys & ModifierKeys.Control) != 0) modifiers |= ModifierControl;
            if ((keys & ModifierKeys.Alt) != 0) modifiers |= ModifierOption;
            if ((keys & ModifierKeys.Shift) != 0) modifiers |= ModifierShift;
            return true;
        }
    }
}
