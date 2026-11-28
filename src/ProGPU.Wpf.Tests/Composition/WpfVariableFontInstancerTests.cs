using System.Buffers.Binary;
using MS.Internal.Text.TextInterface;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfVariableFontInstancerTests
{
    private static readonly string InterVariablePath = FindRepoPath(
        "external", "ProGPU", "src", "ProGPU.Fonts.Inter", "Fonts", "InterVariable.ttf");

    [Fact]
    public void ReportsTheWeightAxisOfAVariableFont()
    {
        var instancer = new System.Windows.Media.ProGPU.Composition.WpfVariableFontInstancer();

        Assert.True(instancer.TryGetWeightRange(InterVariablePath, out int minimum, out int maximum, out int defaultWeight));
        Assert.Equal(100, minimum);
        Assert.Equal(900, maximum);
        Assert.Equal(400, defaultWeight);
    }

    [Fact]
    public void StaticFontHasNoWeightAxis()
    {
        var instancer = new System.Windows.Media.ProGPU.Composition.WpfVariableFontInstancer();
        string staticPath = FindRepoPath("external", "ProGPU", "src", "ProGPU.Fonts.Inter", "Fonts", "Inter-Regular.ttf");

        Assert.False(instancer.TryGetWeightRange(staticPath, out _, out _, out _));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(600)]
    [InlineData(800)]
    public void InstanceDrawsAndSpacesLikeTheVariableFontAtThatWeight(int weight)
    {
        var instancer = new System.Windows.Media.ProGPU.Composition.WpfVariableFontInstancer();
        byte[] source = File.ReadAllBytes(InterVariablePath);

        byte[] staticBytes = instancer.CreateWeightInstance(source, weight);

        var variable = new TtfFont(source, 0).WithVariations(new FontVariationSetting("wght", weight));
        var instance = new TtfFont(staticBytes, 0);

        Assert.False(instance.IsVariableFont);
        Assert.Equal(variable.NumGlyphs, instance.NumGlyphs);

        foreach (char character in "Hamburgefonstiv0123456789%.")
        {
            ushort glyph = variable.GetGlyphIndex(character);
            Assert.NotEqual((ushort)0, glyph);
            Assert.Equal(glyph, instance.GetGlyphIndex(character));

            float expectedAdvance = variable.GetAdvanceWidth(glyph, instance.UnitsPerEm);
            float actualAdvance = instance.GetAdvanceWidth(glyph, instance.UnitsPerEm);
            Assert.InRange(actualAdvance, expectedAdvance - 1.01f, expectedAdvance + 1.01f);

            Assert.True(variable.TryGetGlyphBounds(glyph, out short expectedXMin, out short expectedYMin, out short expectedXMax, out short expectedYMax));
            Assert.True(instance.TryGetGlyphBounds(glyph, out short actualXMin, out short actualYMin, out short actualXMax, out short actualYMax));
            Assert.InRange(actualXMin, expectedXMin - 1, expectedXMin + 1);
            Assert.InRange(actualXMax, expectedXMax - 1, expectedXMax + 1);
            Assert.InRange(actualYMin, expectedYMin - 1, expectedYMin + 1);
            Assert.InRange(actualYMax, expectedYMax - 1, expectedYMax + 1);
        }
    }

    [Fact]
    public void HeavierInstancesAreWiderAndCarryTheirWeightClass()
    {
        var instancer = new System.Windows.Media.ProGPU.Composition.WpfVariableFontInstancer();
        byte[] source = File.ReadAllBytes(InterVariablePath);

        var light = new TtfFont(instancer.CreateWeightInstance(source, 300), 0);
        var bold = new TtfFont(instancer.CreateWeightInstance(source, 800), 0);
        ushort glyph = light.GetGlyphIndex('H');

        Assert.True(bold.GetAdvanceWidth(glyph, light.UnitsPerEm) > light.GetAdvanceWidth(glyph, light.UnitsPerEm));

        Assert.True(bold.TryGetTable("OS/2", out ReadOnlyMemory<byte> os2));
        Assert.Equal(800, BinaryPrimitives.ReadUInt16BigEndian(os2.Span[4..]));
        Assert.False(bold.TryGetTable("fvar", out _));
        Assert.False(bold.TryGetTable("gvar", out _));
    }

    [Fact]
    public void NonVariableFontCannotBeInstanced()
    {
        var instancer = new System.Windows.Media.ProGPU.Composition.WpfVariableFontInstancer();
        byte[] staticFont = File.ReadAllBytes(FindRepoPath("external", "ProGPU", "src", "ProGPU.Fonts.Inter", "Fonts", "Inter-Regular.ttf"));

        Assert.Throws<NotSupportedException>(() => instancer.CreateWeightInstance(staticFont, 700));
    }

    [Fact]
    public void FaceSelectionRoutesCharacterMappingThroughTheWeightInstance()
    {
        string textInterface = File.ReadAllText(FindRepoPath(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "MS", "internal", "Text", "TextInterface", "PortableTextInterface.cs"));
        string physicalFamily = File.ReadAllText(FindRepoPath(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "MS", "internal", "FontFace", "PhysicalFontFamily.cs"));

        Assert.Contains("internal Font ApplyVariableWeight(Font font, FontWeight weight)", textInterface, StringComparison.Ordinal);
        Assert.Contains("return ApplyVariableWeight(bestFont, weight);", textInterface, StringComparison.Ordinal);

        // A failed instance build must degrade to the default instance, not fail text layout.
        Assert.Contains("instanceFont = null;", textInterface, StringComparison.Ordinal);
        Assert.Contains("return instanceFont ?? font;", textInterface, StringComparison.Ordinal);
        Assert.Contains("instanceData = font.FontData.GetWeightInstance((int)weight);", textInterface, StringComparison.Ordinal);

        // Text formatting picks faces in MapGlyphTypeface, not GetFirstMatchingFont; both must
        // return the weight instance or a variable font renders every weight as its default.
        Assert.Equal(2, CountOccurrences(physicalFamily, "_family.ApplyVariableWeight("));
    }

    [Theory]
    [InlineData(450, 1, 1000, 400)]
    [InlineData(500, 1, 1000, 500)]
    [InlineData(50, 1, 1000, 100)]
    [InlineData(1000, 1, 1000, 900)]
    [InlineData(250, 295, 900, 300)]
    [InlineData(950, 295, 900, 900)]
    public void WeightsSnapToTheNearestStandardWeightInTheAxisRange(int requested, int minimum, int maximum, int expected)
    {
        Assert.Equal(expected, VariableFontInstancer.SnapWeight(requested, minimum, maximum));
    }

    [Fact]
    public void NoStandardWeightInRangeSelectsNothing()
    {
        Assert.Equal(0, VariableFontInstancer.SnapWeight(400, 410, 490));
        Assert.Equal(0, VariableFontInstancer.SelectInstanceWeight(400, 410, 490, 450, 450));
    }

    [Theory]
    // SF Pro: default instance is wght 400 and the file reports 400, so Regular reuses the file.
    [InlineData(400, 1, 1000, 400, 400, 0)]
    [InlineData(700, 1, 1000, 400, 400, 700)]
    [InlineData(300, 1, 1000, 400, 400, 300)]
    // SF Mono: the default is wght 295 (off the 100-step grid), so Regular and Light need instances.
    [InlineData(300, 295, 900, 295, 295, 300)]
    [InlineData(400, 295, 900, 295, 295, 400)]
    [InlineData(100, 295, 900, 295, 295, 300)]
    // A file whose OS/2 weight differs from the axis default must not be reused for the default weight.
    [InlineData(400, 1, 1000, 400, 300, 400)]
    public void InstanceWeightIsOnlyReusedWhenTheFileIsThatWeight(
        int requested, int minimum, int maximum, int axisDefault, int faceWeight, int expected)
    {
        Assert.Equal(expected, VariableFontInstancer.SelectInstanceWeight(requested, minimum, maximum, axisDefault, faceWeight));
    }

    [Fact]
    public void CachePathIsStableDistinctPerWeightAndToleratesAMissingSource()
    {
        string missing = Path.Combine(Path.GetTempPath(), "librewpf-missing-" + Guid.NewGuid().ToString("N") + ".ttf");
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "librewpf-cache-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("LIBREWPF_FONT_INSTANCE_CACHE");
        Environment.SetEnvironmentVariable("LIBREWPF_FONT_INSTANCE_CACHE", cacheDirectory);
        try
        {
            string regular = VariableFontInstancer.GetCachePath(missing, 400);
            Assert.Equal(regular, VariableFontInstancer.GetCachePath(missing, 400));
            Assert.NotEqual(regular, VariableFontInstancer.GetCachePath(missing, 600));
            Assert.StartsWith(cacheDirectory, regular, StringComparison.Ordinal);
            Assert.EndsWith(".ttf", regular, StringComparison.Ordinal);

            string real = VariableFontInstancer.GetCachePath(InterVariablePath, 600);
            Assert.NotEqual(VariableFontInstancer.GetCachePath(missing, 600), real);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIBREWPF_FONT_INSTANCE_CACHE", previous);
        }
    }

    [Fact]
    public void EnsureInstanceWritesOnceThroughTheRegisteredInstancerAndFailsWithoutOne()
    {
        string directory = Path.Combine(Path.GetTempPath(), "librewpf-instance-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(directory, "instance.ttf");
        var service = new CountingInstancer();
        try
        {
            using (PortableWpfServiceRegistry.RegisterVariableFontInstancer(service))
            {
                VariableFontInstancer.EnsureInstance(InterVariablePath, 600, destination);
                VariableFontInstancer.EnsureInstance(InterVariablePath, 600, destination);
            }

            Assert.Equal(1, service.Calls);
            Assert.Equal(600, service.LastWeight);
            Assert.Equal(new byte[] { 0, 1, 0, 0 }, File.ReadAllBytes(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            // Another test in this process may already have installed the real default instancer.
            if (!PortableWpfServiceRegistry.TryGetVariableFontInstancer(out _))
            {
                string other = Path.Combine(directory, "other.ttf");
                Assert.Throws<InvalidOperationException>(() => VariableFontInstancer.EnsureInstance(InterVariablePath, 700, other));
                Assert.False(File.Exists(other));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class CountingInstancer : IPortableVariableFontInstancer
    {
        public int Calls { get; private set; }

        public int LastWeight { get; private set; }

        public bool TryGetWeightRange(string fontPath, out int minimum, out int maximum, out int defaultWeight)
        {
            minimum = maximum = defaultWeight = 0;
            return false;
        }

        public byte[] CreateWeightInstance(byte[] fontData, int weight)
        {
            Calls++;
            LastWeight = weight;
            return new byte[] { 0, 1, 0, 0 };
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepoPath(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
