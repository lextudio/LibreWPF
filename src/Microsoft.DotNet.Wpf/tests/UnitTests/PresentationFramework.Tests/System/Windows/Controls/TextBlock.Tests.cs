// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Windows.Documents;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows.Controls;

public sealed class TextBlockTests
{
    private const string CjkText = "文件"; // U+6587 U+4EF6

    // A Windows-MIL test process must not switch its frozen resource domain, and a
    // non-portable host has no ProGPU paragraph to fall back to for Display mode.
    private sealed class PortableMediaFactAttribute : FactAttribute
    {
        public PortableMediaFactAttribute([CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires a process initialized with portable media before WPF construction.";
        }
    }

    // Characterization fixture for a known defect, not desired behavior.
    //
    // PortableTextLine declines every TextFormattingMode other than Ideal, so Display
    // falls through to SimpleTextLine. That path measures Latin correctly but reports a
    // zero width for CJK, so a CJK run lays out at zero extent and a
    // Display-mode host such as a menu item collapses. This pins the current behavior so
    // it cannot change silently; the Display == 0 expectations are expected to flip when
    // the Display path is fixed.
    [PortableMediaFact]
    public void TextFormattingModeDisplay_MeasuresCjkAsZeroWidthInAFaceThatHasTheGlyphs()
    {
        // The face is verified to contain both CJK glyphs, so a zero width below is the
        // measurement path failing rather than the font lacking coverage.
        FontFamily family = FindCjkCapableFamily();

        Size latinIdeal = Measure(family, "abc", TextFormattingMode.Ideal);
        Size latinDisplay = Measure(family, "abc", TextFormattingMode.Display);
        Size cjkDisplay = Measure(family, CjkText, TextFormattingMode.Display);

        // Controls: Latin measures correctly in both modes from this same face, so Display
        // is a working mode here and the failure below is specific to CJK.
        Assert.True(latinIdeal.Width > 0, $"Ideal Latin width was {latinIdeal.Width}.");
        Assert.True(latinDisplay.Width > 0, $"Display Latin width was {latinDisplay.Width}.");

        // The defect: the CJK run collapses to zero width in Display, so a Display-mode
        // host lays the text out at no extent at all.
        Assert.Equal(0, cjkDisplay.Width);
    }

    private static Size Measure(FontFamily family, string text, TextFormattingMode mode)
    {
        TextBlock textBlock = new() { Text = text, FontSize = 24, FontFamily = family };
        TextOptions.SetTextFormattingMode(textBlock, mode);
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        textBlock.Arrange(new Rect(textBlock.DesiredSize));
        return textBlock.DesiredSize;
    }

    // A physical face that really contains the CJK glyphs, so the fixture never measures
    // fallback or .notdef behavior instead of Display-mode measurement.
    private static FontFamily FindCjkCapableFamily()
    {
        foreach (FontFamily family in Fonts.SystemFontFamilies)
        {
            // Dot-prefixed names are private to the font collection and are not usable as
            // an explicit FontFamily source, so they cannot stand in for a real face here.
            if (family.Source.StartsWith('.') || family.Source.Contains(','))
            {
                continue;
            }

            try
            {
                foreach (Typeface typeface in family.GetTypefaces())
                {
                    if (typeface.TryGetGlyphTypeface(out GlyphTypeface face) &&
                        HasGlyph(face, CjkText[0]) && HasGlyph(face, CjkText[1]))
                    {
                        return new FontFamily(family.Source);
                    }
                }
            }
            catch (Exception)
            {
                // Unusable family; keep probing the remaining installed families.
            }
        }

        Assert.Skip("No installed font contains the CJK glyphs used by this fixture.");
        return null!;
    }

    private static bool HasGlyph(GlyphTypeface face, char character) =>
        face.CharacterToGlyphMap.TryGetValue(character, out ushort glyph) && glyph != 0;
    [Fact]
    public void PortableRtlFinalInsertionUsesPhysicalParagraphEdge()
    {
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new TextProvider());
        TextBlock textBlock = new()
        {
            Text = "English 1234 — שלום עולם — العربية 5678 — mixed direction text",
            FlowDirection = FlowDirection.RightToLeft,
            TextWrapping = TextWrapping.Wrap,
            Width = 300,
        };

        textBlock.Measure(new Size(300, double.PositiveInfinity));
        textBlock.Arrange(new Rect(textBlock.DesiredSize));

        TextPointer finalPosition = textBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;
        Rect finalCaret = finalPosition.GetCharacterRect(LogicalDirection.Forward);
        TextPointer previousPosition = finalPosition.GetNextInsertionPosition(LogicalDirection.Backward)!;
        Rect previousCaret = previousPosition.GetCharacterRect(LogicalDirection.Forward);

        Assert.Equal(0, finalCaret.X);
        Assert.Equal(5, previousCaret.X);
    }

    [Fact]
    public void GetRectangles_MultilineContent_UsesPrecedingLineHeights()
    {
        TextBlock textBlock = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Width = 200,
        };
        Hyperlink firstLink = new(new Run("First")) { FontSize = 48 };
        Hyperlink secondLink = new(new Run("Second")) { FontSize = 12 };
        Hyperlink thirdLink = new(new Run("Third")) { FontSize = 12 };
        textBlock.Inlines.Add(firstLink);
        textBlock.Inlines.Add(new LineBreak());
        textBlock.Inlines.Add(secondLink);
        textBlock.Inlines.Add(new LineBreak());
        textBlock.Inlines.Add(thirdLink);

        textBlock.Measure(new Size(200, double.PositiveInfinity));
        textBlock.Arrange(new Rect(textBlock.DesiredSize));

        IContentHost contentHost = (IContentHost)textBlock;
        Rect first = Assert.Single(contentHost.GetRectangles(firstLink));
        Rect second = Assert.Single(contentHost.GetRectangles(secondLink));
        Rect third = Assert.Single(contentHost.GetRectangles(thirdLink));

        Assert.True(second.Top >= first.Bottom);
        Assert.True(third.Top >= second.Bottom);
    }

    private sealed class TextProvider : IPortableTextFormatting
    {
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request) => new Paragraph(request);
    }

    private sealed class Paragraph : IPortableTextParagraph
    {
        internal Paragraph(in PortableTextParagraphRequest request)
        {
            var glyphs = new PortableTextGlyph[request.Text.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                glyphs[i] = new(0, i, i + 1, i * 5, 0, 5, 0);
            }

            Glyphs = glyphs;
            Lines = new PortableTextLineInfo[]
            {
                new(0, glyphs.Length, 0, glyphs.Length, glyphs.Length * 5, 0, request.LineHeight),
            };
        }

        public ReadOnlyMemory<PortableTextGlyph> Glyphs { get; }
        public ReadOnlyMemory<PortableTextLineInfo> Lines { get; }
        public PortableTextHit HitTest(int lineIndex, float distance) => new(Math.Clamp((int)(distance / 5), 0, Glyphs.Length), false);
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => hit.Position * 5;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => Math.Clamp(position + (previous ? -1 : 1), 0, Glyphs.Length);
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        {
            if (end <= start)
            {
                return 0;
            }

            rectangles[0] = new(start * 5, 0, (end - start) * 5, Lines.Span[0].Height);
            return 1;
        }
    }
}
