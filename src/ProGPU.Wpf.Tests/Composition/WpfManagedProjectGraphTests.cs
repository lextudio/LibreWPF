using System.Xml.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfManagedProjectGraphTests
{
    [Theory]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/ReachFramework/ReachFramework.csproj")]
    public void DirectWriteForwarderReferenceIsWindowsOnly(string relativeProjectPath)
    {
        var projectPath = FindRepoPath(relativeProjectPath.Split('/'));
        var document = XDocument.Load(projectPath);

        var directWriteReferences = document
            .Descendants("ProjectReference")
            .Where(reference =>
            {
                var include = reference.Attribute("Include")?.Value.Replace('/', '\\');
                return include?.EndsWith(@"DirectWriteForwarder\DirectWriteForwarder.vcxproj", StringComparison.OrdinalIgnoreCase) == true;
            })
            .ToArray();

        var reference = Assert.Single(directWriteReferences);

        Assert.Equal("'$(OS)' == 'Windows_NT'", reference.Attribute("Condition")?.Value);
        Assert.Equal("TargetFramework;TargetFrameworks", reference.Element("UndefineProperties")?.Value);
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
