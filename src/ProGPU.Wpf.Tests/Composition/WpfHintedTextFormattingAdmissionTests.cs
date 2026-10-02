using System.Windows.Media.ProGPU.Composition;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Deliberately invalid font bytes prove these request-policy checks complete
// before native font construction. No accepted hinted generation is fabricated.
public sealed class WpfHintedTextFormattingAdmissionTests
{
    private static readonly PortableTextStyleMetrics[] Metrics = [new(9, 3)];
    private static readonly PortableTextHintingStyle[] Devices =
        [new(12 * 64, 12 * 64, PortableTextHintInterpreter.TrueType40)];
    private static readonly PortableHintedTextOptions Options =
        new(1, PortableHintedTextProjection.ScalarReference, PortableHintedTextCoverage.NonzeroVector);

    [Fact]
    public void ProviderExposesHintedFormattingOnlyThroughItsSeparateCapability()
    {
        IPortableTextFormatting provider = new WpfPortableTextFormatting();
        Assert.IsAssignableFrom<IPortableHintedTextFormatting>(provider);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MissingOriginalTextOrStylePartitionFailsBeforeFontConstruction(int invalid)
    {
        var request = CreateRequest();
        var metrics = Metrics;
        var devices = Devices;
        switch (invalid)
        {
            case 0: request = request with { Text = ReadOnlyMemory<char>.Empty }; break;
            case 1: request = request with { Styles = ReadOnlyMemory<PortableTextStyle>.Empty }; break;
            case 2: metrics = []; break;
            case 3: devices = []; break;
        }

        var provider = new WpfPortableTextFormatting();
        Assert.Throws<ArgumentException>(() => provider.FormatHinted(request, metrics, devices, Options));
        Assert.Throws<ArgumentException>(() => provider.FormatHintedWithNominalMetrics(request, metrics, devices, Options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void UnsupportedSourcePolicyCannotReachOrdinaryFormatting(int invalid)
    {
        var request = CreateRequest();
        request = invalid switch
        {
            0 => request with { Features = new PortableTextFeature[] { new(0x6c696761, 1) } },
            1 => request with { IncrementalTab = 24 },
            2 => request with { TabOrigin = 2 },
            3 => request with { MeasureIntrinsicWidths = true },
            4 => request with { Wrapping = PortableTextWrapping.WholeWord },
            5 => CreateRequest("a\t"),
            6 => CreateRequest("a\ufffc"),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        var provider = new WpfPortableTextFormatting();
        Assert.Throws<NotSupportedException>(() => provider.FormatHinted(request, Metrics, Devices, Options));
        Assert.Throws<NotSupportedException>(() => provider.FormatHintedWithNominalMetrics(request, Metrics, Devices, Options));
    }

    [Fact]
    public void NominalSourcePreparationRejectsVariableInstanceBeforeNativeFontConstruction()
    {
        var request = CreateRequest();
        PortableTextHintingStyle[] devices = [Devices[0] with { VariationCount = 1 }];
        Assert.Throws<NotSupportedException>(() => new WpfPortableTextFormatting().FormatHintedWithNominalMetrics(request, Metrics, devices, Options));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.Epsilon)]
    public void InvalidDpiFailsBeforeFontConstruction(float dpi)
    {
        var request = CreateRequest();
        var options = Options with { DpiScale = dpi };
        var provider = new WpfPortableTextFormatting();

        Assert.Throws<ArgumentOutOfRangeException>(() => provider.FormatHinted(request, Metrics, Devices, options));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownExecutionPolicyFailsBeforeFontConstruction(bool projection)
    {
        var request = CreateRequest();
        var options = projection
            ? Options with { Projection = (PortableHintedTextProjection)int.MaxValue }
            : Options with { Coverage = (PortableHintedTextCoverage)int.MaxValue };
        var provider = new WpfPortableTextFormatting();

        Assert.Throws<ArgumentOutOfRangeException>(() => provider.FormatHinted(request, Metrics, Devices, options));
    }

    private static PortableTextParagraphRequest CreateRequest(string text = "ab")
    {
        var font = new PortableTextFont(new byte[] { 0xa5 }, 0, 1024);
        return new(text.AsMemory(), font, 12, 12, 120, false, PortableTextAlignment.Left,
            Styles: new PortableTextStyle[] { new(0, text.Length, font, 12) });
    }
}
