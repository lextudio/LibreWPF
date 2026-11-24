// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using System.Windows.Markup;

// The theme information WPF reads for an assembly's generic dictionary: "themes/generic.xaml" in the assembly itself.
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

namespace System.Windows;

/// <summary>
/// The seam between WPF's theme lookup (SystemResources) and the registry of dictionaries compiled by the XAML source generator. It drives
/// the real ResourceDictionaries class, the one WPF uses per assembly, so the two phases below are what an application sees: with nothing
/// registered the lookup takes the unchanged BAML path, and with a generated generic dictionary registered under the assembly's name it
/// is found (without BAML).
/// </summary>
public class SystemResourcesGeneratedDictionaryTests
{
    private sealed class Probe { }

    [Fact]
    public void TheGenericDictionary_FallsBackToBamlWhenNothingIsRegistered_ThenUsesTheRegisteredOne()
    {
        var assembly = typeof(Probe).Assembly;

        // Phase 1, every ordinary application: nothing registered. The lookup must not throw and must find no dictionary (this
        // assembly has no BAML either), i.e. take the old path unchanged.
        new SystemResources.ResourceDictionaries(assembly).LoadGenericDictionary(isTraceEnabled: false)
            .Should().BeNull();

        // Phase 2, an XSG-compiled library: its module initializer registers the generic dictionary under the assembly's name.
        // A new ResourceDictionaries instance (WPF keeps one per assembly and caches the result of the first lookup).
        XamlSourceGeneratedResources.RegisterDictionary(assembly, "themes/generic.xaml", () => new ResourceDictionary { [typeof(Probe)] = "generated" });

        var dictionary = new SystemResources.ResourceDictionaries(assembly).LoadGenericDictionary(isTraceEnabled: false);

        dictionary.Should().NotBeNull();
        dictionary[typeof(Probe)].Should().Be("generated");
    }

    private static (ResourceDictionary? Dictionary, object?[] Args) LoadByName(string assemblyName, string resourceName)
    {
        // The assembly could not be loaded (NativeAOT: a theme assembly has no metadata), so only its name is known: assembly == null.
        var method = typeof(SystemResources.ResourceDictionaries).GetMethod("LoadDictionaryOrGenerated",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var instance = new SystemResources.ResourceDictionaries(typeof(Probe).Assembly);
        var args = new object?[] { null, assemblyName, resourceName, false, null };
        var result = (ResourceDictionary?)method.Invoke(instance, args);
        return (result, args);
    }

    [Fact]
    public void ByName_WhenTheAssemblyCannotBeLoaded_TheRegisteredDictionaryIsFound()
    {
        const string name = "Test.NativeAot.Theme";
        XamlSourceGeneratedResources.RegisterDictionary(name, "themes/byname.xaml", () => new ResourceDictionary { ["k"] = "by name" });

        var (dictionary, args) = LoadByName(name, "themes/byname");

        dictionary.Should().NotBeNull();
        dictionary!["k"].Should().Be("by name");
        args[4].Should().BeNull("a generated dictionary has no BAML source URI");
    }

    [Fact]
    public void ByName_WhenNothingIsRegistered_AndNoAssembly_TheAnswerIsNoDictionary_WithoutThrowing()
    {
        var (dictionary, _) = LoadByName("Test.NativeAot.Unregistered", "themes/none");

        dictionary.Should().BeNull();
    }
}
