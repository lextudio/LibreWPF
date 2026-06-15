using System;
using System.Numerics;
using System.Reflection;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfEffectReflection
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool TryCreateProGpuEffect(object? effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        if (effect != null)
        {
            if (TypeNameEndsWith(effect, "BlurEffect")
                && TryCreateBlurEffect(effect, out proGpuEffect))
            {
                return true;
            }

            if (TypeNameEndsWith(effect, "DropShadowEffect")
                && TryCreateDropShadowEffect(effect, out proGpuEffect))
            {
                return true;
            }
        }

        proGpuEffect = null!;
        return false;
    }

    private static bool TryCreateBlurEffect(object effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        var radius = 5d;
        if (TryReadDoubleProperty(effect, "Radius", out var reflectedRadius)
            || TryReadDoubleProperty(effect, "BlurRadius", out reflectedRadius))
        {
            radius = reflectedRadius;
        }

        proGpuEffect = new global::ProGPU.Scene.BlurEffect((float)Math.Max(0d, radius));
        return true;
    }

    private static bool TryCreateDropShadowEffect(object effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        var blurRadius = TryReadDoubleProperty(effect, "BlurRadius", out var reflectedBlurRadius)
            ? Math.Max(0d, reflectedBlurRadius)
            : 5d;
        var shadowDepth = TryReadDoubleProperty(effect, "ShadowDepth", out var reflectedDepth)
            ? reflectedDepth
            : 5d;
        var direction = TryReadDoubleProperty(effect, "Direction", out var reflectedDirection)
            ? reflectedDirection
            : 315d;
        var opacity = TryReadDoubleProperty(effect, "Opacity", out var reflectedOpacity)
            ? Math.Clamp(reflectedOpacity, 0d, 1d)
            : 1d;

        var radians = direction * Math.PI / 180d;
        var offset = new Vector2(
            (float)(shadowDepth * Math.Cos(radians)),
            (float)(-shadowDepth * Math.Sin(radians)));

        var color = new Vector4(0f, 0f, 0f, (float)opacity);
        if (TryGetPropertyValue(effect, "Color", out var colorValue)
            && TryReadColor(colorValue, opacity, out var reflectedColor))
        {
            color = reflectedColor;
        }

        proGpuEffect = new global::ProGPU.Scene.DropShadowEffect(
            (float)blurRadius,
            offset,
            color);
        return true;
    }

    private static bool TryReadColor(object? colorValue, double opacity, out Vector4 color)
    {
        color = default;
        if (colorValue == null)
        {
            return false;
        }

        if (colorValue is System.Windows.Media.Color mediaColor)
        {
            color = ToVectorColor(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B, opacity);
            return true;
        }

        if (TryReadByteOrDoubleColorProperty(colorValue, "A", out var a)
            && TryReadByteOrDoubleColorProperty(colorValue, "R", out var r)
            && TryReadByteOrDoubleColorProperty(colorValue, "G", out var g)
            && TryReadByteOrDoubleColorProperty(colorValue, "B", out var b))
        {
            color = ToVectorColor(a, r, g, b, opacity);
            return true;
        }

        return false;
    }

    private static Vector4 ToVectorColor(byte a, byte r, byte g, byte b, double opacity)
    {
        return new Vector4(
            r / 255f,
            g / 255f,
            b / 255f,
            (float)((a / 255d) * opacity));
    }

    private static bool TryReadByteOrDoubleColorProperty(object instance, string propertyName, out byte value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue)
        {
            case byte byteValue:
                value = byteValue;
                return true;
            case double doubleValue:
                value = (byte)Math.Clamp(Math.Round(doubleValue), 0d, 255d);
                return true;
            case float floatValue:
                value = (byte)Math.Clamp(MathF.Round(floatValue), 0f, 255f);
                return true;
            case int intValue:
                value = (byte)Math.Clamp(intValue, 0, 255);
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static bool TypeNameEndsWith(object instance, string suffix)
    {
        var type = instance.GetType();
        return type.Name.EndsWith(suffix, StringComparison.Ordinal)
            || (type.FullName?.EndsWith("." + suffix, StringComparison.Ordinal) ?? false);
    }
}
