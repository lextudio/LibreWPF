using System.Runtime.CompilerServices;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Source connection guards only. Native retained-placement behavior and actual
// source/package execution have separate controls; these do not qualify them.
public sealed class WpfHintedTextReflowWiringTests
{
    [Fact]
    public void HintedParagraphExposesTheExistingSourceContinuationCapability()
    {
        Assert.True(typeof(IPortableReflowTextParagraph).IsAssignableFrom(typeof(WpfHintedTextParagraph)));
    }

    [Fact]
    public void ContinuationUsesOriginalNativeOwnerAndFullSourceWithoutReshaping()
    {
        string source = ReadReflow();
        Assert.Contains("_generation.Resource.Reflow(inputStart, maximumWidth)", source, StringComparison.Ordinal);
        Assert.Contains("Adopt(resource, _generation.SourceText)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Substring(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatHinted(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableTextFormattingService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecursiveAdmissionAndCloseAreCheckedBeforeContinuationPublication()
    {
        string source = ReadReflow();
        int guard = source.IndexOf("if (_reflowing)", StringComparison.Ordinal);
        int begin = source.IndexOf("_reflowing = true;", StringComparison.Ordinal);
        int retire = source.IndexOf("_failedReflow.Dispose();", StringComparison.Ordinal);
        int native = source.IndexOf("_generation.Resource.Reflow(", StringComparison.Ordinal);
        int publish = source.IndexOf("Adopt(resource, _generation.SourceText)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && begin > guard && retire > begin && native > retire && publish > native);
        Assert.Contains("ObjectDisposedException.ThrowIf(IsDisposed, this);", source[retire..native], StringComparison.Ordinal);
        Assert.Contains("ObjectDisposedException.ThrowIf(IsDisposed, this);", source[native..publish], StringComparison.Ordinal);
        Assert.Contains("_failedReflow.Capture(resource);", source, StringComparison.Ordinal);
        Assert.Contains("_failedReflow.DisposePreservingFailure(failure);", source, StringComparison.Ordinal);
        Assert.Contains("finally { _reflowing = false; }", source, StringComparison.Ordinal);
    }

    private static string ReadReflow([CallerFilePath] string testFile = "")
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!,
            "..", "..", "ProGPU.Wpf", "Composition", "WpfHintedTextParagraph.Source.cs"));
        string source = File.ReadAllText(path);
        int start = source.IndexOf("public IPortableTextParagraph Reflow(", StringComparison.Ordinal);
        int end = source.IndexOf("public float GetBaselineOffset(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
