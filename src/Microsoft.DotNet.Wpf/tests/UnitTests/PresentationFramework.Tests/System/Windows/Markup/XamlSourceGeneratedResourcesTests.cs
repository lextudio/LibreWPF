// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.Windows.Markup;

/// <summary>
/// The registry of resource dictionaries compiled by the XAML source generator. Every WPF theme lookup asks it first (see
/// SystemResources), so what matters most for stock (JIT, BAML) applications is that an EMPTY registry answers "no" without
/// side effects, which makes the lookup fall through to the assembly's BAML exactly as before.
/// </summary>
public class XamlSourceGeneratedResourcesTests
{
    // Names are unique per test: the registry is process-wide.
    private static string UniqueName([System.Runtime.CompilerServices.CallerMemberName] string test = "") => "Registry." + test;

    [Fact]
    public void AnEmptyRegistry_AnswersNoForAnAssemblyName()
    {
        XamlSourceGeneratedResources.TryCreateDictionary(UniqueName(), "themes/aero2.normalcolor.xaml", out var dictionary)
            .Should().BeFalse();
        dictionary.Should().BeNull();
    }

    [Fact]
    public void AnEmptyRegistry_AnswersNoForAnAssembly_AndDoesNotThrow()
    {
        // The path a stock application takes for every theme assembly it loads: nothing registered, so the BAML path continues.
        // Asking twice must be as harmless as asking once (the first call runs the assembly's module constructor).
        Assembly assembly = typeof(XamlSourceGeneratedResourcesTests).Assembly;

        XamlSourceGeneratedResources.TryCreateDictionary(assembly, "themes/not-registered.xaml", out var first).Should().BeFalse();
        XamlSourceGeneratedResources.TryCreateDictionary(assembly, "themes/not-registered.xaml", out var second).Should().BeFalse();

        first.Should().BeNull();
        second.Should().BeNull();
    }

    [Fact]
    public void ARegisteredDictionary_IsCreatedByAssemblyNameAndPath()
    {
        string name = UniqueName();
        XamlSourceGeneratedResources.RegisterDictionary(name, "Themes/Aero2.NormalColor.xaml", () => new ResourceDictionary { ["key"] = "value" });

        XamlSourceGeneratedResources.TryCreateDictionary(name, "Themes/Aero2.NormalColor.xaml", out var dictionary).Should().BeTrue();

        dictionary["key"].Should().Be("value");
    }

    [Theory]
    [InlineData("themes/aero2.normalcolor.xaml")]
    [InlineData("THEMES/AERO2.NORMALCOLOR.XAML")]
    [InlineData("/themes/aero2.normalcolor.xaml")]
    [InlineData("themes\\aero2.normalcolor.xaml")]
    public void ThePathIsMatchedWithoutRegardToCaseLeadingSlashOrSeparatorStyle(string lookup)
    {
        string name = UniqueName() + lookup.GetHashCode();
        XamlSourceGeneratedResources.RegisterDictionary(name, "Themes/Aero2.NormalColor.xaml", () => new ResourceDictionary());

        XamlSourceGeneratedResources.TryCreateDictionary(name, lookup, out var dictionary).Should().BeTrue();
        dictionary.Should().NotBeNull();
    }

    [Fact]
    public void TheAssemblyNameIsMatchedWithoutRegardToCase()
    {
        string name = UniqueName();
        XamlSourceGeneratedResources.RegisterDictionary(name.ToUpperInvariant(), "a.xaml", () => new ResourceDictionary());

        XamlSourceGeneratedResources.TryCreateDictionary(name.ToLowerInvariant(), "a.xaml", out _).Should().BeTrue();
    }

