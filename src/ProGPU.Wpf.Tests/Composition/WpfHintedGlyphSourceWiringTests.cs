using System.Runtime.CompilerServices;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Placement/publication guards supplement, not replace, a source-core build.
public sealed class WpfHintedGlyphSourceWiringTests
{
    [Fact]
    public void HintedInkReturnBelongsToInkMethodAndCaretMethodsRemainGated()
    {
        string source = ReadGlyphRun();
        string ink = Between(source, "public Rect ComputeInkBoundingBox()", "private double AdjustAdvanceForDisplayLayout");
        Assert.Contains("PortableRect hinted = _portableHintedGlyphRun.BaselineRelativeInkBounds;", ink, StringComparison.Ordinal);
        Assert.Contains("return hinted.IsEmpty ? Rect.Empty", ink, StringComparison.Ordinal);
        string[] boundaries = ["public double GetDistanceFromCaretCharacterHit", "public CharacterHit GetCaretCharacterHitFromDistance",
            "public CharacterHit GetNextCaretCharacterHit", "public CharacterHit GetPreviousCaretCharacterHit", "#endregion Public Methods"];
        for (int i = 0; i < boundaries.Length - 1; i++)
        {
            string caret = Between(source, boundaries[i], boundaries[i + 1]);
            Assert.Contains("CheckPortableHintedCaretAdmission();", caret, StringComparison.Ordinal);
            Assert.DoesNotContain("return hinted.IsEmpty", caret, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HintedPublicationReadsPotentiallyThrowingInkBeforePublishingOwner()
    {
        string publication = Between(ReadGlyphRun(), "internal void InitializePortableHintedGlyphRun", "private void CheckPortableHintedCaretAdmission");
        int read = publication.IndexOf("PortableRect ink = binding.InkBounds;", StringComparison.Ordinal);
        int publish = publication.IndexOf("_portableHintedGlyphRun = binding;", StringComparison.Ordinal);
        Assert.True(read >= 0 && publish > read);
        Assert.DoesNotContain("_portableInkBoundsCache = binding.InkBounds", publication, StringComparison.Ordinal);
    }

    private static string Between(string source, string first, string next)
    {
        int start = source.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0, first);
        int end = source.IndexOf(next, start + first.Length, StringComparison.Ordinal);
        Assert.True(end > start, next);
        return source[start..end];
    }

    private static string ReadGlyphRun([CallerFilePath] string testFile = "") => File.ReadAllText(Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(testFile)!, "..", "..", "..", "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore",
        "System", "Windows", "Media", "GlyphRun.cs")));
}
