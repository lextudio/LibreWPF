using System.Reflection;
using System.Text;
using PrintingTools.Core;
using Xunit;

namespace LibreWPF.SystemPrinting.Tests;

/// <summary>
/// The XML the converter writes, checked as text. Reading it back through PrintCapabilities/PrintTicket needs the real
/// ReachFramework, which this default-context test host cannot load; those round trips run in the PrintingTools harness.
/// </summary>
public class PrintSchemaTests
{
    private static readonly Type Schema = typeof(System.Printing.PrintQueue).Assembly.GetType("System.Printing.PrintSchema", throwOnError: true)!;

    private static string Xml(string method, params object[] args)
    {
        var stream = (MemoryStream)Schema.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static PrintingTools.Core.PrintCapabilities Capabilities(params PageMediaSizeInfo[] sizes) => new(
        sizes,
        new[] { PageOrientation.Portrait, PageOrientation.Landscape },
        DuplexingSupport.LongEdge,
        new[] { ColorMode.Color, ColorMode.Monochrome },
        new[] { 1, 2, 3 });

    [Fact]
    public void CapabilitiesListSizesByKeywordInMicronsAndSkipSizesWithoutOne()
    {
        var xml = Xml("CapabilitiesXml", Capabilities(
            new PageMediaSizeInfo(CommonPageMediaSizes.A4, isDefault: true),
            new PageMediaSizeInfo(CommonPageMediaSizes.Letter),
            new PageMediaSizeInfo(new PageMediaSize("Oficio", 612d, 936d))));

        Assert.Contains("name=\"psk:ISOA4\"", xml);
        Assert.Contains("name=\"psk:NorthAmericaLetter\"", xml);
        Assert.DoesNotContain("Oficio", xml); // no Print Schema keyword: left out, not mislabelled
        Assert.Contains(">209903<", xml); // 595 pt in microns
        Assert.Contains(">297039<", xml); // 842 pt
    }

    [Fact]
    public void CapabilitiesCarryOrientationDuplexColorAndTheCopiesDefinitionTheParserNeeds()
    {
        var xml = Xml("CapabilitiesXml", Capabilities(new PageMediaSizeInfo(CommonPageMediaSizes.A4)));

        Assert.Contains("name=\"psk:Landscape\"", xml);
        Assert.Contains("name=\"psk:TwoSidedLongEdge\"", xml);
        Assert.DoesNotContain("TwoSidedShortEdge", xml);
        Assert.Contains("name=\"psk:Monochrome\"", xml);
        // ReachFramework drops a copies definition that lacks any of default, minimum and maximum.
        Assert.Contains("name=\"psk:JobCopiesAllDocuments\"", xml);
        foreach (var property in new[] { "psf:DefaultValue", "psf:MinValue", "psf:MaxValue" })
        {
            Assert.Contains($"name=\"{property}\"", xml);
        }
    }

    [Fact]
    public void TicketCarriesTheChosenOptionsAndCanLeaveOutThePageSize()
    {
        var ticket = new PrintTicketModel(CommonPageMediaSizes.Letter)
        {
            Orientation = PageOrientation.Landscape,
            Duplex = DuplexingMode.TwoSidedShortEdge,
            ColorMode = ColorMode.Monochrome,
            Copies = 4,
        };

        var xml = Xml("TicketXml", ticket, true);
        Assert.Contains("name=\"psk:NorthAmericaLetter\"", xml);
        Assert.Contains("name=\"psk:Landscape\"", xml);
        Assert.Contains("name=\"psk:TwoSidedShortEdge\"", xml);
        Assert.Contains("name=\"psk:Monochrome\"", xml);
        Assert.Contains(">4<", xml);

        var withoutSize = Xml("TicketXml", ticket, false);
        Assert.DoesNotContain("PageMediaSize", withoutSize);
        Assert.Contains("name=\"psk:Landscape\"", withoutSize);
    }

    [Fact]
    public void BSizesHaveNoKeywordBecauseIsoAndJisDiffer()
    {
        var xml = Xml("CapabilitiesXml", Capabilities(new PageMediaSizeInfo(new PageMediaSize("B5", 499d, 709d))));
        Assert.DoesNotContain("JISB5", xml);
        Assert.DoesNotContain("B5", xml);
        Assert.False((bool)Schema.GetMethod("ListsPageSizes", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { Capabilities(new PageMediaSizeInfo(new PageMediaSize("B5", 499d, 709d))) })!);
    }

    [Fact]
    public void PwgStyleNamesFromCupsMapToKeywords()
    {
        var xml = Xml("CapabilitiesXml", Capabilities(
            new PageMediaSizeInfo(new PageMediaSize("iso_a4_210x297mm", 595d, 842d), isDefault: true),
            new PageMediaSizeInfo(new PageMediaSize("na_letter_8.5x11in", 612d, 792d)),
            new PageMediaSizeInfo(new PageMediaSize("na_ledger_11x17in", 792d, 1224d)),
            new PageMediaSizeInfo(new PageMediaSize("custom_min_1x1in", 72d, 72d))));

        Assert.Contains("name=\"psk:ISOA4\"", xml);
        Assert.Contains("name=\"psk:NorthAmericaLetter\"", xml);
        Assert.Contains("name=\"psk:NorthAmericaTabloid\"", xml);
        Assert.DoesNotContain("custom_min", xml);
    }

    [Fact]
    public void ATicketNamesItsPageSizeOnlyWhenThatSizeHasAKeyword()
    {
        var keyworded = Xml("TicketXml", new PrintTicketModel(new PageMediaSize("iso_a4_210x297mm", 595d, 842d)), true);
        Assert.Contains("name=\"psk:ISOA4\"", keyworded);

        // A size without a keyword is not invented as a custom one (the capabilities do not list it either) ...
        var keywordless = Xml("TicketXml", new PrintTicketModel(new PageMediaSize("Oficio", 612d, 936d)), true);
        Assert.DoesNotContain("PageMediaSize", keywordless);
        Assert.DoesNotContain("CustomMediaSize", keywordless);

        // ... and a queue that lists no sizes at all gets none named, even for a size that has a keyword.
        var unlisted = Xml("TicketXml", new PrintTicketModel(CommonPageMediaSizes.Letter), false);
        Assert.DoesNotContain("PageMediaSize", unlisted);
    }

    [Fact]
    public void AutoColorLeavesTheColorFeatureOutOfATicket()
    {
        var xml = Xml("TicketXml", new PrintTicketModel(CommonPageMediaSizes.A4) { ColorMode = ColorMode.Auto }, true);
        Assert.DoesNotContain("PageOutputColor", xml);
    }
}
