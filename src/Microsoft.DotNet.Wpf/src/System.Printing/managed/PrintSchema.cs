using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Core = PrintingTools.Core;

namespace System.Printing;

/// <summary>
/// Converts between PrintingTools' capability/ticket models and Print Schema XML (the format
/// <see cref="PrintCapabilities"/> and <see cref="PrintTicket"/> read), so that queues of any backend can offer them.
/// Page sizes without a Print Schema keyword are left out of the capabilities document.
/// </summary>
internal static class PrintSchema
{
    private const string Psf = "http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";
    private const string Psk = "http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords";
    private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema";

    private const double MicronsPerPoint = 25400d / 72d;
    private const double DipsPerPoint = 96d / 72d;

    // Core page-size names -> Print Schema keywords (only unambiguous ones: ISO B and JIS B sheets differ, so B sizes have none).
    private static readonly Dictionary<string, string> MediaKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Letter"] = "NorthAmericaLetter",
        ["Legal"] = "NorthAmericaLegal",
        ["Tabloid"] = "NorthAmericaTabloid",
        ["A3"] = "ISOA3",
        ["A4"] = "ISOA4",
        ["A5"] = "ISOA5",
        ["A6"] = "ISOA6",
        ["Executive"] = "NorthAmericaExecutive",
    };

    // PWG 5101.1 self-describing names ("iso_a4_210x297mm", "na_letter_8.5x11in", as CUPS and the macOS adapter report them):
    // the "<class>_<name>" prefix, without the dimensions, maps to a keyword.
    private static readonly Dictionary<string, string> PwgKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["na_letter"] = "NorthAmericaLetter",
        ["na_legal"] = "NorthAmericaLegal",
        ["na_ledger"] = "NorthAmericaTabloid",
        ["na_executive"] = "NorthAmericaExecutive",
        ["iso_a3"] = "ISOA3",
        ["iso_a4"] = "ISOA4",
        ["iso_a5"] = "ISOA5",
        ["iso_a6"] = "ISOA6",
    };

    /// <summary>The Print Schema keyword of a page size name, or null when it has no unambiguous one.</summary>
    internal static string? KeywordFor(string name)
    {
        if (MediaKeywords.TryGetValue(name, out var keyword))
        {
            return keyword;
        }

        var parts = name.Split('_');
        return parts.Length >= 2 && PwgKeywords.TryGetValue(parts[0] + "_" + parts[1], out keyword) ? keyword : null;
    }

    /// <summary>
    /// Whether the capabilities document lists any page size. Sizes without a Print Schema keyword are left out of it, so a ticket
    /// must use this same test to decide whether it may name a size at all.
    /// </summary>
    internal static bool ListsPageSizes(Core.PrintCapabilities capabilities) =>
        capabilities.PageMediaSizes.Any(m => KeywordFor(m.Size.Name) is not null);

    internal static MemoryStream CapabilitiesXml(Core.PrintCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return Write("PrintCapabilities", w =>
        {
            var sizes = capabilities.PageMediaSizes.Where(m => KeywordFor(m.Size.Name) is not null).ToList();
            if (sizes.Count > 0)
            {
                Feature(w, "PageMediaSize", () =>
                {
                    foreach (var size in sizes)
                    {
                        w.WriteStartElement("psf", "Option", Psf);
                        w.WriteAttributeString("name", "psk:" + KeywordFor(size.Size.Name));
                        MediaSizeProperties(w, size.Size);
                        w.WriteEndElement();
                    }
                });
            }

            Feature(w, "PageOrientation", () =>
            {
                foreach (var orientation in capabilities.Orientations)
                {
                    Option(w, orientation == Core.PageOrientation.Landscape ? "Landscape" : "Portrait");
                }
            });

            Feature(w, "JobDuplexAllDocumentsContiguously", () =>
            {
                Option(w, "OneSided");
                if (capabilities.Duplexing.HasFlag(Core.DuplexingSupport.LongEdge))
                {
                    Option(w, "TwoSidedLongEdge");
                }

                if (capabilities.Duplexing.HasFlag(Core.DuplexingSupport.ShortEdge))
                {
                    Option(w, "TwoSidedShortEdge");
                }
            });

            var colors = capabilities.ColorModes.Where(c => c != Core.ColorMode.Auto).ToList();
            if (colors.Count > 0)
            {
                Feature(w, "PageOutputColor", () =>
                {
                    foreach (var color in colors)
                    {
                        Option(w, color == Core.ColorMode.Color ? "Color" : "Monochrome");
                    }
                });
            }

            if (capabilities.SupportedCopyCounts.Count > 0)
            {
                w.WriteStartElement("psf", "ParameterDef", Psf);
                w.WriteAttributeString("name", "psk:JobCopiesAllDocuments");
                Property(w, "psf:DataType", "xsd:QName", "xsd:integer");
                // The parser accepts the definition only with all three of default, minimum and maximum.
                Property(w, "psf:DefaultValue", "xsd:integer", capabilities.GetDefaultCopies().ToString(CultureInfo.InvariantCulture));
                Property(w, "psf:MinValue", "xsd:integer", capabilities.SupportedCopyCounts.Min().ToString(CultureInfo.InvariantCulture));
                Property(w, "psf:MaxValue", "xsd:integer", capabilities.SupportedCopyCounts.Max().ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
        });
    }

    /// <param name="includePageSize">
    /// False when the queue reports no page sizes at all: the model still needs a size (Letter) but the ticket must not claim one
    /// that nothing validated.
    /// </param>
    internal static MemoryStream TicketXml(Core.PrintTicketModel ticket, bool includePageSize = true)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return Write("PrintTicket", w =>
        {
            // Only a size that has a keyword can be named; a size without one (or a queue that lists none) is left out
            // rather than invented, and the queue's own default applies.
            if (includePageSize && KeywordFor(ticket.PageMediaSize.Name) is { } keyword)
            {
                Feature(w, "PageMediaSize", () =>
                {
                    w.WriteStartElement("psf", "Option", Psf);
                    w.WriteAttributeString("name", "psk:" + keyword);
                    MediaSizeProperties(w, ticket.PageMediaSize);
                    w.WriteEndElement();
                });
            }

            Feature(w, "PageOrientation", () => Option(w, ticket.Orientation == Core.PageOrientation.Landscape ? "Landscape" : "Portrait"));
            Feature(w, "JobDuplexAllDocumentsContiguously", () => Option(w, ticket.Duplex switch
            {
                Core.DuplexingMode.TwoSidedLongEdge => "TwoSidedLongEdge",
                Core.DuplexingMode.TwoSidedShortEdge => "TwoSidedShortEdge",
                _ => "OneSided",
            }));
            if (ticket.ColorMode != Core.ColorMode.Auto)
            {
                Feature(w, "PageOutputColor", () => Option(w, ticket.ColorMode == Core.ColorMode.Color ? "Color" : "Monochrome"));
            }

            w.WriteStartElement("psf", "ParameterInit", Psf);
            w.WriteAttributeString("name", "psk:JobCopiesAllDocuments");
            Value(w, "xsd:integer", Math.Max(1, ticket.Copies).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
        });
    }

    /// <summary>
    /// The ticket model with every property that <paramref name="ticket"/> sets applied over <paramref name="baseline"/>;
    /// properties the ticket leaves null (or Unknown) keep the baseline's value.
    /// </summary>
    internal static Core.PrintTicketModel Apply(PrintTicket? ticket, Core.PrintTicketModel baseline, Core.PrintCapabilities capabilities)
    {
        var model = baseline.Clone();
        if (ticket is null)
        {
            return model;
        }

        if (ticket.PageMediaSize is { } media)
        {
            // A size the ticket names but this queue does not have (e.g. only a name, no dimensions) must not vanish silently:
            // a placeholder that no queue lists makes the validation replace it and record a warning.
            model.PageMediaSize = FindSize(media, capabilities)
                                  ?? new Core.PageMediaSize(media.PageMediaSizeName?.ToString() ?? "Unspecified", 1d, 1d);
        }

        switch (ticket.PageOrientation)
        {
            case PageOrientation.Landscape or PageOrientation.ReverseLandscape:
                model.Orientation = Core.PageOrientation.Landscape;
                break;
            case PageOrientation.Portrait or PageOrientation.ReversePortrait:
                model.Orientation = Core.PageOrientation.Portrait;
                break;
        }

        switch (ticket.Duplexing)
        {
            case Duplexing.OneSided:
                model.Duplex = Core.DuplexingMode.OneSided;
                break;
            case Duplexing.TwoSidedLongEdge:
                model.Duplex = Core.DuplexingMode.TwoSidedLongEdge;
                break;
            case Duplexing.TwoSidedShortEdge:
                model.Duplex = Core.DuplexingMode.TwoSidedShortEdge;
                break;
        }

        switch (ticket.OutputColor)
        {
            case OutputColor.Color:
                model.ColorMode = Core.ColorMode.Color;
                break;
            case OutputColor.Monochrome or OutputColor.Grayscale:
                model.ColorMode = Core.ColorMode.Monochrome;
                break;
        }

        if (ticket.CopyCount is { } copies && copies > 0)
        {
            model.Copies = copies;
        }

        return model;
    }

    /// <summary>The queue's defaults as a ticket model.</summary>
    internal static Core.PrintTicketModel Defaults(Core.PrintCapabilities capabilities) =>
        new(capabilities.GetDefaultPageMediaSize() ?? Core.CommonPageMediaSizes.Letter)
        {
            Orientation = capabilities.GetDefaultOrientation(),
            ColorMode = capabilities.GetDefaultColorMode(),
            Copies = capabilities.GetDefaultCopies(),
        };

    private static Core.PageMediaSize? FindSize(PageMediaSize media, Core.PrintCapabilities capabilities)
    {
        if (media.PageMediaSizeName is { } name)
        {
            foreach (var info in capabilities.PageMediaSizes)
            {
                if (KeywordFor(info.Size.Name) is { } keyword && keyword.Equals(name.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return info.Size;
                }
            }
        }

        if (media.Width is { } width && media.Height is { } height)
        {
            // Ticket sizes are in 1/96 inch, Core's in points; match with a tolerance of one point.
            foreach (var info in capabilities.PageMediaSizes)
            {
                if (Math.Abs(info.Size.Width * DipsPerPoint - width) <= DipsPerPoint && Math.Abs(info.Size.Height * DipsPerPoint - height) <= DipsPerPoint)
                {
                    return info.Size;
                }
            }

            return new Core.PageMediaSize("Custom", width / DipsPerPoint, height / DipsPerPoint);
        }

        return null;
    }

    private static void MediaSizeProperties(XmlWriter w, Core.PageMediaSize size)
    {
        ScoredProperty(w, "psk:MediaSizeWidth", Math.Round(size.Width * MicronsPerPoint).ToString("0", CultureInfo.InvariantCulture));
        ScoredProperty(w, "psk:MediaSizeHeight", Math.Round(size.Height * MicronsPerPoint).ToString("0", CultureInfo.InvariantCulture));
    }

    private static MemoryStream Write(string root, Action<XmlWriter> body)
    {
        var stream = new MemoryStream();
        using (var w = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, Indent = false }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("psf", root, Psf);
            w.WriteAttributeString("xmlns", "psf", null, Psf);
            w.WriteAttributeString("xmlns", "xsi", null, Xsi);
            w.WriteAttributeString("xmlns", "xsd", null, Xsd);
            w.WriteAttributeString("xmlns", "psk", null, Psk);
            w.WriteAttributeString("version", "1");
            body(w);
            w.WriteEndElement();
            w.WriteEndDocument();
        }

        stream.Position = 0;
        return stream;
    }

    private static void Feature(XmlWriter w, string name, Action options)
    {
        w.WriteStartElement("psf", "Feature", Psf);
        w.WriteAttributeString("name", "psk:" + name);
        options();
        w.WriteEndElement();
    }

    private static void Option(XmlWriter w, string name)
    {
        w.WriteStartElement("psf", "Option", Psf);
        w.WriteAttributeString("name", "psk:" + name);
        w.WriteEndElement();
    }

    private static void ScoredProperty(XmlWriter w, string name, string integer)
    {
        w.WriteStartElement("psf", "ScoredProperty", Psf);
        w.WriteAttributeString("name", name);
        Value(w, "xsd:integer", integer);
        w.WriteEndElement();
    }

    private static void Property(XmlWriter w, string name, string type, string value)
    {
        w.WriteStartElement("psf", "Property", Psf);
        w.WriteAttributeString("name", name);
        Value(w, type, value);
        w.WriteEndElement();
    }

    private static void Value(XmlWriter w, string type, string value)
    {
        w.WriteStartElement("psf", "Value", Psf);
        w.WriteAttributeString("xsi", "type", Xsi, type);
        w.WriteString(value);
        w.WriteEndElement();
    }
}
