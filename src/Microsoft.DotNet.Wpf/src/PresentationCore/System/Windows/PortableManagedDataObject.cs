// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace System.Windows;

internal sealed class PortableManagedDataObject : ITypedDataObject
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
