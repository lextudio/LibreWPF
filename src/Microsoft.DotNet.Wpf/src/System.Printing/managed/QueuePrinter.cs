using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintingTools.Core;
using PrintingTools.Wpf;

namespace System.Printing;

/// <summary>
/// Prints WPF content to a queue: every page is rendered to a bitmap with <see cref="RenderTargetBitmap"/>, the bitmaps become a
/// PDF and the backend's <see cref="IPrintDocumentSubmitter"/> sends it. This is what the CUPS backend needs (its queues take
/// PDF); a backend without a submitter (the Win32 spooler here) is refused before anything is rendered.
/// </summary>
internal static class QueuePrinter
{
    private const double DipsPerInch = 96d;
    private const double PdfDpi = 200d;
    private const double DipsPerPoint = DipsPerInch / 72d;

    internal static void Print(PrintQueue queue, object content)
    {
        if (PrintBackend.Adapter is not IPrintDocumentSubmitter submitter)
        {
            throw new PlatformNotSupportedException("This print backend cannot submit a prepared document to a queue.");
        }

        var pageSize = DefaultPageSize(queue);
        var pages = Pages(content, pageSize);
        if (pages.Count == 0)
        {
            throw new System.Windows.Xps.XpsWriterException("There is nothing to print: the content has no pages.");
        }

        var pdf = new RasterPdfWriter();
        foreach (var (visual, size) in pages)
        {
            var width = Math.Max(1, (int)Math.Ceiling(size.Width * PdfDpi / DipsPerInch));
            var height = Math.Max(1, (int)Math.Ceiling(size.Height * PdfDpi / DipsPerInch));
            var bitmap = new RenderTargetBitmap(width, height, PdfDpi, PdfDpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            pdf.AddPage(size.Width * 72d / DipsPerInch, size.Height * 72d / DipsPerInch, width, height, pixels);
        }

        submitter.SubmitDocumentAsync(queue.Info.Id, pdf.ToArray(), "application/pdf", "Document").GetAwaiter().GetResult();
    }

    /// <summary>The queue's default page in device-independent units; content that has no page of its own is placed on it.</summary>
    private static Size DefaultPageSize(PrintQueue queue)
    {
        var capabilities = PrintBackend.Adapter.GetCapabilitiesAsync(queue.Info.Id, null).GetAwaiter().GetResult();
        var size = capabilities.GetDefaultPageMediaSize() ?? CommonPageMediaSizes.Letter;
        return new Size(size.Width * DipsPerPoint, size.Height * DipsPerPoint);
    }

    private static List<(Visual Visual, Size Size)> Pages(object content, Size defaultPage)
    {
        switch (content)
        {
            case FixedPage fixedPage:
                var size = new Size(fixedPage.Width > 0 ? fixedPage.Width : defaultPage.Width, fixedPage.Height > 0 ? fixedPage.Height : defaultPage.Height);
                fixedPage.Measure(size);
                fixedPage.Arrange(new Rect(size));
                fixedPage.UpdateLayout();
                return new() { (fixedPage, size) };
            case DocumentPaginator paginator:
                return FromPaginator(paginator, defaultPage);
            case IDocumentPaginatorSource source:
                return FromPaginator(source.DocumentPaginator, defaultPage);
            case Visual visual:
                if (visual is UIElement { IsMeasureValid: false } or UIElement { IsArrangeValid: false })
                {
                    // Like a FixedPage: an element that was never laid out draws nothing. Measure it against the page and put it at the
                    // page's origin at its desired size (arranging it into the whole page would centre a fixed-size element in it).
                    var element = (UIElement)visual;
                    element.Measure(defaultPage);
                    element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
                    element.UpdateLayout();
                }

                return new() { (visual, defaultPage) };
            default:
                throw new NotSupportedException($"{content.GetType().Name} cannot be written to a print queue.");
        }
    }

    private static List<(Visual Visual, Size Size)> FromPaginator(DocumentPaginator paginator, Size defaultPage)
    {
        var pages = new List<(Visual, Size)>();
        if (!paginator.IsPageCountValid)
        {
            paginator.ComputePageCount();
        }

        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            if (page == DocumentPage.Missing || page.Visual is null)
            {
                continue;
            }

            // A page with no usable size of its own is placed on the queue's default page.
            var size = page.Size;
            pages.Add((page.Visual, size.IsEmpty || !(size.Width > 0) || !(size.Height > 0) || double.IsInfinity(size.Width) || double.IsInfinity(size.Height) ? defaultPage : size));
        }

        return pages;
    }
}
