// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;

namespace System.Windows;

internal static class PortableClipboardService
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly object s_sync = new object();
    private static IDataObject? s_dataObject;
    private static Func<string?>? s_getText;
    private static Action<string?>? s_setText;

    internal static bool IsEnabled
    {
        get
        {
            return !s_isWindows;
        }
    }

    internal static IDisposable Register(Func<string?> getText, Action<string?> setText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(setText);

        if (s_isWindows)
        {
            return EmptyRegistration.Instance;
        }

        Volatile.Write(ref s_getText, getText);
        Volatile.Write(ref s_setText, setText);
        return new Registration(getText, setText);
    }

    internal static void Clear()
    {
        lock (s_sync)
        {
            s_dataObject = null;
        }

        Volatile.Read(ref s_setText)?.Invoke(null);
    }

    internal static bool TryClear()
    {
        if (s_isWindows)
        {
            return false;
        }

        Clear();
        return true;
    }

    internal static bool TryFlush()
    {
        return !s_isWindows;
    }

    internal static bool TryGetDataObject(out IDataObject? dataObject)
    {
        dataObject = null;
        if (s_isWindows)
        {
            return false;
        }

        lock (s_sync)
        {
            if (s_dataObject != null)
            {
                dataObject = s_dataObject;
                return true;
            }
        }

        Func<string?>? getText = Volatile.Read(ref s_getText);
        if (getText == null)
        {
            return true;
        }

        string? text = getText();
        if (text == null)
        {
            return true;
        }

        var textDataObject = new PortableDataObject();
        textDataObject.SetData(DataFormats.UnicodeText, text, autoConvert: false);
        lock (s_sync)
        {
            s_dataObject ??= textDataObject;
            dataObject = s_dataObject;
        }

        return true;
    }

    internal static bool TryIsCurrent(IDataObject data, out bool isCurrent)
    {
        isCurrent = false;
        if (s_isWindows)
        {
            return false;
        }

        lock (s_sync)
        {
            isCurrent = ReferenceEquals(s_dataObject, data);
        }

        return true;
    }

    internal static bool TrySetData(string format, object data, bool autoConvert, bool copy)
    {
        if (s_isWindows)
        {
            return false;
        }

        var dataObject = new PortableDataObject();
        dataObject.SetData(format, data, autoConvert);
        return TrySetDataObject(dataObject, copy);
    }

    internal static bool TrySetFileDropList(StringCollection fileDropList)
    {
        if (s_isWindows)
        {
            return false;
        }

        string[] strings = new string[fileDropList.Count];
        fileDropList.CopyTo(strings, 0);
        return TrySetData(DataFormats.FileDrop, strings, autoConvert: true, copy: true);
    }

    internal static bool TrySetObject(object data, bool copy)
    {
        if (s_isWindows)
        {
            return false;
        }

        IDataObject dataObject;
        if (data is IDataObject existingDataObject)
        {
            dataObject = existingDataObject;
        }
        else
        {
            var portableDataObject = new PortableDataObject();
            portableDataObject.SetData(data);
            dataObject = portableDataObject;
        }

        return TrySetDataObject(dataObject, copy);
    }

    internal static bool TrySetDataObject(IDataObject dataObject, bool copy)
    {
        if (s_isWindows)
        {
            return false;
        }

        lock (s_sync)
        {
            s_dataObject = dataObject;
        }

        if (TryGetUnicodeText(dataObject, out string? text))
        {
            Volatile.Read(ref s_setText)?.Invoke(text);
        }

        return true;
    }

    private static bool TryGetUnicodeText(IDataObject dataObject, out string? text)
    {
        text = null;
        if (!dataObject.GetDataPresent(DataFormats.UnicodeText, autoConvert: false))
        {
            return false;
        }

        text = dataObject.GetData(DataFormats.UnicodeText, autoConvert: false) as string;
        return true;
    }

    private sealed class Registration : IDisposable
    {
        private Func<string?>? _getText;
        private Action<string?>? _setText;

        internal Registration(Func<string?> getText, Action<string?> setText)
        {
            _getText = getText;
            _setText = setText;
        }

        public void Dispose()
        {
            Func<string?>? getText = _getText;
            Action<string?>? setText = _setText;
            if (getText == null || setText == null)
            {
                return;
            }

            _getText = null;
            _setText = null;

            if (ReferenceEquals(Volatile.Read(ref s_getText), getText))
            {
                Volatile.Write(ref s_getText, null);
            }

            if (ReferenceEquals(Volatile.Read(ref s_setText), setText))
            {
                Volatile.Write(ref s_setText, null);
            }
        }
    }

    private sealed class EmptyRegistration : IDisposable
    {
        internal static readonly EmptyRegistration Instance = new EmptyRegistration();

        public void Dispose()
        {
        }
    }

    private sealed class PortableDataObject : ITypedDataObject
    {
        private readonly Dictionary<string, Entry> _data = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public object? GetData(string format) => GetData(format, autoConvert: true);

        public object? GetData(Type format)
        {
            ArgumentNullException.ThrowIfNull(format);
            return GetData(format.FullName ?? format.Name, autoConvert: true);
        }

        public object? GetData(string format, bool autoConvert)
        {
            ArgumentException.ThrowIfNullOrEmpty(format);
            return TryGetEntry(format, autoConvert, out Entry entry) ? entry.Data : null;
        }

        public bool GetDataPresent(string format) => GetDataPresent(format, autoConvert: true);

        public bool GetDataPresent(Type format)
        {
            ArgumentNullException.ThrowIfNull(format);
            return GetDataPresent(format.FullName ?? format.Name, autoConvert: true);
        }

        public bool GetDataPresent(string format, bool autoConvert)
        {
            ArgumentException.ThrowIfNullOrEmpty(format);
            return TryGetEntry(format, autoConvert, out _);
        }

        public string[] GetFormats() => GetFormats(autoConvert: true);

        public string[] GetFormats(bool autoConvert)
        {
            string[] formats = new string[_data.Count];
            _data.Keys.CopyTo(formats, 0);
            return formats;
        }

        public void SetData(object? data)
        {
            SetData(data?.GetType().FullName ?? DataFormats.Serializable, data, autoConvert: true);
        }

        public void SetData(string format, object? data)
        {
            SetData(format, data, autoConvert: true);
        }

        public void SetData(Type format, object? data)
        {
            ArgumentNullException.ThrowIfNull(format);
            SetData(format.FullName ?? format.Name, data, autoConvert: true);
        }

        public void SetData(string format, object? data, bool autoConvert)
        {
            ArgumentException.ThrowIfNullOrEmpty(format);
            _data[format] = new Entry(data, autoConvert);
        }

        public bool TryGetData<T>([NotNullWhen(true), MaybeNullWhen(false)] out T data)
        {
            return TryGetData(typeof(T).FullName ?? typeof(T).Name, autoConvert: true, out data);
        }

        public bool TryGetData<T>(
            string format,
            [NotNullWhen(true), MaybeNullWhen(false)] out T data)
        {
            return TryGetData(format, autoConvert: true, out data);
        }

        public bool TryGetData<T>(
            string format,
            bool autoConvert,
            [NotNullWhen(true), MaybeNullWhen(false)] out T data)
        {
            data = default;
            object? value = GetData(format, autoConvert);
            if (value is not T typed)
            {
                return false;
            }

            data = typed;
            return true;
        }

        public bool TryGetData<T>(
            string format,
            Func<TypeName, Type?> resolver,
            bool autoConvert,
            [NotNullWhen(true), MaybeNullWhen(false)] out T data)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            return TryGetData(format, autoConvert, out data);
        }

        private bool TryGetEntry(string format, bool autoConvert, out Entry entry)
        {
            if (_data.TryGetValue(format, out entry))
            {
                return true;
            }

            if (!autoConvert)
            {
                entry = default;
                return false;
            }

            if (IsTextFormat(format)
                && _data.TryGetValue(DataFormats.UnicodeText, out entry)
                && entry.AutoConvert)
            {
                return true;
            }

            entry = default;
            return false;
        }

        private static bool IsTextFormat(string format)
        {
            return format == DataFormats.Text
                || format == DataFormats.UnicodeText
                || format == DataFormats.StringFormat;
        }

        private readonly struct Entry
        {
            internal Entry(object? data, bool autoConvert)
            {
                Data = data;
                AutoConvert = autoConvert;
            }

            internal object? Data { get; }

            internal bool AutoConvert { get; }
        }
    }
}
