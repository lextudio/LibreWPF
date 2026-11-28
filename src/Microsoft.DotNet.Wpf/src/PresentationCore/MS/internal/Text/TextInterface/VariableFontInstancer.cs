// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Text.TextInterface
{
    /// <summary>
    /// Exposes the weights of a variable font as static font files.
    /// </summary>
    /// <remarks>
    /// The portable text pipeline shapes, measures and renders from font bytes and has no per-run
    /// variation state, so a variable font would otherwise render every weight as its default
    /// instance. A weight instance is generated once (by the registered
    /// <see cref="IPortableVariableFontInstancer"/>), cached on disk, and exposed as a normal font
    /// face (see <see cref="PortableFontData"/>). This type owns the weight set, the cache location
    /// and the file handling; the font engine lives in ProGPU.Wpf.
    /// </remarks>
    internal static class VariableFontInstancer
    {
        // Bump when the generator output changes so stale cache files are not reused.
        private const int GeneratorVersion = 1;

        internal static readonly int[] StandardWeights = { 100, 200, 300, 400, 500, 600, 700, 800, 900 };

        private static readonly object s_generationLock = new object();

        internal static string GetFaceName(int weight) => weight switch
        {
            <= 100 => "Thin",
            <= 200 => "ExtraLight",
            <= 300 => "Light",
            <= 400 => "Regular",
            <= 500 => "Medium",
            <= 600 => "SemiBold",
            <= 700 => "Bold",
            <= 800 => "ExtraBold",
            _ => "Black",
        };

        /// <summary>
        /// Reads the weight axis of a variable font file. False when the font has none, or when no
        /// instancer is registered yet (callers must not cache that answer).
        /// </summary>
        internal static bool TryGetWeightRange(string path, out int minimum, out int maximum, out int defaultWeight, out bool serviceAvailable)
        {
            minimum = maximum = defaultWeight = 0;
            serviceAvailable = PortableWpfServiceRegistry.TryGetVariableFontInstancer(out IPortableVariableFontInstancer instancer);
            return serviceAvailable && instancer.TryGetWeightRange(path, out minimum, out maximum, out defaultWeight);
        }

        /// <summary>The weight in <see cref="StandardWeights"/> nearest to <paramref name="requested"/> within the axis range.</summary>
        internal static int SnapWeight(int requested, int minimum, int maximum)
        {
            int best = 0;
            int bestDistance = int.MaxValue;
            foreach (int weight in StandardWeights)
            {
                if (weight < minimum || weight > maximum)
                {
                    continue;
                }

                int distance = Math.Abs(weight - requested);
                if (distance < bestDistance)
                {
                    best = weight;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// The instance weight to use for <paramref name="requested"/>, or 0 when the face itself is already
        /// right (it is the default instance and reports that weight) or no standard weight is in range.
        /// </summary>
        internal static int SelectInstanceWeight(int requested, int minimum, int maximum, int axisDefault, int faceWeight)
        {
            int weight = SnapWeight(requested, minimum, maximum);
            return weight == 0 || weight == axisDefault && faceWeight == axisDefault ? 0 : weight;
        }

        internal static string GetCachePath(string sourcePath, int weight)
        {
            // A source that has gone missing must not throw here: the key only needs to be stable, and the
            // failure surfaces (and is handled) when the instance is generated.
            var info = new FileInfo(sourcePath);
            long length = info.Exists ? info.Length : 0;
            long modified = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            string key = string.Concat(
                GeneratorVersion.ToString(CultureInfo.InvariantCulture), "|",
                info.FullName, "|",
                length.ToString(CultureInfo.InvariantCulture), "|",
                modified.ToString(CultureInfo.InvariantCulture), "|",
                weight.ToString(CultureInfo.InvariantCulture));
            string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 16);
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(GetCacheDirectory(), $"{name}-w{weight}-{hash}.ttf");
        }

        /// <summary>Creates the weight instance file if it is not already cached.</summary>
        internal static void EnsureInstance(string sourcePath, int weight, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                return;
            }

            lock (s_generationLock)
            {
                if (File.Exists(destinationPath))
                {
                    return;
                }

                if (!PortableWpfServiceRegistry.TryGetVariableFontInstancer(out IPortableVariableFontInstancer instancer))
                {
                    throw new InvalidOperationException("No variable font instancer is registered.");
                }

                byte[] instance = instancer.CreateWeightInstance(File.ReadAllBytes(sourcePath), weight);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                string temporaryPath = destinationPath + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
                File.WriteAllBytes(temporaryPath, instance);
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
        }

        private static string GetCacheDirectory()
        {
            string overrideDirectory = Environment.GetEnvironmentVariable("LIBREWPF_FONT_INSTANCE_CACHE");
            if (!string.IsNullOrEmpty(overrideDirectory))
            {
                return overrideDirectory;
            }

            string home = Environment.GetEnvironmentVariable("HOME");
            if (OperatingSystem.IsMacOS() && !string.IsNullOrEmpty(home))
            {
                return Path.Combine(home, "Library", "Caches", "LibreWPF", "FontInstances");
            }

            return Path.Combine(Path.GetTempPath(), "LibreWPF", "FontInstances");
        }
    }
}
