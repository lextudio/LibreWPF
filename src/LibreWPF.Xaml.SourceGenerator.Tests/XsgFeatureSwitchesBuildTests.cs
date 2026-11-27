using System.Diagnostics;
using LibreWPF.Xaml.SourceGenerator;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// The whole chain, with a real <c>dotnet build</c>: MSBuild properties -> XamlSourceGeneratorFeatures.targets (CompilerVisibleProperty) ->
/// the compiler's generated .editorconfig (build_property.*) -> the generator. The probe project does not reference WPF, so the generated
/// code itself does not compile; what is asserted is whether the generator's own diagnostics (LWXSG0003, LWXSG0004) appear.
/// </summary>
public class XsgFeatureSwitchesBuildTests
{
    private const string NewStyleWithExpression = """
        <Window x:Class="Sample.MainWindow">
          <Grid>
            <TextBlock x:Name="Status" Text="{1 + 1}" />
          </Grid>
        </Window>
        """;

    private const string OldStyleWithExpression = """
        <Window x:Class="Sample.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <TextBlock x:Name="Status" Text="{1 + 1}" />
          </Grid>
        </Window>
        """;

    private static string Build(string xaml, string properties)
    {
        var directory = Directory.CreateTempSubdirectory("XsgBuild-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "MainWindow.xaml"), xaml);
            File.WriteAllText(Path.Combine(directory.FullName, "MainWindow.cs"), "namespace Sample { public partial class MainWindow { } }");
            var project = Path.Combine(directory.FullName, "Probe.csproj");
            File.WriteAllText(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <NuGetAudit>false</NuGetAudit>
                    <!-- The probe names the generator as an Analyzer itself: no package to reference. -->
                    <LibreWpfXsgAutoReference>false</LibreWpfXsgAutoReference>
                    {properties}
                  </PropertyGroup>
                  <ItemGroup>
                    <Analyzer Include="{typeof(XamlSourceGenerator).Assembly.Location}" />
                    <AdditionalFiles Include="MainWindow.xaml" />
                  </ItemGroup>
                  <Import Project="{Path.Combine(AppContext.BaseDirectory, "XsgFeatures.targets")}" />
                </Project>
                """);

            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = directory.FullName,
            };
            foreach (var argument in new[] { "build", project, "-nologo", "-v:minimal", "-nodeReuse:false" })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return output.Result + error.Result;
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void TheOverallSwitchAlone_LetsANewStyleDocumentWithAnExpressionThrough()
    {
        // Projects of kind 3: XSG on, new-style XAML. Every feature follows the overall switch, so neither diagnostic appears.
        var output = Build(NewStyleWithExpression, "<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>");

        Assert.DoesNotContain("LWXSG0003", output);
        Assert.DoesNotContain("LWXSG0004", output);
        // The generator ran and emitted Sample_MainWindow.g.cs, which this probe cannot compile because it has no WPF: the errors are in it.
        Assert.Contains("Sample_MainWindow.g.cs", output);
    }

    [Fact]
    public void SimplerXamlOff_ReachesTheGeneratorAndRefusesANewStyleDocument()
    {
        var output = Build(NewStyleWithExpression, """
            <UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>
            <LibreWpfSimplerXaml>false</LibreWpfSimplerXaml>
            """);

        Assert.Contains("LWXSG0003", output);
        Assert.DoesNotContain("Sample_MainWindow.g.cs", output);
    }

    [Fact]
    public void CSharpExpressionsOff_ReachesTheGeneratorAndRefusesTheExpression()
    {
        var output = Build(OldStyleWithExpression, """
            <UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>
            <LibreWpfCSharpExpressions>false</LibreWpfCSharpExpressions>
            """);

        Assert.Contains("LWXSG0004", output);
        Assert.DoesNotContain("LWXSG0003", output);
        Assert.DoesNotContain("Sample_MainWindow.g.cs", output);
    }

    [Fact]
    public void AnOldStyleDocumentNeedsNeitherFeature()
    {
        // Projects of kind 2: XSG on, old-style XAML. Simpler XAML off changes nothing for a document that declares its namespaces.
        var output = Build(OldStyleWithExpression, """
            <UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>
            <LibreWpfSimplerXaml>false</LibreWpfSimplerXaml>
            """);

        Assert.DoesNotContain("LWXSG0003", output);
        Assert.DoesNotContain("LWXSG0004", output);
        Assert.Contains("Sample_MainWindow.g.cs", output);
    }

    [Fact]
    public void WithTheOverallSwitchOff_FeatureSwitchesAreOffForTheGenerator()
    {
        // A BAML project does not feed XAML to the generator; if someone does anyway, the features are off, as the targets define them.
        var output = Build(NewStyleWithExpression, "");

        Assert.Contains("LWXSG0003", output);
    }
}