    [Fact]
    public void EveryLookupCreatesANewDictionary()
    {
        // A dictionary belongs to one theme load; sharing an instance across lookups would share mutable state.
        string name = UniqueName();
        XamlSourceGeneratedResources.RegisterDictionary(name, "a.xaml", () => new ResourceDictionary());

        XamlSourceGeneratedResources.TryCreateDictionary(name, "a.xaml", out var first).Should().BeTrue();
        XamlSourceGeneratedResources.TryCreateDictionary(name, "a.xaml", out var second).Should().BeTrue();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void ADictionaryOfAnotherPathOrAssemblyIsNotFound()
    {
        string name = UniqueName();
        XamlSourceGeneratedResources.RegisterDictionary(name, "a.xaml", () => new ResourceDictionary());

        XamlSourceGeneratedResources.TryCreateDictionary(name, "b.xaml", out _).Should().BeFalse();
        XamlSourceGeneratedResources.TryCreateDictionary(name + ".Other", "a.xaml", out _).Should().BeFalse();
    }

    [Fact]
    public void TheLastRegistrationForAPathWins()
    {
        string name = UniqueName();
        XamlSourceGeneratedResources.RegisterDictionary(name, "a.xaml", () => new ResourceDictionary { ["v"] = 1 });
        XamlSourceGeneratedResources.RegisterDictionary(name, "a.xaml", () => new ResourceDictionary { ["v"] = 2 });

        XamlSourceGeneratedResources.TryCreateDictionary(name, "a.xaml", out var dictionary).Should().BeTrue();

        dictionary["v"].Should().Be(2);
    }

    [Fact]
    public void ARegistrationMadeForAnAssemblyObjectIsFoundByThatAssembly()
    {
        // The generator registers a theme under the compiling assembly (or, for a stand-in theme, under another assembly's name).
        Assembly assembly = typeof(XamlSourceGeneratedResourcesTests).Assembly;
        XamlSourceGeneratedResources.RegisterDictionary(assembly, "themes/own.xaml", () => new ResourceDictionary { ["own"] = true });

        XamlSourceGeneratedResources.TryCreateDictionary(assembly, "themes/own.xaml", out var dictionary).Should().BeTrue();

        dictionary["own"].Should().Be(true);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Action nullName = () => XamlSourceGeneratedResources.TryCreateDictionary((string)null, "a.xaml", out _);
        Action nullPath = () => XamlSourceGeneratedResources.TryCreateDictionary("n", null, out _);
        Action nullFactory = () => XamlSourceGeneratedResources.RegisterDictionary("n", "a.xaml", null);

        nullName.Should().Throw<ArgumentNullException>();
        nullPath.Should().Throw<ArgumentNullException>();
        nullFactory.Should().Throw<ArgumentNullException>();
    }

    // ---- components: the root objects of XAML files, created by resource path (Application.StartupUri, LoadComponent) ------------------------

    // The pack:// scheme is registered by System.IO.Packaging: in an application that has happened long before LoadComponent runs.
    private static Uri Pack(string text)
    {
        _ = System.IO.Packaging.PackUriHelper.Create(new Uri("application:///", UriKind.Absolute));   // its static constructor registers the scheme
        return new Uri(text, UriKind.RelativeOrAbsolute);
    }

    [Fact]
    public void AComponentThatWasNeverRegistered_IsNotCreated()
    {
        XamlSourceGeneratedResources.TryCreateComponent(new Uri("/" + UniqueName() + "/never.xaml", UriKind.Relative), out var component).Should().BeFalse();
        component.Should().BeNull();
    }

    [Theory]
    [InlineData("pack://application:,,,/{0};component/Views/Page.xaml")]
    [InlineData("pack://application:,,,/{0};v1.2.3.4;component/views/page.xaml")]   // a version in the assembly part, and the path in any case
    [InlineData("/{0};component/Views/Page.xaml")]                                  // as Application.LoadComponent passes it
    public void ARegisteredComponent_IsCreatedByAssemblyAndPath(string uriTemplate)
    {
        string assembly = UniqueName();
        object made = new object();
        XamlSourceGeneratedResources.RegisterComponent(assembly, "views/page.xaml", () => made);

        XamlSourceGeneratedResources.TryCreateComponent(Pack(string.Format(uriTemplate, assembly)), out var component).Should().BeTrue();

        component.Should().BeSameAs(made);
    }

    [Fact]
    public void ARegisteredComponent_IsANewInstanceEveryTime()
    {
        string assembly = UniqueName();
        XamlSourceGeneratedResources.RegisterComponent(assembly, "w.xaml", () => new object());
        var uri = new Uri("/" + assembly + ";component/w.xaml", UriKind.Relative);

        XamlSourceGeneratedResources.TryCreateComponent(uri, out var first);
        XamlSourceGeneratedResources.TryCreateComponent(uri, out var second);

        first.Should().NotBeNull();
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void ARelativeUri_FindsTheAssemblyThatRegisteredThatPath()
    {
        // Application.StartupUri="MainWindow.xaml" has no assembly in it: the entry assembly first, then whoever registered the path.
        string path = UniqueName().ToLowerInvariant() + "/startup.xaml";
        object made = new object();
        XamlSourceGeneratedResources.RegisterComponent(UniqueName(), path, () => made);

        XamlSourceGeneratedResources.TryCreateComponent(Pack("pack://application:,,,/" + path), out var component).Should().BeTrue();

        component.Should().BeSameAs(made);
    }

    [Fact]
    public void ARelativeUriRegisteredByTwoAssemblies_IsNotGuessed()
    {
        // Two libraries registering "shared.xaml": the result must not depend on dictionary enumeration order, so neither is chosen.
        string path = UniqueName().ToLowerInvariant() + "/shared.xaml";
        XamlSourceGeneratedResources.RegisterComponent(UniqueName() + ".A", path, () => new object());
        XamlSourceGeneratedResources.RegisterComponent(UniqueName() + ".B", path, () => new object());

        XamlSourceGeneratedResources.TryCreateComponent(Pack("pack://application:,,,/" + path), out var component).Should().BeFalse();

        component.Should().BeNull();
    }

    [Fact]
    public void AnExplicitAssemblyThatRegisteredNothing_DoesNotFindAnotherAssembliesComponent()
    {
        string registered = UniqueName() + ".Registered";
        XamlSourceGeneratedResources.RegisterComponent(registered, "main.xaml", () => new object());

        // "Other" names an assembly: only that assembly's registrations count, not any assembly that has the same path.
        XamlSourceGeneratedResources.TryCreateComponent(Pack("pack://application:,,,/" + UniqueName() + ".Other;component/main.xaml"), out var component).Should().BeFalse();

        component.Should().BeNull();
    }

    [Fact]
    public void AFactoryThatReturnsNull_IsReportedAsThat()
    {
        string assembly = UniqueName();
        XamlSourceGeneratedResources.RegisterComponent(assembly, "broken.xaml", () => null);

        Action create = () => XamlSourceGeneratedResources.TryCreateComponent(new Uri("/" + assembly + ";component/broken.xaml", UriKind.Relative), out _);

        create.Should().Throw<InvalidOperationException>().WithMessage("*returned null*");
    }

    [Fact]
    public void LoadComponent_CreatesARegisteredComponent_WithoutAnyBamlResource()
    {
        string assembly = UniqueName();
        object made = new object();
        XamlSourceGeneratedResources.RegisterComponent(assembly, "main.xaml", () => made);

        // The same call that BAML applications use for StartupUri and navigation: with a registered component it needs no resource.
        Application.LoadComponent(new Uri("/" + assembly + ";component/main.xaml", UriKind.Relative)).Should().BeSameAs(made);
    }

    [Fact]
    public void LoadComponent_OfAnUnregisteredUri_StillTakesTheResourcePath()
    {
        // The registry says no, so the BAML path runs exactly as before: this assembly has no such resource.
        Action load = () => Application.LoadComponent(new Uri("/" + UniqueName() + ";component/missing.xaml", UriKind.Relative));

        load.Should().Throw<Exception>().And.Should().NotBeOfType<NullReferenceException>();
    }
}
