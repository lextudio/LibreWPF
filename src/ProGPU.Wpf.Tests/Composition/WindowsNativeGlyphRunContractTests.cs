using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WindowsNativeGlyphRunContractTests
{
    [Fact]
    public void NativeMilSkipsPortableGlyphRunsBeforeDirectWriteAccess()
    {
        var storagePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "core",
            "glyph",
            "GlyphRunCore.h");
        var drawingContextPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "core",
            "uce",
            "drawingcontext.cpp");

        var storage = File.ReadAllText(storagePath);
        var drawingContext = File.ReadAllText(drawingContextPath);

        Assert.Contains("bool HasDWriteFont() const", storage, StringComparison.Ordinal);
        Assert.Contains("return m_pIDWriteFont != NULL;", storage, StringComparison.Ordinal);

        var nullGlyphRunCheck = drawingContext.IndexOf("if (NULL == pGlyphRun)", StringComparison.Ordinal);
        var portableGlyphRunCheck = drawingContext.IndexOf("if (!pGlyphRun->HasDWriteFont())", StringComparison.Ordinal);
        var directWritePath = drawingContext.IndexOf("pGlyphRun->ShouldUseGeometry", StringComparison.Ordinal);

        Assert.True(nullGlyphRunCheck >= 0);
        Assert.True(portableGlyphRunCheck > nullGlyphRunCheck);
        Assert.True(directWritePath > portableGlyphRunCheck);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
