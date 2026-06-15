using System;
using System.Reflection;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfGuidelineSetReflection
{
    public static bool TryReadDynamicGuidelineY1(object? guidelines, out double coordinate)
    {
        coordinate = default;
        if (TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY)
            && guidelinesX.Length == 0
            && guidelinesY.Length == 1)
        {
            coordinate = guidelinesY[0];
            return true;
        }

        return false;
    }

    public static bool TryReadDynamicGuidelineYPair(
        object? guidelines,
        out double leadingCoordinate,
        out double drivenCoordinate)
    {
        leadingCoordinate = default;
        drivenCoordinate = default;

        if (!TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY)
            || guidelinesX.Length != 0
            || guidelinesY.Length != 2)
        {
            return false;
        }

        leadingCoordinate = guidelinesY[0];
        drivenCoordinate = guidelinesY[1];
        return true;
    }

    public static bool TryReadDynamicGuidelineSet(
        object? guidelines,
        out double[] guidelinesX,
        out double[] guidelinesY)
    {
        guidelinesX = Array.Empty<double>();
        guidelinesY = Array.Empty<double>();

        if (guidelines == null
            || !IsDynamicFrozenGuidelineSet(guidelines)
            || !TryGetPropertyValue(guidelines, "GuidelinesX", out var guidelinesXValue)
            || !TryGetPropertyValue(guidelines, "GuidelinesY", out var guidelinesYValue)
            || !TryReadDoubleCollection(guidelinesXValue, out guidelinesX)
            || !TryReadDoubleCollection(guidelinesYValue, out guidelinesY))
        {
            guidelinesX = Array.Empty<double>();
            guidelinesY = Array.Empty<double>();
            return false;
        }

        return true;
    }

    private static bool IsDynamicFrozenGuidelineSet(object guidelines)
    {
        return TryGetPropertyValue(guidelines, "IsFrozen", out var isFrozenValue)
            && TryGetPropertyValue(guidelines, "IsDynamic", out var isDynamicValue)
            && isFrozenValue is bool isFrozen
            && isDynamicValue is bool isDynamic
            && isFrozen
            && isDynamic;
    }

    public static bool TryReadDoubleCollection(object? collection, out double[] values)
    {
        values = Array.Empty<double>();

        if (collection == null
            || !TryGetPropertyValue(collection, "Count", out var countValue)
            || countValue is not int count
            || count < 0)
        {
            return false;
        }

        var indexer = FindIndexer(collection.GetType());
        if (indexer == null)
        {
            return count == 0;
        }

        values = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (!TryConvertToDouble(indexer(collection, i), out values[i]))
            {
                values = Array.Empty<double>();
                return false;
            }
        }

        return true;
    }

    private static Func<object, int, object?>? FindIndexer(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var parameters = property.GetIndexParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
            {
                return (instance, index) => property.GetValue(instance, new object[] { index });
            }
        }

        return null;
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
