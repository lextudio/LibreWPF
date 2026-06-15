using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using ProGPU.Scene;

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

            if (IsShaderEffectLike(effect)
                && TryCreateShaderEffect(effect, out proGpuEffect))
            {
                return true;
            }

            if (TryCreateEmulatedBitmapEffect(effect, out proGpuEffect))
            {
                return true;
            }
        }

        proGpuEffect = null!;
        return false;
    }

    public static bool TryCreateProGpuPushEffect(
        object? effect,
        object? effectInput,
        out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        proGpuEffect = null!;
        if (effect == null || !IsSupportedBitmapEffectInput(effectInput))
        {
            return false;
        }

        return TryCreateProGpuEffect(effect, out proGpuEffect);
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

    private static bool TryCreateShaderEffect(object effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        proGpuEffect = null!;

        if (!TryGetPropertyValue(effect, "PixelShader", out var pixelShader) || pixelShader == null)
        {
            return false;
        }

        if (!TryResolveShaderReplacement(effect, pixelShader, out var replacement))
        {
            return false;
        }

        if (TryReadUIntField(effect, "_intCount", out var intCount) && intCount > 0)
        {
            return false;
        }

        if (TryReadUIntField(effect, "_boolCount", out var boolCount) && boolCount > 0)
        {
            return false;
        }

        if (!TryReadShaderSamplerState(effect, out var sourceTextureRegisterIndex, out var samplingMode))
        {
            return false;
        }

        var parameters = new WpfShaderEffectParams
        {
            ShaderSource = replacement.ShaderSource,
            ShaderKey = replacement.ShaderKey,
            Constants = ReadFloatConstants(effect),
            SamplingMode = samplingMode,
            SourceTextureRegisterIndex = sourceTextureRegisterIndex
        };

        var nativeEffect = new WpfShaderEffect(parameters)
        {
            Padding = ReadMaxShaderPadding(effect)
        };

        proGpuEffect = nativeEffect;
        return true;
    }

    private static bool TryCreateEmulatedBitmapEffect(object effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        proGpuEffect = null!;
        if (!TypeNameEndsWith(effect, "BitmapEffect"))
        {
            return false;
        }

        if (TryInvokeBoolMethod(effect, "CanBeEmulatedUsingEffectPipeline", out var canBeEmulated)
            && !canBeEmulated)
        {
            return false;
        }

        if (TryInvokeMethod(effect, "GetEmulatingEffect", out var emulatedEffect)
            && emulatedEffect != null
            && !ReferenceEquals(effect, emulatedEffect))
        {
            return TryCreateProGpuEffect(emulatedEffect, out proGpuEffect);
        }

        return false;
    }

    private static bool TryResolveShaderReplacement(
        object effect,
        object pixelShader,
        out WpfShaderEffectReplacement replacement)
    {
        replacement = null!;

        foreach (var key in EnumerateShaderReplacementKeys(effect, pixelShader))
        {
            if (WpfShaderEffectRegistry.TryGet(key, out replacement))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateShaderReplacementKeys(object effect, object pixelShader)
    {
        var effectType = effect.GetType();
        if (!string.IsNullOrWhiteSpace(effectType.FullName))
        {
            yield return effectType.FullName!;
        }

        yield return effectType.Name;

        if (TryGetPropertyValue(pixelShader, "UriSource", out var uriSource) && uriSource != null)
        {
            yield return uriSource.ToString() ?? string.Empty;

            if (uriSource is Uri uri && uri.IsAbsoluteUri)
            {
                yield return uri.AbsoluteUri;
            }
        }

        if (TryGetByteArray(pixelShader, "_shaderBytecode", out var bytecode)
            || TryGetByteArray(pixelShader, "ShaderBytecode", out bytecode))
        {
            yield return WpfShaderEffectRegistry.CreatePixelShaderBytecodeKey(bytecode);
        }
    }

    private static float[] ReadFloatConstants(object effect)
    {
        if (!TryGetFieldValue(effect, "_floatRegisters", out var registers) || registers is not IEnumerable enumerable)
        {
            return Array.Empty<float>();
        }

        var constants = new float[WpfShaderEffectParams.ConstantFloatCount];
        var highestRegister = -1;
        var registerIndex = 0;

        foreach (var register in enumerable)
        {
            if (register != null)
            {
                if (registerIndex >= WpfShaderEffectParams.MaxConstantRegisterCount)
                {
                    break;
                }

                if (TryReadFloatTuple(register, out var tuple))
                {
                    var offset = registerIndex * WpfShaderEffectParams.FloatsPerConstantRegister;
                    constants[offset] = tuple.X;
                    constants[offset + 1] = tuple.Y;
                    constants[offset + 2] = tuple.Z;
                    constants[offset + 3] = tuple.W;
                    highestRegister = registerIndex;
                }
            }

            registerIndex++;
        }

        if (highestRegister < 0)
        {
            return Array.Empty<float>();
        }

        var length = (highestRegister + 1) * WpfShaderEffectParams.FloatsPerConstantRegister;
        Array.Resize(ref constants, length);
        return constants;
    }

    private static bool TryReadShaderSamplerState(
        object effect,
        out int sourceTextureRegisterIndex,
        out TextureSamplingMode samplingMode)
    {
        sourceTextureRegisterIndex = 0;
        samplingMode = TextureSamplingMode.Linear;

        if (!TryGetFieldValue(effect, "_samplerData", out var samplerData) || samplerData is not IEnumerable enumerable)
        {
            return true;
        }

        var registerIndex = 0;
        var hasImplicitInput = false;

        foreach (var sampler in enumerable)
        {
            if (sampler != null)
            {
                if (!TryGetMemberValue(sampler, "_brush", out var brush) || brush == null)
                {
                    registerIndex++;
                    continue;
                }

                if (!IsImplicitInputBrush(brush))
                {
                    return false;
                }

                if (hasImplicitInput)
                {
                    return false;
                }

                if (registerIndex >= WpfShaderEffectParams.MaxSamplerRegisterCount)
                {
                    return false;
                }

                sourceTextureRegisterIndex = registerIndex;
                hasImplicitInput = true;

                if (TryGetMemberValue(sampler, "_samplingMode", out var reflectedSamplingMode))
                {
                    samplingMode = ConvertSamplingMode(reflectedSamplingMode);
                }
            }

            registerIndex++;
        }

        return true;
    }

    private static float ReadMaxShaderPadding(object effect)
    {
        var padding = 0d;
        if (TryReadDoubleField(effect, "_topPadding", out var top))
        {
            padding = Math.Max(padding, top);
        }

        if (TryReadDoubleField(effect, "_bottomPadding", out var bottom))
        {
            padding = Math.Max(padding, bottom);
        }

        if (TryReadDoubleField(effect, "_leftPadding", out var left))
        {
            padding = Math.Max(padding, left);
        }

        if (TryReadDoubleField(effect, "_rightPadding", out var right))
        {
            padding = Math.Max(padding, right);
        }

        return (float)Math.Max(0d, padding);
    }

    private static bool IsSupportedBitmapEffectInput(object? effectInput)
    {
        if (effectInput == null)
        {
            return true;
        }

        return IsContextBitmapEffectInput(effectInput)
            && IsDefaultBitmapEffectArea(effectInput);
    }

    private static bool IsContextBitmapEffectInput(object effectInput)
    {
        if (TryInvokeBoolMethod(effectInput, "ShouldSerializeInput", out var shouldSerializeInput))
        {
            return !shouldSerializeInput;
        }

        if (!TryGetPropertyValue(effectInput, "Input", out var input))
        {
            return false;
        }

        if (input == null)
        {
            return true;
        }

        return TryGetStaticPropertyValue(effectInput.GetType(), "ContextInputSource", out var contextInputSource)
            && ReferenceEquals(input, contextInputSource);
    }

    private static bool IsDefaultBitmapEffectArea(object effectInput)
    {
        if (TryGetPropertyValue(effectInput, "AreaToApplyEffect", out var area)
            && area != null
            && TryGetPropertyValue(area, "IsEmpty", out var isEmpty)
            && isEmpty is bool empty)
        {
            return empty;
        }

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
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(propertyName, MemberFlags | BindingFlags.DeclaredOnly);
            if (property == null)
            {
                continue;
            }

            if (property.GetIndexParameters().Length != 0)
            {
                break;
            }

            value = property.GetValue(instance);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetStaticPropertyValue(Type instanceType, string propertyName, out object? value)
    {
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        for (var type = instanceType; type != null; type = type.BaseType)
        {
            var property = type.GetProperty(propertyName, staticFlags | BindingFlags.DeclaredOnly);
            if (property == null)
            {
                continue;
            }

            if (property.GetIndexParameters().Length != 0)
            {
                break;
            }

            value = property.GetValue(null);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetFieldValue(object instance, string fieldName, out object? value)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, MemberFlags | BindingFlags.DeclaredOnly);
            if (field == null)
            {
                continue;
            }

            value = field.GetValue(instance);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetMemberValue(object instance, string memberName, out object? value)
    {
        return TryGetFieldValue(instance, memberName, out value)
            || TryGetPropertyValue(instance, memberName, out value);
    }

    private static bool TryGetByteArray(object instance, string memberName, out byte[] value)
    {
        if (TryGetMemberValue(instance, memberName, out var memberValue)
            && memberValue is byte[] bytes)
        {
            value = bytes;
            return true;
        }

        value = Array.Empty<byte>();
        return false;
    }

    private static bool TryReadUIntField(object instance, string fieldName, out uint value)
    {
        value = 0;
        if (!TryGetFieldValue(instance, fieldName, out var fieldValue))
        {
            return false;
        }

        switch (fieldValue)
        {
            case uint uintValue:
                value = uintValue;
                return true;
            case int intValue when intValue >= 0:
                value = (uint)intValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadDoubleField(object instance, string fieldName, out double value)
    {
        value = 0d;
        if (!TryGetFieldValue(instance, fieldName, out var fieldValue))
        {
            return false;
        }

        switch (fieldValue)
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

    private static bool TryReadFloatTuple(object value, out Vector4 tuple)
    {
        if (value is Vector4 vector)
        {
            tuple = vector;
            return true;
        }

        if (TryReadFloatMember(value, "r", out var r)
            && TryReadFloatMember(value, "g", out var g)
            && TryReadFloatMember(value, "b", out var b)
            && TryReadFloatMember(value, "a", out var a))
        {
            tuple = new Vector4(r, g, b, a);
            return true;
        }

        if (TryReadFloatMember(value, "R", out r)
            && TryReadFloatMember(value, "G", out g)
            && TryReadFloatMember(value, "B", out b)
            && TryReadFloatMember(value, "A", out a))
        {
            tuple = new Vector4(r, g, b, a);
            return true;
        }

        tuple = default;
        return false;
    }

    private static bool TryReadFloatMember(object instance, string memberName, out float value)
    {
        value = 0f;
        if (!TryGetMemberValue(instance, memberName, out var reflectedValue))
        {
            return false;
        }

        switch (reflectedValue)
        {
            case float floatValue:
                value = floatValue;
                return true;
            case double doubleValue:
                value = (float)doubleValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryInvokeBoolMethod(object instance, string methodName, out bool value)
    {
        value = false;
        if (!TryInvokeMethod(instance, methodName, out var methodValue) || methodValue is not bool boolValue)
        {
            return false;
        }

        value = boolValue;
        return true;
    }

    private static bool TryInvokeMethod(object instance, string methodName, out object? value)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethod(
                methodName,
                MemberFlags | BindingFlags.DeclaredOnly,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (method == null)
            {
                continue;
            }

            value = method.Invoke(instance, null);
            return true;
        }

        value = null;
        return false;
    }

    private static TextureSamplingMode ConvertSamplingMode(object? samplingMode)
    {
        if (samplingMode is int intValue)
        {
            return intValue == 0 ? TextureSamplingMode.Nearest : TextureSamplingMode.Linear;
        }

        return string.Equals(samplingMode?.ToString(), "NearestNeighbor", StringComparison.Ordinal)
            ? TextureSamplingMode.Nearest
            : TextureSamplingMode.Linear;
    }

    private static bool IsImplicitInputBrush(object brush)
    {
        return TypeNameEndsWith(brush, "ImplicitInputBrush");
    }

    private static bool IsShaderEffectLike(object instance)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            if (type.Name.Equals("ShaderEffect", StringComparison.Ordinal)
                || (type.FullName?.EndsWith(".ShaderEffect", StringComparison.Ordinal) ?? false))
            {
                return true;
            }
        }

        return TryGetPropertyValue(instance, "PixelShader", out _);
    }

    private static bool TypeNameEndsWith(object instance, string suffix)
    {
        var type = instance.GetType();
        return type.Name.EndsWith(suffix, StringComparison.Ordinal)
            || (type.FullName?.EndsWith("." + suffix, StringComparison.Ordinal) ?? false);
    }
}
