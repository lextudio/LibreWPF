using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Scene;
using PortableColor = ProGPU.Wpf.Interop.PortableColor;
using PortableBitmapEffectInputSource = ProGPU.Wpf.Interop.IPortableBitmapEffectInputSource;
using PortableEffect = ProGPU.Wpf.Interop.PortableEffect;
using PortableEffectKind = ProGPU.Wpf.Interop.PortableEffectKind;
using PortableEffectSource = ProGPU.Wpf.Interop.IPortableEffectSource;
using PortablePixelShader = ProGPU.Wpf.Interop.PortablePixelShader;
using PortableShaderEffect = ProGPU.Wpf.Interop.PortableShaderEffect;
using PortableShaderEffectSource = ProGPU.Wpf.Interop.IPortableShaderEffectSource;
using PortableShaderSampler = ProGPU.Wpf.Interop.PortableShaderSampler;
using PortableShaderSamplerKind = ProGPU.Wpf.Interop.PortableShaderSamplerKind;
using PortableShaderSamplingMode = ProGPU.Wpf.Interop.PortableShaderSamplingMode;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfEffectMapper
{
    public static bool TryCreateProGpuEffect(
        object? effect,
        out global::ProGPU.Scene.EffectBase proGpuEffect,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        if (effect != null)
        {
            if (effect is PortableEffectSource effectSource
                && effectSource.TryGetPortableEffect(out var portableEffect)
                && TryCreatePortableEffect(portableEffect, out proGpuEffect))
            {
                return true;
            }

            if (effect is PortableShaderEffectSource shaderEffectSource
                && shaderEffectSource.TryGetPortableShaderEffect(out var portableShaderEffect)
                && TryCreatePortableShaderEffect(portableShaderEffect, imageSourceAdapter, out proGpuEffect))
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
        out global::ProGPU.Scene.EffectBase proGpuEffect,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        proGpuEffect = null!;
        if (effect == null || !IsSupportedBitmapEffectInput(effectInput))
        {
            return false;
        }

        return TryCreateProGpuEffect(effect, out proGpuEffect, imageSourceAdapter);
    }

    private static bool TryCreatePortableEffect(PortableEffect effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        switch (effect.Kind)
        {
            case PortableEffectKind.Blur:
                proGpuEffect = new global::ProGPU.Scene.BlurEffect((float)Math.Max(0d, effect.Radius));
                return true;

            case PortableEffectKind.DropShadow:
                var radians = effect.Direction * Math.PI / 180d;
                var offset = new Vector2(
                    (float)(effect.ShadowDepth * Math.Cos(radians)),
                    (float)(-effect.ShadowDepth * Math.Sin(radians)));
                proGpuEffect = new global::ProGPU.Scene.DropShadowEffect(
                    (float)Math.Max(0d, effect.BlurRadius),
                    offset,
                    ToVectorColor(effect.Color, Math.Clamp(effect.Opacity, 0d, 1d)));
                return true;

            default:
                proGpuEffect = null!;
                return false;
        }
    }

    private static bool TryCreatePortableShaderEffect(
        PortableShaderEffect effect,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        proGpuEffect = null!;

        if (!TryResolveShaderReplacement(effect, out var replacement))
        {
            return false;
        }

        if (effect.IntConstantCount > 0 || effect.BoolConstantCount > 0)
        {
            return false;
        }

        if (!TryReadPortableShaderSamplerState(
                effect,
                imageSourceAdapter,
                out var sourceTextureRegisterIndex,
                out var samplingMode,
                out var samplers))
        {
            return false;
        }

        var parameters = new WpfShaderEffectParams
        {
            ShaderSource = replacement.ShaderSource,
            ShaderKey = replacement.ShaderKey,
            Constants = CopyPortableFloatConstants(effect),
            Samplers = samplers,
            SamplingMode = samplingMode,
            SourceTextureRegisterIndex = sourceTextureRegisterIndex
        };

        var nativeEffect = new WpfShaderEffect(parameters)
        {
            Padding = (float)Math.Min(float.MaxValue, Math.Max(0d, effect.MaxPadding))
        };

        proGpuEffect = nativeEffect;
        return true;
    }

    private static bool TryResolveShaderReplacement(
        PortableShaderEffect effect,
        out WpfShaderEffectReplacement replacement)
    {
        replacement = null!;

        foreach (var key in EnumerateShaderReplacementKeys(effect))
        {
            if (WpfShaderEffectRegistry.TryGet(key, out replacement))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateShaderReplacementKeys(PortableShaderEffect effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.EffectTypeFullName))
        {
            yield return effect.EffectTypeFullName!;
        }

        if (!string.IsNullOrWhiteSpace(effect.EffectTypeName))
        {
            yield return effect.EffectTypeName!;
        }

        PortablePixelShader? pixelShader = effect.PixelShader;
        if (pixelShader == null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(pixelShader.UriSource))
        {
            yield return pixelShader.UriSource!;
        }

        if (!string.IsNullOrWhiteSpace(pixelShader.AbsoluteUri))
        {
            yield return pixelShader.AbsoluteUri!;
        }

        if (pixelShader.Bytecode.Length > 0)
        {
            yield return WpfShaderEffectRegistry.CreatePixelShaderBytecodeKey(pixelShader.Bytecode);
        }
    }

    private static float[] CopyPortableFloatConstants(PortableShaderEffect effect)
    {
        if (effect.FloatConstants.Length == 0)
        {
            return Array.Empty<float>();
        }

        var length = Math.Min(effect.FloatConstants.Length, WpfShaderEffectParams.ConstantFloatCount);
        var constants = new float[length];
        Array.Copy(effect.FloatConstants, constants, length);
        return constants;
    }

    private static bool TryReadPortableShaderSamplerState(
        PortableShaderEffect effect,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out int sourceTextureRegisterIndex,
        out TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler[] samplers)
    {
        sourceTextureRegisterIndex = 0;
        samplingMode = TextureSamplingMode.Linear;
        samplers = Array.Empty<WpfShaderEffectSampler>();

        var hasImplicitInput = false;
        List<WpfShaderEffectSampler>? samplerList = null;

        foreach (PortableShaderSampler portableSampler in effect.Samplers)
        {
            var registerIndex = portableSampler.RegisterIndex;
            if ((uint)registerIndex >= WpfShaderEffectParams.MaxSamplerRegisterCount)
            {
                return false;
            }

            var samplerSamplingMode = ConvertSamplingMode(portableSampler.SamplingMode);
            if (portableSampler.Kind == PortableShaderSamplerKind.ImplicitInput)
            {
                if (hasImplicitInput)
                {
                    return false;
                }

                sourceTextureRegisterIndex = registerIndex;
                samplingMode = samplerSamplingMode;
                hasImplicitInput = true;
            }
            else if (portableSampler.Kind == PortableShaderSamplerKind.ImageSource)
            {
                if (TryCreateImageSourceShaderSampler(
                        portableSampler.ImageSource,
                        imageSourceAdapter,
                        registerIndex,
                        samplerSamplingMode,
                        out var imageSampler))
                {
                    samplerList ??= new List<WpfShaderEffectSampler>();
                    samplerList.Add(imageSampler);
                }
                else
                {
                    return false;
                }
            }
            else if (portableSampler.Kind == PortableShaderSamplerKind.Brush
                && portableSampler.Brush != null)
            {
                if (TryCreateShaderSamplerBrush(
                        portableSampler.Brush,
                        imageSourceAdapter,
                        registerIndex,
                        samplerSamplingMode,
                        out var shaderSampler))
                {
                    samplerList ??= new List<WpfShaderEffectSampler>();
                    samplerList.Add(shaderSampler);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        if (samplerList != null)
        {
            foreach (var shaderSampler in samplerList)
            {
                if (shaderSampler.RegisterIndex == sourceTextureRegisterIndex)
                {
                    return false;
                }
            }

            samplers = samplerList.ToArray();
        }

        return true;
    }

    private static bool TryCreateImageSourceShaderSampler(
        object? imageSource,
        IWpfImageSourceAdapter? imageSourceAdapter,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (ResolveImageSource(imageSource, imageSourceAdapter) is MediaBitmapSource bitmapSource
            && bitmapSource.PixelWidth > 0
            && bitmapSource.PixelHeight > 0
            && WpfBitmapSourceImageAdapter.TryGetGpuTexture(bitmapSource, out var texture))
        {
            sampler = new WpfShaderEffectSampler(registerIndex, texture, samplingMode);
            return true;
        }

        sampler = null!;
        return false;
    }

    private static bool TryCreateShaderSamplerBrush(
        object brush,
        IWpfImageSourceAdapter? imageSourceAdapter,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (imageSourceAdapter is IWpfShaderEffectSamplerBrushAdapter samplerBrushAdapter
            && samplerBrushAdapter.TryAdaptShaderEffectSamplerBrush(
                brush,
                registerIndex,
                samplingMode,
                out sampler))
        {
            return true;
        }

        sampler = null!;
        return false;
    }

    private static MediaImageSource? ResolveImageSource(
        object? imageSource,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        return imageSource is MediaImageSource mediaImageSource
            ? mediaImageSource
            : imageSourceAdapter?.AdaptImageSource(imageSource);
    }

    private static bool IsSupportedBitmapEffectInput(object? effectInput)
    {
        if (effectInput == null)
        {
            return true;
        }

        return effectInput is PortableBitmapEffectInputSource inputSource
            && inputSource.TryGetPortableBitmapEffectInput(out var input)
            && input.UsesContextInput
            && input.HasDefaultAreaToApplyEffect;
    }

    private static Vector4 ToVectorColor(PortableColor color, double opacity)
    {
        return ToVectorColor(color.A, color.R, color.G, color.B, opacity);
    }

    private static Vector4 ToVectorColor(byte a, byte r, byte g, byte b, double opacity)
    {
        return new Vector4(
            r / 255f,
            g / 255f,
            b / 255f,
            (float)((a / 255d) * opacity));
    }

    private static TextureSamplingMode ConvertSamplingMode(PortableShaderSamplingMode samplingMode)
    {
        return samplingMode == PortableShaderSamplingMode.NearestNeighbor
            ? TextureSamplingMode.Nearest
            : TextureSamplingMode.Linear;
    }
}
