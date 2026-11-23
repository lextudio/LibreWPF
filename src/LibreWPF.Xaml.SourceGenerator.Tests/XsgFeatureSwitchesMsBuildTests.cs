using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// The MSBuild side of the XSG switches (the SDK's XamlSourceGeneratorFeatures.targets), evaluated by the real MSBuild.
///
///   UseLibreWpfXamlSourceGenerator   the overall switch: off = the BAML compiler, exactly upstream WPF;
///   LibreWpfSimplerXaml, LibreWpfCSharpExpressions   features of the XSG path, each following the overall switch unless set.
///
/// The three project kinds:  1. XSG off, old-style XAML (BAML);  2. XSG on, old-style XAML;  3. XSG on, new-style XAML.
/// </summary>
public class XsgFeatureSwitchesMsBuildTests
{
    private static readonly string s_targets = Path.Combine(AppContext.BaseDirectory, "XsgFeatures.targets");

    private sealed record Evaluation(string? Overall, string? SimplerXaml, string? CSharpExpressions, string[] CompilerVisible, string Warnings);

    /// <summary>Evaluates a project that sets <paramref name="properties"/> and then imports the targets, as a project using LibreWPF.Sdk does.</summary>
    private static Evaluation Evaluate(params (string Name, string Value)[] properties)
    {
        var directory = Directory.CreateTempSubdirectory("XsgSwitches-");
        try
        {
            var project = Path.Combine(directory.FullName, "Probe.proj");
            var body = string.Concat(properties.Select(p => $"    <{p.Name}>{p.Value}</{p.Name}>\n"));
            File.WriteAllText(project, $"""
                <Project>
                  <PropertyGroup>
                {body}  </PropertyGroup>
                  <Import Project="{s_targets}" />
                </Project>
                """);

            using var json = JsonDocument.Parse(RunMsBuild(project, "-nologo", "-getProperty:UseLibreWpfXamlSourceGenerator,LibreWpfSimplerXaml,LibreWpfCSharpExpressions", "-getItem:CompilerVisibleProperty").Output);
            var props = json.RootElement.GetProperty("Properties");
            string? Get(string name) => props.TryGetProperty(name, out var v) ? v.GetString() : null;
            var items = json.RootElement.GetProperty("Items").TryGetProperty("CompilerVisibleProperty", out var list)
                ? list.EnumerateArray().Select(i => i.GetProperty("Identity").GetString()!).ToArray()
                : [];

            // The warning target is what the build runs before CoreCompile; run it by name.
            var warnings = RunMsBuild(project, "-nologo", "-t:_LibreWpfXsgFeatureChecks", "-v:minimal").Output;
            return new Evaluation(Get("UseLibreWpfXamlSourceGenerator"), Get("LibreWpfSimplerXaml"), Get("LibreWpfCSharpExpressions"), items, warnings);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static (int ExitCode, string Output) RunMsBuild(string project, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var text = output.Result + error.Result;
        Assert.True(process.ExitCode == 0, "msbuild failed: " + text);
        return (process.ExitCode, text);
    }

    // ---- 1: BAML ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ABamlProject_HasTheOverallSwitchOffAndEveryFeatureOff_AndNoWarning()
    {
        var e = Evaluate();

        Assert.Equal("false", e.Overall);
        Assert.Equal("false", e.SimplerXaml);
        Assert.Equal("false", e.CSharpExpressions);
        Assert.DoesNotContain("LWXSG5001", e.Warnings);
    }

    // ---- 2 and 3: XSG ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheOverallSwitchAlone_TurnsEveryFeatureOn()
    {
        var e = Evaluate(("UseLibreWpfXamlSourceGenerator", "true"));

        Assert.Equal("true", e.Overall);
        Assert.Equal("true", e.SimplerXaml);
        Assert.Equal("true", e.CSharpExpressions);
        Assert.DoesNotContain("LWXSG5001", e.Warnings);
    }

    [Theory]
    [InlineData("false", "true", "false", "true")]
    [InlineData("true", "false", "true", "false")]
    [InlineData("false", "false", "false", "false")]
    [InlineData("true", "true", "true", "true")]
    public void AFeatureCanBeTurnedOffOrOnIndependentlyOfTheOtherOnes(string simpler, string csharp, string expectSimpler, string expectCSharp)
    {
        var e = Evaluate(("UseLibreWpfXamlSourceGenerator", "true"), ("LibreWpfSimplerXaml", simpler), ("LibreWpfCSharpExpressions", csharp));

        Assert.Equal(expectSimpler, e.SimplerXaml);
        Assert.Equal(expectCSharp, e.CSharpExpressions);
        Assert.DoesNotContain("LWXSG5001", e.Warnings);
    }

    [Fact]
    public void AValueIsKeptAsTheProjectWroteIt()
    {
        var e = Evaluate(("UseLibreWpfXamlSourceGenerator", "true"), ("LibreWpfSimplerXaml", "False"));

        Assert.Equal("False", e.SimplerXaml);
    }

    [Fact]
    public void TheSwitchesReachTheGeneratorAsCompilerVisibleProperties()
    {
        var e = Evaluate(("UseLibreWpfXamlSourceGenerator", "true"));

        Assert.Contains("UseLibreWpfXamlSourceGenerator", e.CompilerVisible);
        Assert.Contains("LibreWpfSimplerXaml", e.CompilerVisible);
        Assert.Contains("LibreWpfCSharpExpressions", e.CompilerVisible);
    }

    // ---- a feature switch without the overall switch ---------------------------------------------------------------------------

    [Theory]
    [InlineData("LibreWpfSimplerXaml")]
    [InlineData("LibreWpfCSharpExpressions")]
    public void AFeatureSetToTrueWhileTheOverallSwitchIsOff_IsReportedAsDoingNothing(string feature)
    {
        var e = Evaluate((feature, "true"));

        Assert.Equal("false", e.Overall);
        Assert.Contains("LWXSG5001", e.Warnings);
        Assert.Contains(feature + "=true has no effect", e.Warnings);
    }

    [Theory]
    [InlineData("LibreWpfSimplerXaml")]
    [InlineData("LibreWpfCSharpExpressions")]
    public void AFeatureSetToFalseWhileTheOverallSwitchIsOff_IsNotAWarning(string feature)
    {
        var e = Evaluate((feature, "false"));

        Assert.DoesNotContain("LWXSG5001", e.Warnings);
    }

    [Fact]
    public void BothFeaturesSetWithoutTheOverallSwitch_AreBothReported()
    {
        var e = Evaluate(("LibreWpfSimplerXaml", "true"), ("LibreWpfCSharpExpressions", "true"));

        Assert.Contains("LibreWpfSimplerXaml=true has no effect", e.Warnings);
        Assert.Contains("LibreWpfCSharpExpressions=true has no effect", e.Warnings);
    }

    [Fact]
    public void TheTargetsFileIsTheOneThePackageShips()
    {
        // The test evaluates a copy of packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets, which the SDK package
        // ships whole (PackagingContent Include="targets\*") and Sdk/Sdk.targets imports.
        Assert.True(File.Exists(s_targets));
        var sdkTargets = File.ReadAllText(FindInRepository("packaging", "ProGPU.Wpf.Sdk", "Sdk", "Sdk.targets"));
        Assert.Contains("ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets", sdkTargets);
        Assert.Equal(File.ReadAllText(FindInRepository("packaging", "ProGPU.Wpf.Sdk", "targets", "ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets")),
            File.ReadAllText(s_targets));
    }

    private static string FindInRepository(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
