using System.Xml.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfManagedProjectGraphTests
{
    [Fact]
    public void FocusedProGpuWpfGraphAvoidsSharedOutputParallelContention()
    {
        var project = XDocument.Load(FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "ProGPU.Wpf.Tests.csproj"));

        Assert.Equal("false", Assert.Single(project.Descendants("BuildInParallel")).Value);
    }

    [Fact]
    public void ProGpuWpfUsesDirectSourceReferencesForConsumedVectorAndTextApis()
    {
        var project = XDocument.Load(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGPU.Wpf.csproj"));
        var projectReferences = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .ToArray();

        Assert.Contains(
            projectReferences,
            include => include!.EndsWith(@"external\ProGPU\src\ProGPU.Text\ProGPU.Text.csproj", StringComparison.Ordinal));
        Assert.Contains(
            projectReferences,
            include => include!.EndsWith(@"external\ProGPU\src\ProGPU.Vector\ProGPU.Vector.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtendedAssemblyInfoGenerationPreservesNoOpBuildTimestamps()
    {
        var targetsPath = FindRepoPath(
            "eng",
            "WpfArcadeSdk",
            "tools",
            "ExtendedAssemblyInfo.targets");
        var targets = XDocument.Load(targetsPath);
        var generationTarget = Assert.Single(
            targets.Descendants("Target"),
            target => target.Attribute("Name")?.Value == "CoreGenerateExtendedAssemblyInfo");
        var write = Assert.Single(generationTarget.Descendants("WriteLinesToFile"));

        Assert.Equal("true", write.Attribute("WriteOnlyWhenDifferent")?.Value);
        Assert.DoesNotContain(
            generationTarget.Descendants("Delete"),
            delete => delete.Attribute("Files")?.Value == "$(GeneratedExtendedAssemblyInfoFile)");
    }

    [Theory]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/ReachFramework/ReachFramework.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationFramework.Tests/PresentationFramework.Tests.csproj")]
    public void DirectWriteForwarderReferenceIsWindowsOnly(string relativeProjectPath)
    {
        var projectPath = FindRepoPath(relativeProjectPath.Split('/'));
        var document = XDocument.Load(projectPath);

        var reference = Assert.Single(
            document.Descendants("ProjectReference"),
            reference =>
            {
                var include = reference.Attribute("Include")?.Value.Replace('/', '\\');
                return include?.EndsWith(@"DirectWriteForwarder\DirectWriteForwarder.vcxproj", StringComparison.OrdinalIgnoreCase) == true;
            });

        Assert.Equal("'$(OS)' == 'Windows_NT'", reference.Attribute("Condition")?.Value);
        Assert.Equal("TargetFramework;TargetFrameworks", reference.Element("UndefineProperties")?.Value);
    }

    [Fact]
    public void PresentationFrameworkPortableTestsAvoidNativeWindowsBuildDependencies()
    {
        var unitTestTargetsPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "tests",
            "UnitTests",
            "Directory.Build.targets");
        var unitTestTargets = XDocument.Load(unitTestTargetsPath);
        var includeNativeDependencies = Assert.Single(
            unitTestTargets.Descendants("Target"),
            target => target.Attribute("Name")?.Value == "IncludeNativeDependencies");
        Assert.Equal("'$(OS)' == 'Windows_NT'", includeNativeDependencies.Attribute("Condition")?.Value);

        var nativeProjectReferences = unitTestTargets
            .Descendants("ItemGroup")
            .Where(itemGroup => itemGroup.Elements("ProjectReference").Any())
            .Where(
                itemGroup => itemGroup.Elements("ProjectReference").Any(
                    reference => reference.Attribute("Include")?.Value.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();
        Assert.NotEmpty(nativeProjectReferences);
        Assert.All(
            nativeProjectReferences,
            itemGroup => Assert.Equal("'$(OS)' == 'Windows_NT'", itemGroup.Attribute("Condition")?.Value));

        var presentationFrameworkTestsProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "tests",
            "UnitTests",
            "PresentationFramework.Tests",
            "PresentationFramework.Tests.csproj");
        var presentationFrameworkTestsProject = XDocument.Load(presentationFrameworkTestsProjectPath);
        Assert.Contains(
            presentationFrameworkTestsProject.Descendants("ProjectReference"),
            reference => reference.Attribute("Include")?.Value.Replace('/', '\\').EndsWith(
                @"external\ProGPU\src\ProGPU.Wpf.Interop\ProGPU.Wpf.Interop.csproj",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void PortableMenuItemCloseSynchronizesItsTemplatePopup()
    {
        var menuItemPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "MenuItem.cs");
        var menuItem = File.ReadAllText(menuItemPath);

        Assert.Contains("menuItem.ClosePortableTemplatePopup();", menuItem, StringComparison.Ordinal);
        Assert.Contains("private void ClosePortableTemplatePopup()", menuItem, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", menuItem, StringComparison.Ordinal);
        Assert.Contains("if (_submenuPopup is { IsOpen: true } popup)", menuItem, StringComparison.Ordinal);
        Assert.Contains(
            "popup.SetCurrentValueInternal(Popup.IsOpenProperty, BooleanBoxes.FalseBox);",
            menuItem,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntFontFace.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntFontFace.cs")]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\ArabicFallbackData.Generated.cs", @"MS\Internal\Text\TextInterface\ProGPU\ArabicFallbackData.Generated.cs")]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntFontContainer.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntFontContainer.cs")]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntSimpleGlyphShaper.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntSimpleGlyphShaper.cs")]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntFontSubsetter.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntFontSubsetter.cs")]
    public void PresentationCoreIncludesProGpuTextSourceOnNonWindows(string sourcePath, string linkPath)
    {
        var projectPath = FindRepoPath("src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "PresentationCore.csproj");
        var document = XDocument.Load(projectPath);

        var compileItem = Assert.Single(
            document.Descendants("Compile"),
            item =>
            {
                var include = item.Attribute("Include")?.Value.Replace('/', '\\');
                return include?.EndsWith(sourcePath, StringComparison.OrdinalIgnoreCase) == true;
            });

        Assert.Equal("'$(OS)' != 'Windows_NT'", compileItem.Attribute("Condition")?.Value);

        Assert.Contains(
            document.Descendants("NoWarn"),
            item => item.Value.Contains("CA1859", StringComparison.Ordinal)
                && item.Attribute("Condition")?.Value == "'$(OS)' != 'Windows_NT'");
        Assert.Equal(linkPath, compileItem.Attribute("Link")?.Value);
    }

    [Fact]
    public void MediaContextNotificationWindowSkipsWin32WindowCreationOnNonWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaContextNotificationWindow.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("if (!s_isWindows)", source, StringComparison.Ordinal);
        Assert.Contains("new HwndWrapper", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (!s_isWindows)", StringComparison.Ordinal)
                < source.IndexOf("new HwndWrapper", StringComparison.Ordinal),
            "The non-Windows guard must run before creating the hidden HWND notification window.");
        Assert.Contains("_ownerMediaContext.Channel.SetNotificationWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationUIPortableBuildUsesWinFormsFreeDialogStubs()
    {
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationUI",
            "PresentationUI.csproj");
        var document = XDocument.Load(projectPath);

        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\DocumentSignatureManager.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\DocumentSignatureManager.Portable.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' == 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\RightsManagementManager.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\RightsManagementManager.Portable.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' == 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\Application\DocumentPropertiesDialog.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\CredentialManagerDialog.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\RMPublishingDialog.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\SignatureSummaryDialog.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");
        AssertCompileCondition(
            document,
            @"MS\Internal\Documents\SigningDialog.cs",
            "'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'");

        var winFormsReferenceGroup = Assert.Single(
            document.Descendants("ItemGroup"),
            itemGroup => itemGroup.Elements("MicrosoftPrivateWinFormsReference").Any());
        Assert.Equal("'$(ProGpuWpfSkipMissingPrivateWinFormsReferences)' != 'true'", winFormsReferenceGroup.Attribute("Condition")?.Value);

        var rootPath = Path.GetDirectoryName(FindRepoPath("README.md"))!;
        var rootPropsPath = Path.Combine(rootPath, "Directory.Build.props");
        var rootProps = File.ReadAllText(rootPropsPath);
        Assert.Contains("PROGPU_WPF_PORTABLE_NO_WINFORMS", rootProps, StringComparison.Ordinal);
    }

    [Fact]
    public void ProGpuWpfRenderDataCatalogUsesTypedDrawingReplayTerminology()
    {
        var sourcePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "WpfRenderDataInstructionRedirectionCatalog.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("TypedDrawingReplay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReflectionReplay", source, StringComparison.Ordinal);
    }

    private static void AssertCompileCondition(XDocument document, string includePath, string condition)
    {
        var item = Assert.Single(
            document.Descendants("Compile"),
            compile =>
            {
                var include = compile.Attribute("Include")?.Value.Replace('/', '\\');
                return string.Equals(include, includePath, StringComparison.OrdinalIgnoreCase);
            });

        Assert.Equal(condition, item.Attribute("Condition")?.Value);
    }

    [Fact]
    public void ProGpuWpfProductSourceStaysFreeOfReflectionMarkers()
    {
        var projectPath = FindRepoPath("src", "ProGPU.Wpf", "ProGPU.Wpf.csproj");
        var sourceRoot = Path.GetDirectoryName(projectPath)!;
        string[] forbiddenMarkers =
        [
            "using System.Reflection",
            "System.Reflection.Assembly",
            "TargetInvocationException",
            "Type.GetType",
            "GetType()",
            "GetType().FullName",
            "BindingFlags",
            "GetProperty(",
            "GetMethod(",
            "GetField(",
            "GetConstructor(",
            "Activator.CreateInstance",
            "Assembly.Load",
            "ReflectionReplay",
        ];

        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (relativePath.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relativePath.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(sourcePath);

            foreach (var marker in forbiddenMarkers)
            {
                Assert.False(
                    source.Contains(marker, StringComparison.Ordinal),
                    $"{relativePath} must not contain reflection marker '{marker}'.");
            }
        }
    }

    [Fact]
    public void SystemXamlNameScopeDictionaryImplementsDictionaryContract()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "System.Xaml",
            "System",
            "Xaml",
            "NameScopeDictionary.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("throw new NotImplementedException", source, StringComparison.Ordinal);
        Assert.Contains("int ICollection<KeyValuePair<string, object>>.Count", source, StringComparison.Ordinal);
        Assert.Contains("void ICollection<KeyValuePair<string, object>>.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("void ICollection<KeyValuePair<string, object>>.CopyTo", source, StringComparison.Ordinal);
        Assert.Contains("bool ICollection<KeyValuePair<string, object>>.Remove", source, StringComparison.Ordinal);
        Assert.Contains("object IDictionary<string, object>.this[string key]", source, StringComparison.Ordinal);
        Assert.Contains("bool IDictionary<string, object>.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("ICollection<string> IDictionary<string, object>.Keys", source, StringComparison.Ordinal);
        Assert.Contains("_underlyingNameScope.RegisterName(name, scopedElement)", source, StringComparison.Ordinal);
        Assert.Contains("_underlyingNameScope.UnregisterName(name)", source, StringComparison.Ordinal);
        Assert.Contains("return _underlyingNameScope is not null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationFrameworkInternalCollectionsImplementGenericContracts()
    {
        var listOfObjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "ListOfObject.cs");
        var weakDictionaryPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "WeakDictionary.cs");
        var listOfObject = File.ReadAllText(listOfObjectPath);
        var weakDictionary = File.ReadAllText(weakDictionaryPath);

        Assert.DoesNotContain("throw new NotImplementedException", listOfObject, StringComparison.Ordinal);
        Assert.DoesNotContain("throw new NotImplementedException", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("_list.Insert(index, item);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list.RemoveAt(index);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list[index] = value;", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list.Add(item);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("get { return _list.IsReadOnly; }", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list.Remove(item);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException();", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("public void CopyTo(KeyType[] array, int arrayIndex)", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("public bool Contains(ValueType item)", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("EqualityComparer<ValueType>.Default", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("public void CopyTo(ValueType[] array, int arrayIndex)", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("array[arrayIndex++] = key;", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("array[arrayIndex++] = value;", weakDictionary, StringComparison.Ordinal);
    }

    [Fact]
    public void XamlWriterSkipsEmptyRuntimeNames()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Markup",
            "Primitives",
            "ElementMarkupObject.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("IsEmptyRuntimeNameProperty(pd, dpd, instance)", source, StringComparison.Ordinal);
        Assert.Contains("instance is not FrameworkElement && instance is not FrameworkContentElement", source, StringComparison.Ordinal);
        Assert.Contains("return String.IsNullOrEmpty(pd.GetValue(instance) as string);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PortablePopupSettlesAnimatedPlacementAndTracksLayoutOnlyMovement()
    {
        var popupPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "Primitives",
            "Popup.cs");
        var popup = File.ReadAllText(popupPath);

        Assert.Contains("SchedulePortableSettledPosition();", popup, StringComparison.Ordinal);
        Assert.Contains("PortablePlacementTrackingInterval", popup, StringComparison.Ordinal);
        Assert.Contains("PortablePlacementSettleDelay", popup, StringComparison.Ordinal);
        Assert.Contains("new DispatcherTimer(DispatcherPriority.Loaded)", popup, StringComparison.Ordinal);
        Assert.DoesNotContain("new DispatcherTimer(DispatcherPriority.Render)\n            {\n                Interval = PortablePlacementTrackingInterval", popup, StringComparison.Ordinal);
        Assert.Contains("_portableSettledPosition.Tick += OnPortableSettledPosition;", popup, StringComparison.Ordinal);
        Assert.Contains("Reposition();\n            if (Environment.TickCount64 >= _portablePlacementTrackingDeadline)", popup, StringComparison.Ordinal);
        Assert.Contains("else\n                {\n                    // A portable popup shares the owner compositor", popup, StringComparison.Ordinal);
        Assert.Contains("OnWindowResize(_popupRoot, new AutoResizedEventArgs(clientSize));\n                    // Size can remain constant", popup, StringComparison.Ordinal);
        Assert.Contains("UpdatePosition();", popup, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableNativePopupUsesDeviceAwareTransientNonactivatingWindows()
    {
        var popupHost = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableNativePopupHost.cs"));
        var windowHost = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs"));
        var platformServices = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "IWpfPlatformServices.cs"));
        var silkDecorations = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "SilkNetWpfWindowDecorationService.cs"));

        Assert.Contains("Topmost = false", popupHost, StringComparison.Ordinal);
        Assert.Contains("ToNativeLogicalScreenCoordinate(request.PopupScreenDeviceX, _dpiScaleX)", popupHost, StringComparison.Ordinal);
        Assert.Contains("_popupHost.ShowWithoutActivation();", popupHost, StringComparison.Ordinal);
        Assert.Contains("internal static int ToDeviceScreenCoordinate", windowHost, StringComparison.Ordinal);
        Assert.Contains("UpdatePortablePopupOwnerOrigins(bridge.Source, deviceX, deviceY)", windowHost, StringComparison.Ordinal);
        Assert.Contains("internal void ShowWithoutActivation()", windowHost, StringComparison.Ordinal);
        Assert.Contains("PlatformServices.WindowDecorations.TryShowWithoutActivation(_window)", windowHost, StringComparison.Ordinal);
        Assert.Contains("internal void DeferShowUntilRun()", windowHost, StringComparison.Ordinal);
        Assert.Contains("_window.Initialize();", windowHost, StringComparison.Ordinal);
        Assert.Contains("bool TryShowWithoutActivation(object window)", platformServices, StringComparison.Ordinal);
        Assert.Contains("TryShowCocoaWithoutActivation(GetCocoaWindow(view))", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("TryShowGlfwWithoutActivation(view)", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("WindowAttributeSetter.FocusOnShow", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("\"_NET_WM_WINDOW_TYPE_DROPDOWN_MENU\"", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("\"_NET_WM_WINDOW_TYPE_POPUP_MENU\"", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("XChangeProperty(", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("SelRegisterName(\"orderFront:\")", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("SelRegisterName(\"addChildWindow:ordered:\")", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("SelRegisterName(\"setHidesOnDeactivate:\")", silkDecorations, StringComparison.Ordinal);
        Assert.Contains("_popupHost.SetPosition(_nativeLogicalX, _nativeLogicalY);", popupHost, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaContextUsesPortableClockOutsideWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaContext.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.Frequency", source, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.GetTimestamp()", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.QueryPerformanceFrequency(out frequency)", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.QueryPerformanceCounter(out performanceCount)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceFrequency(out _perfCounterFreq)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceCounter(out qpcCurrentTime)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceCounter(out _lastPresentationTime)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceCounter(out counts)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaContextHasPortableRenderWakeupBoundary()
    {
        var mediaContextPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaContext.cs");
        var visualPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Visual.cs");
        var animatablePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Animation",
            "Animatable.cs");
        var presentationCoreRefPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "ref",
            "PresentationCore.cs");
        var renderServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "PortableMediaContextRenderService.cs");
        var moduleInitializerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "ModuleInitializer.cs");
        var portableWpfServiceRegistryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Wpf.Interop",
            "PortableWpfServiceRegistry.cs");
        var portableInvalidationSourcePath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Wpf.Interop",
            "PortableInvalidationSource.cs");
        var dragDropPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "DragDrop.cs");
        var presentationCoreProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");
        var activationServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableWindowActivationService.cs");
        var presentationFrameworkModuleInitializerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "ModuleInitializer.cs");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var proGpuDiagnosticsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfDiagnostics.cs");
        var proGpuSchedulerPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "IWpfRenderScheduler.cs");
        var proGpuPlatformServicesPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "IWpfPlatformServices.cs");
        var silkNetMonitorServicePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "SilkNetWpfMonitorService.cs");
        var proGpuHostPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs");
        var proGpuPortablePresentationSourceBridgePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortablePresentationSourceBridge.cs");
        var proGpuPortablePopupBridgePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortablePopupBridge.cs");
        var proGpuPortablePopupServicePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortablePopupService.cs");
        var proGpuOptionsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowOptions.cs");
        var proGpuDrawingFramePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfDrawingFrame.cs");
        var proGpuCompositionTargetPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs");
        var proGpuInvalidationTrackerPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfVisualInvalidationTracker.cs");
        var proGpuRetainedVisualDependencyRegistrarPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfRetainedVisualDependencyRegistrar.cs");
        var proGpuRetainedCompositionCommandSinkPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "ProGpuRetainedCompositionCommandSink.cs");
        var proGpuCompositorPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "Compositor.cs");
        var proGpuDxfStaticBufferPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "DxfStaticBuffer.cs");
        var proGpuSeriesBufferPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Backend",
            "GpuSeriesBuffer.cs");
        var proGpuLineSeriesPipelinePath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "Extensions",
            "GpuLineSeriesExtensionPipeline.cs");
        var proGpuAcisPipelinePath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "Extensions",
            "AcisSolidExtensionPipeline.cs");
        var proGpuScatterSeriesPipelinePath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "Extensions",
            "GpuScatterSeriesExtensionPipeline.cs");
        var proGpuHitTestCachePath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "GpuRenderCommandHitTestCache.cs");
        var proGpuHitTestingPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Vector",
            "GpuHitTesting.cs");
        var proGpuHitTestingShaderPath = Path.Combine(
            Path.GetDirectoryName(proGpuHitTestingPath)!,
            "Shaders",
            "GpuHitTesting.wgsl");
        var proGpuDirectoryBuildPropsPath = FindRepoPath(
            "external",
            "ProGPU",
            "Directory.Build.props");
        var proGpuDashPatternPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Vector",
            "DashPattern.cs");
        var proGpuBezierSegmentGeometryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Vector",
            "BezierSegmentGeometry.cs");
        var proGpuArcSegmentGeometryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Vector",
            "ArcSegmentGeometry.cs");
        var proGpuHitTestingTestsPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Tests",
            "GpuHitTestingTests.cs");
        var proGpuCompositorReviewTestsPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Tests",
            "CompositorReviewRegressionTests.cs");
        var proGpuDrawingFrameTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "ProGpuWpfDrawingFrameTests.cs");
        var proGpuInvalidationTrackerTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "Composition",
            "Mil",
            "WpfVisualInvalidationTrackerTests.cs");
        var proGpuWindowHostTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "ProGpuWpfWindowHostTests.cs");
        var proGpuPortablePresentationSourceBridgeTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "WpfPortablePresentationSourceBridgeTests.cs");
        var proGpuActivationTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "WpfPortableWindowActivationTests.cs");

        var mediaContext = File.ReadAllText(mediaContextPath);
        var visual = File.ReadAllText(visualPath);
        var animatable = File.ReadAllText(animatablePath);
        var presentationCoreRef = File.ReadAllText(presentationCoreRefPath);
        var renderService = File.ReadAllText(renderServicePath);
        var moduleInitializer = File.ReadAllText(moduleInitializerPath);
        var portableWpfServiceRegistry = File.ReadAllText(portableWpfServiceRegistryPath);
        var portableInvalidationSource = File.ReadAllText(portableInvalidationSourcePath);
        var dragDrop = File.ReadAllText(dragDropPath);
        var presentationCoreProject = File.ReadAllText(presentationCoreProjectPath);
        var activationService = File.ReadAllText(activationServicePath);
        var presentationFrameworkModuleInitializer = File.ReadAllText(presentationFrameworkModuleInitializerPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var proGpuDiagnostics = File.ReadAllText(proGpuDiagnosticsPath);
        var proGpuScheduler = File.ReadAllText(proGpuSchedulerPath);
        var proGpuPlatformServices = File.ReadAllText(proGpuPlatformServicesPath);
        var silkNetMonitorService = File.ReadAllText(silkNetMonitorServicePath);
        var proGpuHost = File.ReadAllText(proGpuHostPath);
        var proGpuPortablePresentationSourceBridge = File.ReadAllText(proGpuPortablePresentationSourceBridgePath);
        var proGpuPortablePopupBridge = File.ReadAllText(proGpuPortablePopupBridgePath);
        var proGpuPortablePopupService = File.ReadAllText(proGpuPortablePopupServicePath);
        var proGpuOptions = File.ReadAllText(proGpuOptionsPath);
        var proGpuDrawingFrame = File.ReadAllText(proGpuDrawingFramePath);
        var proGpuCompositionTarget = File.ReadAllText(proGpuCompositionTargetPath);
        var proGpuInvalidationTracker = File.ReadAllText(proGpuInvalidationTrackerPath);
        var proGpuRetainedVisualDependencyRegistrar = File.ReadAllText(proGpuRetainedVisualDependencyRegistrarPath);
        var proGpuRetainedCompositionCommandSink = File.ReadAllText(proGpuRetainedCompositionCommandSinkPath);
        var proGpuCompositor = File.ReadAllText(proGpuCompositorPath);
        var proGpuDxfStaticBuffer = File.ReadAllText(proGpuDxfStaticBufferPath);
        var proGpuSeriesBuffer = File.ReadAllText(proGpuSeriesBufferPath);
        var proGpuLineSeriesPipeline = File.ReadAllText(proGpuLineSeriesPipelinePath);
        var proGpuAcisPipeline = File.ReadAllText(proGpuAcisPipelinePath);
        var proGpuScatterSeriesPipeline = File.ReadAllText(proGpuScatterSeriesPipelinePath);
        var proGpuHitTestCache = File.ReadAllText(proGpuHitTestCachePath);
        var proGpuHitTesting = File.ReadAllText(proGpuHitTestingPath);
        var proGpuHitTestingShader = File.Exists(proGpuHitTestingShaderPath)
            ? File.ReadAllText(proGpuHitTestingShaderPath)
            : proGpuHitTesting;
        var proGpuDirectoryBuildProps = File.ReadAllText(proGpuDirectoryBuildPropsPath);
        var proGpuDashPattern = File.ReadAllText(proGpuDashPatternPath);
        var proGpuBezierSegmentGeometry = File.ReadAllText(proGpuBezierSegmentGeometryPath);
        var proGpuArcSegmentGeometry = File.ReadAllText(proGpuArcSegmentGeometryPath);
        var proGpuHitTestingTests = File.ReadAllText(proGpuHitTestingTestsPath);
        var proGpuCompositorReviewTests = File.ReadAllText(proGpuCompositorReviewTestsPath);
        var proGpuDrawingFrameTests = File.ReadAllText(proGpuDrawingFrameTestsPath);
        var proGpuInvalidationTrackerTests = File.ReadAllText(proGpuInvalidationTrackerTestsPath);
        var proGpuWindowHostTests = File.ReadAllText(proGpuWindowHostTestsPath);
        var proGpuPortablePresentationSourceBridgeTests = File.ReadAllText(proGpuPortablePresentationSourceBridgeTestsPath);
        var proGpuActivationTests = File.ReadAllText(proGpuActivationTestsPath);

        Assert.Contains(@"<Compile Include=""System\Windows\Media\PortableMediaContextRenderService.cs"" />", presentationCoreProject, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableMediaContextRenderService", renderService, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", renderService, StringComparison.Ordinal);
        Assert.DoesNotContain("using ProGPU.Wpf.Interop;", renderService, StringComparison.Ordinal);
        Assert.DoesNotContain("internal static void RegisterPortableInteropService()", renderService, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableWpfServiceRegistry.RegisterMediaContextRenderService", renderService, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaContextRenderServiceRegistrar", renderService, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableMediaContextRenderService.RegisterPortableInteropService();", moduleInitializer, StringComparison.Ordinal);
        Assert.DoesNotContain("public interface IPortableMediaContextRenderServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.DoesNotContain("public static bool TryGetMediaContextRenderService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortableWindowActivationCallbacks", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortableWindowInputEvent", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortablePopupCreateRequest", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public int PopupScreenDeviceX { get; }", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public int PopupScreenDeviceY { get; }", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public int OwnerClientScreenDeviceX { get; }", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public int OwnerClientScreenDeviceY { get; }", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public enum PortableWindowCloseResult", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct PortableWpfServiceKey", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceKey ServiceKey", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public interface IPortableWindowActivationServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public interface IPortablePopupServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetWindowActivationService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static IDisposable RegisterPopupService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetPopupService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceAssembly", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<Assembly", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow);", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryCloseWindow(object window, out PortableWindowCloseResult result);", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TrySetActivationState(object window, bool isActive);", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryBeginInvokeInput(object window, Action callback);", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryProcessInputEvent(object window, PortableWindowInputEvent input);", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryProcessPresentationSourceInputEvent(object presentationSource, PortableWindowInputEvent input)", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout);", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryRegisterMediaContextRenderService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("bool TryProcessDragDropEvent(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("List<Action<object, TimeSpan>>", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Action requestRender)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Action<TimeSpan> requestRender)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Action<object, TimeSpan> requestRender)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static void RequestRender()", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static void RequestRender(TimeSpan delay)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static void RequestRender(object invalidatedSource, TimeSpan delay)", renderService, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderService.RequestRender(nextTickNeeded)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("internal void PostRender(object invalidatedSource)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderService.RequestRender(invalidatedSource)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("RenderDisconnectedMessageHandlerCore(resizedCompositionTarget)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("private void RenderDisconnectedMessageHandlerCore", mediaContext, StringComparison.Ordinal);
        Assert.Contains("ScheduleNextRenderOp(_timeDelay)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("if (Channel != null)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("EnterInterlockedPresentation();", mediaContext, StringComparison.Ordinal);
        Assert.Contains("if (mctx.Channel != null || PortableMediaContextRenderService.IsEnabled)", visual, StringComparison.Ordinal);
        Assert.Contains("mctx.PostRender(e);", visual, StringComparison.Ordinal);

        Assert.Contains("internal static void FlushDispatcherOperations(object window, DispatcherPriority markerPriority)", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool FlushDispatcherOperations(object window, DispatcherPriority markerPriority, TimeSpan timeout)", activationService, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Wpf.Interop;", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void RegisterPortableInteropService()", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.RegisterWindowActivationService(s_registrar)", activationService, StringComparison.Ordinal);
        Assert.Contains("private sealed class WindowActivationServiceRegistrar : IPortableWindowActivationServiceRegistrar", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow)", activationService, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(application.Dispatcher, typedWindow.Dispatcher)", activationService, StringComparison.Ordinal);
        Assert.Contains("!application.Dispatcher.CheckAccess()", activationService, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(application.MainWindow, typedWindow)", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryCloseWindow(object window, out PortableWindowCloseResult result)", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.Close();", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.IsDisposed", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TrySetActivationState(object window, bool isActive)", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetActivationState(typedWindow, isActive);", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryBeginInvokeInput(object window, Action callback)", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.Dispatcher.BeginInvoke(DispatcherPriority.Input, callback);", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryProcessInputEvent(object window, PortableWindowInputEvent input)", activationService, StringComparison.Ordinal);
        Assert.Contains("private static PortableInputEventArgs CreatePortableInputEvent(PortableWindowInputEvent input)", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.ProcessInput(typedWindow, mappedInput);", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryProcessPresentationSourceInputEvent(object presentationSource, PortableWindowInputEvent input)", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.ProcessInput(typedSource, mappedInput);", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout)", activationService, StringComparison.Ordinal);
        Assert.Contains("Enum.TryParse(markerPriorityName, ignoreCase: false, out DispatcherPriority markerPriority)", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryRegisterMediaContextRenderService(", activationService, StringComparison.Ordinal);
        Assert.Contains("Media.PortableMediaContextRenderService.Register(", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryProcessDragDropEvent(", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.ProcessDragDropEvent(", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.RegisterPortableInteropService();", presentationFrameworkModuleInitializer, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(window, markerPriority, Timeout.InfiniteTimeSpan)", activationService, StringComparison.Ordinal);
        Assert.Contains("markerOperation.Abort()", activationService, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.PushFrame(frame)", activationService, StringComparison.Ordinal);
        Assert.Contains("using MS.Internal;", activationService, StringComparison.Ordinal);
        Assert.Contains("ResolveCapturedMouseInputRoute(", activationService, StringComparison.Ordinal);
        Assert.Contains("mouseDevice?.CapturedMode != CaptureMode.Element", activationService, StringComparison.Ordinal);
        Assert.Contains("mouseDevice.Captured is not DependencyObject capturedElement", activationService, StringComparison.Ordinal);
        Assert.Contains("PresentationSource.CriticalFromVisual(capturedVisual)", activationService, StringComparison.Ordinal);
        Assert.Contains("capturedSource.RootVisual is not UIElement capturedRootHitTestElement", activationService, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ClientToScreen(reportedClientPoint, reportedSource)", activationService, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ScreenToClient(screenPoint, capturedSource)", activationService, StringComparison.Ordinal);
        Assert.Contains("Point clientPoint = ToMouseClientPoint(source, rootHitTestElement, rootPoint);", activationService, StringComparison.Ordinal);
        Assert.Contains("ToInputCoordinate(clientPoint.X)", activationService, StringComparison.Ordinal);
        Assert.Contains("ToInputCoordinate(clientPoint.Y)", activationService, StringComparison.Ordinal);
        Assert.Contains("private static Point ToMouseClientPoint(PresentationSource source, UIElement rootHitTestElement, Point rootPoint)", activationService, StringComparison.Ordinal);
        Assert.Contains("PointUtil.RootToClient(rootPoint, source)", activationService, StringComparison.Ordinal);
        Assert.Contains("public interface IWpfDelayedRenderScheduler : IWpfRenderScheduler", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("void RequestRender(TimeSpan delay)", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.TryGetWindowActivationService(", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceKey.PresentationFramework", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection.Assembly", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection.TargetInvocationException", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("currentType.Assembly", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("exception.GetBaseException()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CreateWindowActivationCallbacks(hostFactory)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TryIsCurrentApplicationMainWindow(window, out bool isMainWindow)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TryCloseWindow(window, out var typedCloseResult)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static WpfWindowCloseResult MapCloseResult(PortableWindowCloseResult result)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TrySetActivationState(window, isActive)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.ShowWithoutActivation();", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.Run(_showActivated);", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TrySetWindowActivationState(_ownerWindow, isActive: true)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("internal void Run(bool showActivated)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ShowNativeWindow(showActivated);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("activationService.TryBeginInvokeInput(Window, callback)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TryProcessInputEvent(window, input)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.TryProcessPortablePopupInput(e)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static PortableWindowInputEvent CreatePortableWindowInputEvent(WpfInputEventArgs e)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TryFlushDispatcherOperations(window, markerPriorityName, timeout)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TryProcessDragDropEvent(", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static bool TryGetWindowActivationService(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableWindowActivationServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("FindPortableWindowActivationServiceType", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryInvokePortableWindowActivationService", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryProcessPortableDragDropByReflection", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCreateDispatcherPriority", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryForwardCompatibleInputToWindow", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCreateCompatibleInputEventArgs", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyHandledState", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OnPortableInput\"", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("\"HandlePortableInput\"", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("FindInstanceMethod", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OnPortableDrop\"", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OnPortableFileDrop\"", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DropFiles\"", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Application", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("presentationFrameworkAssembly.GetType(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateWindowActivationReflectionParameters", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableMediaContextRenderServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("activationService.TryRegisterMediaContextRenderService(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableWpfServiceRegistry.TryGetMediaContextRenderService(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolvePresentationCoreAssembly", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRegisterMediaContextRenderServiceByReflection", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Action<object, TimeSpan>)", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Action<TimeSpan>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<object, WpfPortableWindowActivation>", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("RegisterActiveActivation(window, this)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("s_activeActivations.Remove(Window)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryGetActiveHost(object? window, out ProGpuWpfWindowHost? host)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.RegisterPopupService(_portablePopupService)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryCreatePortablePopup(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private WpfVisualReplayResult ReplayPortablePopups(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryProcessPortablePopupInput(WpfInputEventArgs input)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal sealed class WpfPortablePopupBridge", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("PortablePresentationSourceFactory { get; set; } =", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("PortablePresentationSourceHost.Create;", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("source = PortablePresentationSourceFactory(", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("private void SetSourceClientOrigin()", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("ToLogicalScreenCoordinate(X, _dpiScaleX)", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("(_ownerPopup?.LogicalX ?? 0.0)", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("_localLogicalX = ((double)popupScreenDeviceX - ownerClientScreenDeviceX) / dpiScaleX;", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("_localLogicalY = ((double)popupScreenDeviceY - ownerClientScreenDeviceY) / dpiScaleY;", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("public bool TrySetDeviceScale(double dpiScaleX, double dpiScaleY)", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("_source.SetDeviceScale(dpiScaleX, dpiScaleY);", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("_portablePopupBridges[i].TrySetDeviceScale(dpiScaleX, dpiScaleY);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TryProcessPresentationSourceInputEvent(Source, portableInput)", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("rootVisual.Offset = new Vector2((float)LogicalX, (float)LogicalY);", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("rootVisual.Transform = Matrix4x4.Identity;", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("ProGpuRetainedCompositionLayer.Popup", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("target.ReplayVisualSubtreeUntracked(", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("includePortablePopupRoots: true", proGpuPortablePopupBridge, StringComparison.Ordinal);
        Assert.Contains("internal sealed class WpfPortablePopupService : IPortablePopupServiceRegistrar", proGpuPortablePopupService, StringComparison.Ordinal);
        Assert.Contains("TryCreatePopup(PortablePopupCreateRequest request, out object? presentationSource)", proGpuPortablePopupService, StringComparison.Ordinal);
        Assert.Contains("public static class ProGpuWpfDiagnostics", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryGetActiveHost(window, out host)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("RenderSurfaceGeometrySnapshot", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("CompositionLayerSnapshot", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("MemorySnapshot", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("PerformanceSnapshot", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryRequestRender(object? window)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryWakeNativeLoop(object? window)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetRenderSchedulerWakeupCount(object? window, out long wakeupCount)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetRenderSurfaceGeometry(object? window, out RenderSurfaceGeometrySnapshot geometry)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetCompositionLayerSnapshot(object? window, out CompositionLayerSnapshot snapshot)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetMemorySnapshot(object? window, out MemorySnapshot snapshot)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetPerformanceSnapshot(object? window, out PerformanceSnapshot snapshot)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("host.PresentedFrameCount", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("VisualReplayCacheCapacity", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("metrics.TrackedIntermediateTextureBytes", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("PopupLayerChildCount", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryRaiseInput(", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool HasGpuHitTestCache(object? window)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetGpuHitTestCacheSnapshot(object? window, out GpuHitTestCacheSnapshot snapshot)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryHitTestOwner(object? window, double x, double y, out object? owner)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryHitTestInputOwner(object? window, double x, double y, out object? owner)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryHitTestOwners(object? window, double x, double y, out object?[] owners)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryHitTestOwners(object? window, double x, double y, Span<object?> owners, out int ownerCount)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("public static bool TryQueryHitTestBoundsOwners(", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("Span<object?> owners,\n        out int ownerCount)", proGpuDiagnostics, StringComparison.Ordinal);
        Assert.Contains("IWpfDelayedRenderScheduler delayedScheduler", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ProcessHostInputAndRequestRender", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.TryRequestNativeLoopWakeup();", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("RequestRenderFromMediaContext(RootVisual, TimeSpan.Zero);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.InvalidateWpfSourceForPortableRender(invalidatedSource ?? RootVisual);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.RenderWakeupRequested += OnHostRenderWakeupRequested", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.UpdateTick += OnHostUpdateTick", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("SynchronizeInitialWindowState(updatePortablePresentationSource: false);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("SynchronizeInitialWindowState(updatePortablePresentationSource: true);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private void SynchronizeInitialWindowState(bool updatePortablePresentationSource)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("window is IPortableWindowStateSource stateSource", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ApplyPortableWindowState(windowState, options)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static void ApplyPortableWindowState(", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private void SynchronizeInitialWindowState(\n        PortableWindowState state,", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryMapPortableWindowState(state", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("options.TransparentFramebuffer = state.AllowsTransparency;", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("target.Compositor.ClearColor = System.Numerics.Vector4.Zero;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveWindowBorder(state, options.WindowBorder)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetWindowBorder(ResolveWindowBorder(state, Host.WindowBorder))", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadStringProperty", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadPositiveDimension", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadFiniteDimension", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadBooleanProperty", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryReadProperty", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveWindowBorder(window, options.WindowBorder)", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveWindowBorder(Window, Host.WindowBorder)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ToLogicalClientDimension", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ToLogicalPositionDimension", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetInitialClientSize(width, height)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetClientSize(", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public void SetPosition(object? left, object? top)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetPosition(windowLeft.Value, windowTop.Value)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public void SetTopmost(bool topmost)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetTopmost(topmost)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("setWindowBorder: (activation, resizeMode, windowStyle) =>", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("getHandle: activation =>", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortablePresentationSourceBridge?.Handle", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Action<object, object, object>)", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Func<object, IntPtr>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public void SetWindowBorder(object? resizeMode, object? windowStyle)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetWindowBorder(ResolveWindowBorder(resizeMode, windowStyle, Host.WindowBorder))", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryMapResizeModeToWindowBorder", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("WindowStyle", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ApplicationIdleFlushTimeout = TimeSpan.FromMilliseconds(250)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("UpdateTickFlushTimeout = TimeSpan.FromMilliseconds(8)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("internal void DeferShowUntilRun()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("Host.DeferShowUntilRun();", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private bool ShouldDeferNativeShowUntilRun()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("return !_isNativeRunStarted && IsCurrentApplicationMainWindow(Window);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static bool IsCurrentApplicationMainWindow(object window)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("_isNativeRunStarted = true;", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperations(\"Loaded\", \"Render\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperations(\"Input\", \"Render\", \"ApplicationIdle\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private void OnHostUpdateTick(object? sender, EventArgs e)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperation(\"Background\", UpdateTickFlushTimeout)", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("FlushWpfDispatcherOperations(\"ApplicationIdle\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperation(markerPriorityName, timeout)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryFlushDispatcherOperations(Window, markerPriorityName, timeout)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryDragMove()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.TryBeginDragMove()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("internal static DragDropEffects ProcessPortableDrop", dragDrop, StringComparison.Ordinal);
        Assert.Contains("public static DragDropEffects ProcessPortableDragDrop", dragDrop, StringComparison.Ordinal);
        Assert.Contains("public static System.Windows.DragDropEffects ProcessPortableDragDrop(", presentationCoreRef, StringComparison.Ordinal);
        Assert.Contains("ProcessPortableDragDrop(\n                target,\n                DropEvent", dragDrop, StringComparison.Ordinal);
        Assert.Contains("internal static int ProcessDragDrop(", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static int ProcessDragDropEvent(", activationService, StringComparison.Ordinal);
        Assert.Contains("ToDragDropRoutedEvent(dragDropEventKind)", activationService, StringComparison.Ordinal);
        Assert.Contains("DragDrop.ProcessPortableDragDrop(", activationService, StringComparison.Ordinal);
        Assert.Contains("TryProcessPortableDragDrop(window, e)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public bool TryBeginDragMove()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("PlatformServices.WindowDecorations.TryBeginDragMove(_window)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int Width => _clientWidth;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int Height => _clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int? Left => _window?.Position.X ?? _windowLeft;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int? Top => _window?.Position.Y ?? _windowTop;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public bool Topmost => _window?.TopMost ?? _windowTopmost;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public ProGpuWpfWindowBorder WindowBorder => _windowBorder;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public void SetPosition(int left, int top)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.Position = new Vector2D<int>(left, top)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public void SetTopmost(bool topmost)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.TopMost = topmost", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public void SetWindowBorder(ProGpuWpfWindowBorder windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.WindowBorder = ToSilkWindowBorder(windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ApplyWindowBorderToController()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_windowController.SetDecorations(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_windowController.SetCanResize(resizable)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ProGpuWpfWindowBorder.HiddenResizable", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("windowOptions.WindowBorder = ToSilkWindowBorder(_windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static SilkWindowBorder ToSilkWindowBorder(ProGpuWpfWindowBorder windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public enum ProGpuWpfWindowBorder", proGpuOptions, StringComparison.Ordinal);
        Assert.Contains("internal void SetInitialClientSize(int width, int height)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("SetClientSizeCore(width, height, updatePortablePresentationSource: false)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int _requestedLogicalClientWidth = -1;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_requestedLogicalClientWidth = _clientWidth;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_requestedLogicalClientHeight = _clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int _declaredLogicalClientWidth = -1;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_declaredLogicalClientWidth = _clientWidth;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_declaredLogicalClientHeight = _clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal RenderSurfaceGeometry LastResolvedRenderSurfaceGeometry", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentRenderSurfaceGeometry()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentRenderSurfaceGeometryForDiagnostics()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("RaiseInputForDiagnostics(WpfInputEventArgs input)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderSurfaceGeometry(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveLogicalClientSize(", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveLogicalClientDpiScale(", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ReconcileResolvedLogicalClientSize", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetWpfRootRenderSize", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Reflection;", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection.TargetInvocationException", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("exception.GetBaseException()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdateClientSizeFromNativeResize(size, framebufferSize, monitorDpiScale);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var clientSize = _window.Size;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var framebufferSize = _window.FramebufferSize;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdatePortablePresentationSourceClientSize((uint)_clientWidth, (uint)_clientHeight);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var cachedLogicalClientWidth = GetCachedLogicalClientWidth();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var cachedLogicalClientHeight = GetCachedLogicalClientHeight();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int GetCachedLogicalClientWidth()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int GetCachedLogicalClientHeight()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal static int ResolveCachedLogicalClientDimension(", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DimensionsDifferByDpiScale", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("portablePresentationSourceDimension > 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("requestedLogicalDimension > 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("declaredLogicalDimension > 0", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("_requestedLogicalClientHeight = clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var logicalSize = ResolveLogicalClientSize(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var logicalWidth = (uint)Math.Max(1, clientWidth);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var logicalHeight = (uint)Math.Max(1, clientHeight);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var fallbackScale = NormalizeMonitorDpiScale(monitorDpiScale);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("framebufferSize.X > 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("Math.Ceiling(logicalWidth * fallbackScale)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("PlatformServices.Monitors.GetMonitors()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Backend;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveMonitorDpiScaleWithPlatformFallback(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DisplayScaleResolver.ResolveWindowDisplayScale(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DisplayScaleResolver.ResolveDisplayScaleWithPlatformFallback(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DisplayScaleResolver.NormalizeDisplayScale(dpiScale)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("Func<IMonitor, double?>? getDpiScale", silkNetMonitorService, StringComparison.Ordinal);
        Assert.Contains("var monitors = _getMonitors();", silkNetMonitorService, StringComparison.Ordinal);
        Assert.Contains("new List<WpfMonitorInfo>(monitorCollection.Count)", silkNetMonitorService, StringComparison.Ordinal);
        Assert.Contains("mapped.Add(ToMonitorInfo(monitor, mainMonitor, _getDpiScale));", silkNetMonitorService, StringComparison.Ordinal);
        Assert.Contains("ResolveDpiScale(monitor, width, height, getDpiScale?.Invoke(monitor))", silkNetMonitorService, StringComparison.Ordinal);
        Assert.Contains("monitor.VideoMode.Resolution is Vector2D<int> resolution", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Linq;", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select(monitor => ToMonitorInfo", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToArray()", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Reflection", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", silkNetMonitorService, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveNativePlatformDpiScale", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("TryResolveMacOsBackingScaleFactor", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("window is not INativeWindowSource nativeWindowSource", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("sel_registerName(\"screen\")", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("NativeResizeAppliesMaximizedWslgClientSizeInsteadOfStaleCache", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("NativeResizeDoesNotUseStaleRootRenderSizeForRealLogicalResize", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("Silk.NET's IWindow.Size contract is the logical client size", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("backingScaleFactor", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var dpiScaleX = pixelWidth / (double)logicalWidth", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var dpiScaleY = pixelHeight / (double)logicalHeight", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeDimensionLooksPhysicalForCachedDips", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("TryInferNativeDpiScaleFromCachedDips", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("FramebufferDimensionAllowsNativePhysicalClient", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("SynchronizePortablePresentationSourceGeometry();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("SynchronizePortablePresentationSourceGeometry(geometry);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdatePortablePresentationSourceClientSize(geometry.LogicalWidth, geometry.LogicalHeight)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool UpdatePortablePresentationSourceClientSize(uint logicalWidth, uint logicalHeight)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("LastResolvedRenderSurfaceGeometry = geometry;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("uint ViewportX = 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("uint ViewportY = 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("uint ViewportWidth = 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("uint ViewportHeight = 0", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveClientViewportDpiScale(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveGeometryViewportDimension(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("NormalizeInputEventForCurrentRenderSurface(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("NormalizeInputEventForRenderSurfaceGeometry(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("NativeInputCoordinatesLookPhysical(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal event EventHandler? UpdateTick;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdateTick?.Invoke(this, EventArgs.Empty);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryRequestNativeLoopWakeup()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("return PointerInputCoordinateExceedsLogicalClient(input, geometry);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("PointerCoordinateExceedsLogicalClient(input.X, geometry.LogicalWidth)", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderSurfaceCoordinatesLookPhysical(geometry)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("PROGPU_WPF_TRACE_INPUT", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static readonly bool s_traceInput = IsTraceEnabled(TraceInputEnvironmentVariable);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("if (!s_traceInput)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TraceInputEvent(\"native\", e)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TraceInputEvent(\"wpf\", input)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryHitTestOwner(double x, double y, out object? owner)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryHitTestOwners(double x, double y, out object?[] owners)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryHitTestOwners(double x, double y, Span<object?> owners, out int ownerCount)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryQueryHitTestBoundsOwners(double minX, double minY, double maxX, double maxY, out object?[] owners)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryQueryHitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> owners, out int ownerCount)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryQueryHitTestBoundsCandidates(double minX, double minY, double maxX, double maxY, out object?[] candidates)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryQueryHitTestBoundsCandidates(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryQueryHitTestEllipseCandidates(double minX, double minY, double maxX, double maxY, out object?[] candidates)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryQueryHitTestEllipseCandidates(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool HasGpuHitTestCache => !_isDisposed && _target?.LastGpuHitTestIndex != null;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool TryGetGpuHitTestCacheSnapshot(out ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot snapshot)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("owners = CopyHitTestResults(ownerBuffer.AsSpan(0, ownerCount));", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("candidates = CopyHitTestResults(candidateBuffer.AsSpan(0, candidateCount));", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static object?[] CopyHitTestResults(ReadOnlySpan<object?> results)", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToArray()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true)", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("new object?[64]", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("target.TryHitTestOwner(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("target.TryHitTestOwners(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("target.TryQueryHitTestBoundsOwners(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("target.TryQueryHitTestBoundsCandidates(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("target.TryQueryHitTestEllipseCandidates(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("source = PortablePresentationSourceHost.Create(dpiScaleX, dpiScaleY);", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("presentationSource is not IPortablePresentationSourceHost source", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("bridge.SubscribeToSource();", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("bridge.InstallHitTestOverrides();", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_source.RenderRequested += OnSourceRenderRequested;", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_source.CursorRequested += OnSourceCursorRequested;", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_source.HitTestOverride = _hitTestOverrideHandler;", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_source.HitTestAllBufferOverride = _hitTestAllBufferOverrideHandler;", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_source.HitTestBoundsBufferOverride = _hitTestBoundsBufferOverrideHandler;", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_source.HitTestEllipseBoundsBufferOverride = _hitTestEllipseBoundsBufferOverrideHandler;", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PropertyInfo", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCreatePointHitTestDelegate", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("IPortableVisualOwnerHost", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("PortableVisualOwnerKind.TransparentPointerOverlay", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("PortableVisualOwnerKind.PointerInfrastructure", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("PortableVisualOwnerKind.Window", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private object? TryHitTestOwner(double rootX, double rootY)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("for (int i = 0; i < owners.Length; i++)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("object? owner = owners[i];", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private object?[]? HitTestOwners(double rootX, double rootY)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private bool HitTestOwners(double rootX, double rootY, Span<object?> owners, out int ownerCount)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private object?[]? HitTestBoundsOwners(double minX, double minY, double maxX, double maxY)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private bool HitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private object?[]? HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private bool HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private object?[]? HitTestGeometryOwners(double minX, double minY, double maxX, double maxY, bool isEllipse)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_host.TryHitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("FilterTransparentPointerOverlays(owners[..ownerCount])", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_host.TryQueryHitTestBoundsCandidates(minX, minY, maxX, maxY, candidates, out candidateCount)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_host.TryQueryHitTestEllipseCandidates(minX, minY, maxX, maxY, candidates, out candidateCount)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("_host.TryHitTestOwner(rootX, rootY", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (object? owner in owners)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_host.HasGpuHitTestCache ? Source : null", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("_host.HasGpuHitTestCache ? Array.Empty<object>() : null", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private const string TraceHitTestEnvironmentVariable = \"PROGPU_WPF_TRACE_HIT_TEST\";", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("private static readonly bool s_traceHitTest = IsHitTestTraceEnabled();", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("if (!s_traceHitTest)", proGpuPortablePresentationSourceBridge, StringComparison.Ordinal);
        Assert.Contains("TryBindUsesTypedPortableSourceContractWithoutReflectiveShape", proGpuPortablePresentationSourceBridgeTests, StringComparison.Ordinal);
        Assert.Contains("TryBindInstallsGpuHitTestOverrideWhenSourceExposesHook", proGpuPortablePresentationSourceBridgeTests, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestOverridesReturnHandledMissWhenCacheExists", proGpuPortablePresentationSourceBridgeTests, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestPointOverrideTreatsTransparentGpuOwnerAsHandledMissWithoutSingleOwnerRetry", proGpuPortablePresentationSourceBridgeTests, StringComparison.Ordinal);
        Assert.Contains("Assert.Empty(source.HitTestEllipseBoundsOverride", proGpuPortablePresentationSourceBridgeTests, StringComparison.Ordinal);
        Assert.Contains("PROGPU_WPF_TRACE_RENDER_SURFACE", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static readonly bool s_traceRenderSurface = IsTraceEnabled(TraceRenderSurfaceEnvironmentVariable);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("if (!s_traceRenderSurface)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static bool IsTraceEnabled(string environmentVariable)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TraceRenderSurfaceGeometryIfRequested(geometry)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("RequestRenderAndWakeNativeLoop();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal void RequestRenderAndWakeNativeLoop()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal void InvalidateWpfSourceForPortableRender(object? source)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("object? dirtySource = source ?? _wpfRootVisual;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private bool _forceFullWpfReplay;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool ForceFullWpfReplayForNextFrame => _forceFullWpfReplay;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var forceFullWpfReplay = _forceFullWpfReplay;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("(forceFullWpfReplay || _target.ShouldReplayVisualSubtree(wpfRootVisual))", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("!forceFullWpfReplay", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("InvalidateWpfRootVisualForPresentationSourceGeometryChange();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private void InvalidateWpfRootVisualForPresentationSourceGeometryChange()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_target.WpfInvalidationTracker.MarkDirty(_wpfRootVisual);", proGpuHost, StringComparison.Ordinal);
        int hostOnRender = proGpuHost.IndexOf("private void OnRender(double deltaSeconds)", StringComparison.Ordinal);
        int hostPreReplayGeometrySync = proGpuHost.IndexOf("SynchronizePortablePresentationSourceGeometry(geometry);", hostOnRender, StringComparison.Ordinal);
        int hostPreReplayDispatcherDrain = proGpuHost.IndexOf("ProcessDispatcherQueueCore();", hostPreReplayGeometrySync, StringComparison.Ordinal);
        int hostDetectWpfSourceChanges = proGpuHost.IndexOf("_target.DetectWpfSourceChanges();", hostOnRender, StringComparison.Ordinal);
        Assert.True(
            hostOnRender >= 0 &&
            hostPreReplayGeometrySync >= 0 &&
            hostPreReplayGeometrySync < hostPreReplayDispatcherDrain &&
            hostPreReplayDispatcherDrain < hostDetectWpfSourceChanges,
            "The Silk.NET render callback must synchronize WPF logical geometry before draining dispatcher/layout work and before polling WPF render data.");
        Assert.Contains("_retainedWpfVisualRoot.Scale = Vector3.One", proGpuDrawingFrame, StringComparison.Ordinal);
        Assert.Contains("TrySubscribePropertyChanged(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("TryUnsubscribePropertyChanged(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("TrySubscribeCollectionChanged(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("TryUnsubscribeCollectionChanged(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private readonly List<InvalidationSubscription> _subscriptions = new();", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("_subscriptions.Add(InvalidationSubscription.ForDisposable(subscription));", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("_subscriptions.Add(InvalidationSubscription.ForPropertyChanged(propertyChanged, handler));", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("_subscriptions.Add(InvalidationSubscription.ForCollectionChanged(collectionChanged, handler));", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private readonly struct InvalidationSubscription", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("TryDisposeInvalidationSubscription(_disposable);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private sealed class PropertyChangedInvalidationHandler", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private sealed class CollectionChangedInvalidationHandler", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly List<Action> _unsubscribeActions", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("_unsubscribeActions.Add(() => TryRunInvalidationSubscriptionAction(subscription.Dispose));", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("_unsubscribeActions.Add(() => TryRunInvalidationSubscriptionAction(unsubscribe));", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("ForAction", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("Action? _action", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TrySubscribeInvalidationCallback", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRunInvalidationSubscriptionAction", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableInvalidationSource = ProGPU.Wpf.Interop.IPortableInvalidationSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("interface IPortableInvalidationSource", portableInvalidationSource, StringComparison.Ordinal);
        Assert.Contains("PortableInvalidationSubscription", portableInvalidationSource, StringComparison.Ordinal);
        Assert.Contains("Animatable : Freezable, IAnimatable, DUCE.IResource, IPortableInvalidationSource", animatable, StringComparison.Ordinal);
        Assert.Contains("IPortableInvalidationSource.TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)", animatable, StringComparison.Ordinal);
        Assert.Contains("Animatable : System.Windows.Freezable, System.Windows.Media.Animation.IAnimatable, ProGPU.Wpf.Interop.IPortableInvalidationSource", presentationCoreRef, StringComparison.Ordinal);
        Assert.Contains("bool ProGPU.Wpf.Interop.IPortableInvalidationSource.TrySubscribeInvalidated(System.EventHandler handler, out System.IDisposable subscription)", presentationCoreRef, StringComparison.Ordinal);
        Assert.Contains("source is PortableInvalidationSource invalidationSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("s_eventNames", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEvent(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("AddEventHandler", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("PortableInvalidationSourceMarksTrackerDirtyWithoutReflectedEvent", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("NonPortableChangedEventDoesNotMarkTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("VisitPortableDependencies(source, ref dependencyState", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private static void VisitPortableDependencies<TState, TVisitor>", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private interface IPortableDependencyVisitor<TState>", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("private static IReadOnlyList<object?> EnumeratePortableDependencies", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<object?> { drawingContent }", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("dependencies ??= new List<object?>();", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableDrawingContentSource drawingContentSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableRenderDataSource renderDataSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("renderDataSnapshot.DependentResources", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("for (var i = 0; i < renderDataSnapshot.DependentResources.Count; i++)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("renderDataSnapshot.DependentResources[i]", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dependency in renderDataSnapshot.DependentResources)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private static void VisitCollectionItems<TState, TVisitor>", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("collection is IList list", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("for (var i = 0; i < list.Count; i++)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visitor.Visit(ref state, list[i]);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("collection is IReadOnlyList<object?> objectList", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private interface ICollectionItemVisitor<TState>", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("var enumerator = collection.GetEnumerator();", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("while (enumerator.MoveNext())", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visitor.Visit(ref state, enumerator.Current);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("if (enumerator is IDisposable disposable)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var item in collection)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var item in collection)\n            {\n                SubscribeObject", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var item in collection)\n            {\n                CaptureObjectVisualStateAndChildren", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var item in collection)\n            {\n                CollectTrackedDependencies", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var item in collection)\n            {\n                registered |= RegisterTrackedDependencies", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("internal static bool RegisterTrackedDependencies(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private static HashSet<object>? s_registerTrackedDependenciesVisited;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("private static HashSet<object>? s_enumerateTrackedDependenciesVisited;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("var visited = s_registerTrackedDependenciesVisited;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("s_registerTrackedDependenciesVisited = visited;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visited.Clear();", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("sink.RegisterVisualDependency(source);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);\n        return RegisterTrackedDependencies(sink, source, visited);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("WpfVisualInvalidationTracker.RegisterTrackedDependencies(retainedVisualBranchSink, dependency)", proGpuRetainedVisualDependencyRegistrar, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var trackedDependency in WpfVisualInvalidationTracker.EnumerateTrackedDependencies(dependency))", proGpuRetainedVisualDependencyRegistrar, StringComparison.Ordinal);
        Assert.DoesNotContain("Register(IWpfCompositionCommandSink sink, params object?[] dependencies)", proGpuRetainedVisualDependencyRegistrar, StringComparison.Ordinal);
        Assert.Contains("EnumerateTrackedDependenciesUsesPortableDrawingAndRenderDataSources", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("ListLikeTrackedDependencyTraversalUsesIndexerWithoutEnumerator", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("PortableDrawingRenderDataDependencyChangeMarksTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("EnumerateTrackedDependenciesIgnoresNonPortablePrivateDrawingContentGraph", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("NonPortablePrivateDrawingContentChangeDoesNotMarkTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("PrivateVersionFieldChangeDoesNotMarkTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("NonPortablePublicVersionChangeDoesNotMarkTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("PortableVisualSourceDoesNotProbeReflectedReferenceProperties", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("PortableInvalidationSourceDoesNotProbeReflectedVersionProperties", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("PortableVisualSourceDoesNotProbeReflectedVersionProperties", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("NonPortableChildrenCollectionChangeDoesNotMarkTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("EnumerateTrackedDependenciesDoesNotExpandGradientStopGraphByReflection", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("GradientStopChangeInvalidatesTrackedPortableBrush", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("GeometryDrawingGeometryChangeMarksTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Reflection", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetPropertyValue", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadIntProperty", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("FindIndexer", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateReferencePropertyNames", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldScanReferenceProperties", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPortableGraphSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("s_referencePropertyNames", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableImageDrawingStateSource = ProGPU.Wpf.Interop.IPortableImageDrawingStateSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableGlyphRunDrawingStateSource = ProGPU.Wpf.Interop.IPortableGlyphRunDrawingStateSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableDrawingGroupChildrenSource = ProGPU.Wpf.Interop.IPortableDrawingGroupChildrenSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableDrawingGroupStateSource = ProGPU.Wpf.Interop.IPortableDrawingGroupStateSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableShaderEffectSource = ProGPU.Wpf.Interop.IPortableShaderEffectSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableDrawingContentSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableRenderDataSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("TryReadPortableVisualChildrenSnapshot(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableVisualStateSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableVisualLayoutStateSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableGeometryDrawingStateSource geometryDrawingSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableImageDrawingStateSource imageDrawingSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableGlyphRunDrawingStateSource glyphRunDrawingSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableDrawingGroupStateSource drawingGroupSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableDrawingGroupChildrenSource childrenSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableTileBrushSource tileBrushSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableShaderEffectSource shaderEffectSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("VisitPortableDrawingGroupChildren(ref state, visitor, source, drawingGroupState)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("tileBrush.Content", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("var samplers = shaderEffect.Samplers;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("for (var i = 0; i < samplers.Length; i++)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("var sampler = samplers[i];", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("sampler.Kind == PortableShaderSamplerKind.Brush", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var sampler in shaderEffect.Samplers)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("\"_content\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("\"_drawingContent\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("s_versionFieldNames", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("s_versionPropertyNames", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("VersionSnapshotCount", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("s_fieldNames", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadVersionValue", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetVersionPropertyValue", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectVersionChanges", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureVersionSnapshots", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetVersionFieldValue", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetFieldValue", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("FindField", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetField(", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("\"_floatRegisters\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("\"_samplerData\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("\"_shaderBytecode\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("TryGetPortableVisualState(source, out var visualState)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is PortableVisualStateSource visualStateSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("hasPortableVisualState && visualState.HasOffset", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("hasPortableVisualState && visualState.HasTransform", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("hasPortableVisualState && visualState.HasClip", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("builder.SetScrollableAreaClip(scrollClip.X, scrollClip.Y, scrollClip.Width, scrollClip.Height);", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("hasPortableVisualState && visualState.HasOpacityMask", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("SetOpacityMask(object? opacityMask)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visualState.HasEffect", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visualState.HasCacheMode", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visualState.HasBitmapScalingMode", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visualState.HasTextRenderingMode", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("visualState.HasSnappingGuidelinesX", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("PortableVisualStateChangeMarksTrackerDirtyWithoutEvent", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("source is PortableVisualLayoutStateSource visualLayoutSource", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("layoutState.HasLayoutClip", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("SetLayoutClip(object? clip)", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("PortableLayoutStateChangeMarksTrackerDirtyWithoutEvent", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("NonPortableVisualStatePropertyChangesDoNotMarkTrackerDirty", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadVectorLikeProperty(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadVectorLikeField(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadSizeProperty(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadBoolProperty(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetVisualClip(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetScrollableAreaClip(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("!hasPortableLayoutState && TryGetLayoutClip(source", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool TryGetLayoutClip", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLayoutClipInternal", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetPropertyValue(source, \"Transform\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetPropertyValue(source, \"OpacityMask\"", proGpuInvalidationTracker, StringComparison.Ordinal);
        Assert.Contains("logicalWidth,\n                logicalHeight,\n                dpiScaleX,\n                dpiScaleY", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("Present(\n                    logicalWidth,\n                    logicalHeight,\n                    pixelWidth,\n                    pixelHeight,\n                    viewportX,\n                    viewportY,\n                    viewportWidth,\n                    viewportHeight,\n                    dpiScale)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("new ProGpuRenderTargetViewport(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveLogicalRenderDimension(SceneRootVisual.Size.X, RootVisual.Size.X, RetainedWpfVisualRoot.Size.X, pixelWidth)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public ProGpuContainerVisual PopupRetainedWpfVisualRoot { get; } = new();", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("SceneRootVisual.AddChild(PopupRetainedWpfVisualRoot);", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("Render(logicalWidth, logicalHeight, pixelWidth, pixelHeight, dpiScale, targetView)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("using ProGpuRenderTargetViewport = global::ProGPU.Scene.RenderTargetViewport;", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("renderTargetViewport", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("uint renderTargetWidth", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("renderTargetWidth = Math.Max(1u, renderTargetWidth);", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_explicitRenderTargetWidth = renderTargetWidth;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct RenderTargetViewport", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private RenderTargetViewport? _explicitRenderTargetViewport;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("uint renderWidth = _explicitRenderTargetWidth ?? width", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ApplyRenderPassViewport(pass, renderWidth, renderHeight, useRenderTargetViewport: true)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private readonly GpuRenderCommandHitTestCacheBuilder _hitTestCacheBuilder;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_hitTestCacheBuilder = new GpuRenderCommandHitTestCacheBuilder(_pathAtlas);", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private GpuHitTestDeviceIndex? _lastHitTestDeviceIndex;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public GpuHitTestIndex? LastHitTestIndex", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public GpuHitTestDeviceIndex? LastHitTestDeviceIndex => _lastHitTestDeviceIndex;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private bool _suspendHitTestCacheWrites;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private void AddHitTestCommand(RenderCommand command, Matrix4x4 transform)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("AddHitTestStateCommand(cmd, activeTransform);", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private void AddHitTestDrawCommand(RenderCommand command, Matrix4x4 transform)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_suspendHitTestCacheWrites = true;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreateDashedStrokePath(PathGeometry source, Pen pen, out PathGeometry dashedPath)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("internal static Pen CreateUndashedPen(Pen pen)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("SetLastHitTestIndex(_hitTestCacheBuilder.BuildIndex());", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CollectionsMarshal.AsSpan(_primitives)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("CollectionsMarshal.AsSpan(_pathSegments)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("uint startSegment = AppendPathSegments(segments);", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("private uint AppendPathSegments(ReadOnlySpan<GpuPathSegment> segments)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("_pathSegments.EnsureCapacity(checked(startSegment + segments.Length));", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("_pathSegments.Add(segments[segmentIndex]);", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<ClipState> _clipStack;", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<float> _opacityStack;", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("private struct SmallValueStack<T>", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences<T>()", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("_primitives.ToArray()", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("_pathSegments.ToArray()", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("_pathSegments.AddRange(segments)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<ClipState> _clipStack", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<float> _opacityStack", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("new Stack<ClipState>", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.DoesNotContain("new Stack<float>", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("PrimitiveIndexBucket retained = default;", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("PrimitiveIndexBucket child0 = default;", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("AddChildPrimitive(ref child0, ref child1, ref child2, ref child3, childIndex, primitiveIndex)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("CountNonEmpty(in child0, in child1, in child2, in child3)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("int child0NodeIndex = AddChildNodeSlot(in child0);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("FillChildNode(child0NodeIndex, 0, in child0, min, max, center, depth);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private struct PrimitiveIndexBucket : IPrimitiveIndexSource, IDisposable", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<int>.Shared.Rent(InitialCapacity)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<int>.Shared.Rent(items.Length * 2)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<int>.Shared.Return(items)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("retained.Dispose();", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("child0.Dispose();", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("var builder = new Builder(primitives, maxDepth, maxPrimitivesPerNode);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("builder.AddRootNode(min, max);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private const int MaxPreallocatedNodeCapacity = 65_536;", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("Nodes = new List<GpuHitTestNode>(EstimateNodeCapacity(primitives.Length, maxPrimitivesPerNode));", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("PrimitiveIndices = new List<uint>(primitives.Length);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("int childIndex = FindContainingChild(primitive.BoundsMin, primitive.BoundsMax, center);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("bool fitsLeft = primitiveMax.X <= center.X;", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("bool fitsBottom = primitiveMin.Y >= center.Y;", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("new RootPrimitiveIndices(_primitives.Length)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private ref struct Builder", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private readonly ReadOnlySpan<GpuHitTestPrimitive> _primitives;", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("CopySpan(primitives)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("CopySpan(pathSegments)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("CopyList(builder.Nodes)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("CopyList(builder.PrimitiveIndices)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private static T[] CopyList<T>(List<T> values)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("array[i] = values[i];", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("FindContainingChild(primitive.BoundsMin, primitive.BoundsMax, min, max, center)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("for (int i = 0; i < 4; i++)\n            {\n                var child = GetChildBounds(i, nodeMin, nodeMax, center);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private readonly struct RootPrimitiveIndices", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("var primitiveArray = primitives.ToArray();", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("var pathSegmentArray = pathSegments.ToArray();", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Nodes.ToArray()", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.PrimitiveIndices.ToArray()", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("new Builder(primitiveArray", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("var all = new List<int>(primitiveArray.Length);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.AddNode(min, max, all, depth: 0);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("var retained = new List<int>();", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("List<int>? retained", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("List<int>? child0", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("retained ??= [];", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly struct ListPrimitiveIndices", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("new ListPrimitiveIndices(childPrimitives)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("var childPrimitiveLists = new List<int>[4];", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("childPrimitiveLists[i] = [];", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("new (Vector2 Min, Vector2 Max, List<int> Primitives, int NodeIndex)[childCount]", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("childSlots[slot++]", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("if (pen?.HasDashPattern != true)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("TryGetDashedStrokePath(command, commandPath, pen, out var strokePath, out var strokePen)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("TryAddPathStrokePrimitive(strokePath, transform, id, zIndex, strokePen);", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("AddPathStrokePrimitive(path, transform, id, zIndex, pen);", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("if (!command.Rect.IsEmpty)", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("AddBounds(command.Rect, transform, id, zIndex);", proGpuHitTestCache, StringComparison.Ordinal);
        Assert.Contains("RenderCommandCacheUsesDashedPathSegmentsForStrokeHitTesting", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("RenderCommandCacheUsesGlyphRunCommandBoundsWhenAvailable", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("RenderCommandCacheFeedsGpuCombinedPathFillHitTesting", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllReturnsIntersectingBroadPhaseHitsInDescendingZOrder", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllClassifiesRectBoundsIntersectionDetailOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsEllipseCornerFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllClassifiesEllipseRectRegionIntersectionDetailOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsEllipseStrokeHoleOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryHitTestPointAllReportsTotalHitCountWhenCallerCapacityTruncates", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("AllHitQueryClearsResultBufferWithoutPerQueryHeapArray", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("HitTestIndexBuilderUsesPooledPrimitiveBuckets", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(128, Marshal.SizeOf<GpuHitTestPrimitive>());", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsRoundedRectangleCornerFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsRectangleStrokeHoleOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsQueryBoundsCornerFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllClassifiesRectangleFillIntersectionDetailOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsEllipseFillQueryBoundsFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllClassifiesSameCenterEllipseFillDetailOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsEllipseStrokeHoleOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsRoundedRectangleCornerFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllKeepsRoundedRectangleFillOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsRectangleStrokeHoleOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllKeepsRectangleStrokeOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("LineStrokeCachesDirectionAndLengthForGpuHitTesting", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("EllipseCachesCenterAndInverseRadiiForGpuHitTesting", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("RenderCommandCacheCachesLineStrokeHelperDataForGpuHitTesting", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("RenderCommandCacheCachesEllipseHelperDataForGpuHitTesting", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsLineStrokeBoundsFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllKeepsIntersectingLineStrokeOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsPathFillDifferenceHoleOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllKeepsIntersectingPathFillOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllRejectsPathStrokeBoundsFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryEllipseAllKeepsIntersectingPathStrokeOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsLineStrokeBoundsFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsLineStrokeFlatCapFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsPathFillBoundsFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllClassifiesPathFillRectRegionIntersectionDetailOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsPathStrokeBoundsFalsePositiveOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllRejectsCombinedPathDifferenceHoleOnGpu", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryHitTestPointAllStoresDistinctIdsAndKeepsHighestZDuplicate", proGpuHitTestingTests, StringComparison.Ordinal);
        Assert.Contains("TryQueryBoundsAllStoresDistinctIdsAndKeepsHighestZDuplicate", proGpuHitTestingTests, StringComparison.Ordinal);
        if (File.Exists(proGpuHitTestingShaderPath))
        {
            Assert.Contains("ShaderResource.Load(typeof(GpuHitTestEngine), \"GpuHitTesting.wgsl\")", proGpuHitTesting, StringComparison.Ordinal);
            Assert.Contains(@"<EmbeddedResource Include=""Shaders/*.wgsl;Shaders/*.glsl;Shaders/*.hlsl""", proGpuDirectoryBuildProps, StringComparison.Ordinal);
            Assert.Contains(@"LogicalName=""$(MSBuildProjectName).Shaders.%(Filename)%(Extension)""", proGpuDirectoryBuildProps, StringComparison.Ordinal);
            Assert.DoesNotContain("const QUERY_MODE_BOUNDS: u32", proGpuHitTesting, StringComparison.Ordinal);
        }

        Assert.Contains("const QUERY_MODE_BOUNDS: u32", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("const QUERY_MODE_ELLIPSE_REGION: u32", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("const INTERSECTION_DETAIL_FULLY_INSIDE: u32", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn query_uses_ellipse_region()", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn classify_ellipse_region_intersection_detail(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn ellipses_may_intersect(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn ellipse_stroke_intersects_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn classify_bounds_intersection_detail(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rect_intersects_ellipse(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rect_intersects_ellipse_stroke(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rect_intersects_rounded_rect(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rect_intersects_rect_stroke(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rounded_rect_intersects_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rect_stroke_intersects_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn segment_intersects_rect(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("data2: vec4<f32>,", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("let direction = primitive.data2.xy;", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("CreateLineStrokeHitTestData(start, end)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("fn contains_cached_ellipse(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn rect_intersects_cached_ellipse(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn line_stroke_intersects_rect(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn line_stroke_intersects_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn segment_intersects_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn path_fill_segments_intersect_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn classify_path_fill_ellipse_region_intersection_detail(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn path_stroke_intersects_ellipse_region(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn classify_path_fill_rect_intersection_detail(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn path_stroke_intersects_rect(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn primitive_uses_precise_bounds_region_test(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn intersects_bounds(", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("query_result_capacity()", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("let total_count = results[0].hit + 1u;", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn stored_result_count(capacity: u32) -> u32", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("if (count >= capacity) {\n            break;\n        }\n\n        if (results[count + 1u].hit == 0u) {", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.Contains("fn find_stored_hit_slot(id: i32, stored_count: u32) -> u32", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.DoesNotContain("if (count >= capacity || results[count + 1u].hit == 0u)", proGpuHitTestingShader, StringComparison.Ordinal);
        Assert.DoesNotContain("new GpuHitTestResult[requestedCount + 1]", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("stackalloc GpuHitTestResult[resultBufferElementCount]", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<GpuHitTestResult>.Shared.Rent(resultBufferElementCount)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("deviceIndex.ResultListBuffer.Write(initialResults)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("Span<byte> bytes = stackalloc byte[ResultBufferSizeBytes];", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("deviceIndex.ResultBuffer.ReadBytes(bytes);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("Span<byte> readbackBytes = readSizeBytes <= HitTestStackReadbackByteLimit", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("stackalloc byte[readSizeBytes]", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Rent(readSizeBytes)", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("deviceIndex.ResultListBuffer.ReadBytes(readbackBytes);", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("Buffer = deviceIndex.ResultListBuffer.BufferPtr, Offset = 0, Size = deviceIndex.ResultListBuffer.Size", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("byte[] bytes = deviceIndex.ResultBuffer.ReadBytes", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("byte[] bytes = deviceIndex.ResultListBuffer.ReadBytes", proGpuHitTesting, StringComparison.Ordinal);
        Assert.DoesNotContain("Buffer = deviceIndex.ResultListBuffer.BufferPtr, Offset = 0, Size = checked((uint)(initialResults.Length * resultSize))", proGpuHitTesting, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<Rect> _clipStack;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<bool> _clipScopeIsGeometryMask;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<float> _opacityStack;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<GpuBlendMode> _blendModeStack;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<MaskTextureState> _maskStack;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private readonly record struct MaskTextureState(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private static T[] RentStackSnapshot<T>(in SmallValueStack<T> stack, out int count)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private static T[] RentListSnapshot<T>(List<T> list, out int count)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CollectionsMarshal.SetCount(list, count)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences<T>()", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentStackSnapshot(_clipStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentStackSnapshot(_clipScopeIsGeometryMask", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentStackSnapshot(_opacityStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentStackSnapshot(_blendModeStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentStackSnapshot(_maskStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentListSnapshot(_vectorVerticesList", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentListSnapshot(_vectorIndicesList", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentListSnapshot(_textVerticesList", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentListSnapshot(_drawCalls", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ReturnListSnapshot(savedVectorVertices", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private SmallValueStack<List<CompositorDrawCall>> _drawCallListPool;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private List<CompositorDrawCall> RentDrawCallList(int capacity)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private List<CompositorDrawCall> RentMaskDrawCallList(int capacity)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private void ReturnMaskRenderPassDrawCallLists()", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ReturnMaskRenderPassDrawCallLists();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private void ReturnPendingMaskTexturesToPool()", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ReturnPendingMaskTexturesToPool();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RentMaskDrawCallList(maskDrawCallCount)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var maskTextureCount = _masksToReturnToPool.Count;\n        for (var maskTextureIndex = 0; maskTextureIndex < maskTextureCount; maskTextureIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_maskTexturePool.Add(_masksToReturnToPool[maskTextureIndex]);", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var maskPassCount = _maskRenderPasses.Count;\n        for (var maskPassIndex = 0; maskPassIndex < maskPassCount; maskPassIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var maskPass = _maskRenderPasses[maskPassIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var maskDrawCalls = maskPass.DrawCalls;\n            var maskDrawCallCount = maskDrawCalls.Count;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var dc = maskDrawCalls[drawCallIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var staticDrawCallList = RentDrawCallList(commands.Count)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var staticDrawCallList = RentDrawCallList(context.Commands.Count)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ReturnDrawCallList(staticDrawCalls)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var drawCallCount = _drawCalls.Count;\n            for (var drawCallIndex = 0; drawCallIndex < drawCallCount; drawCallIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var dc = _drawCalls[drawCallIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var drawCalls = sb.DrawCalls;\n        for (var drawCallIndex = 0; drawCallIndex < drawCalls.Length; drawCallIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var dc = drawCalls[drawCallIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var diagnosticCommands = diagContext.Commands;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var cmd = diagnosticCommands[commandIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var commands = ctx.Commands;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var commandCount = commands.Count;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var commands = picture.Commands;\n        for (var commandIndex = 0; commandIndex < commands.Length; commandIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var commands = context.Commands;\n            var commandCount = commands.Count;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var textRecords = staticBuffer.TextRecords;\n            for (var recordIndex = 0; recordIndex < textRecords.Length; recordIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("staticBuffer.UpdateTextBuffer(CollectionsMarshal.AsSpan(_textVerticesList));", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public void UpdateTextBuffer(ReadOnlySpan<GlyphInstance> textVertices)", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.Contains("UpdateTextBuffer((ReadOnlySpan<GlyphInstance>)textVertices);", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.Contains("uint requiredBytes = checked((uint)textVertexCount * (uint)Marshal.SizeOf<GlyphInstance>());", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.Contains("_textVertexBufferBack.Write(textVertices);", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.Contains("var extensionStateEnumerator = _extensionStates.Values.GetEnumerator();", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.Contains("while (extensionStateEnumerator.MoveNext())", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var state in _extensionStates.Values)", proGpuDxfStaticBuffer, StringComparison.Ordinal);
        Assert.Contains("var layoutGlyphs = layout.Glyphs;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var layoutGlyphCount = layoutGlyphs.Count;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int glyphIndex = 0; glyphIndex < layoutGlyphCount; glyphIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var runGlyph = layoutGlyphs[glyphIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var extensionCount = _registeredExtensions.Count;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var ext = _registeredExtensions[extensionIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var pathFigures = cmd.Path.Figures;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int figureIndex = 0; figureIndex < pathFigures.Count; figureIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var sourceFigures = source.Figures;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int figureIndex = 0; figureIndex < sourceFigures.Count; figureIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int segmentIndex = 0; segmentIndex < figureSegments.Count; segmentIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int dashIndex = 0; dashIndex < quadraticSegments.Length; dashIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int dashIndex = 0; dashIndex < cubicSegments.Length; dashIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int dashIndex = 0; dashIndex < arcSegments.Length; dashIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("for (int layerIndex = 0; layerIndex < colorLayers.Count; layerIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var layer = colorLayers[layerIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private static PathGeometry CreatePositionedGlyphOutline(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var figures = outline.Figures;\n        for (var figureIndex = 0; figureIndex < figures.Count; figureIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var segments = figure.Segments;\n            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var segment = figureSegments[segmentIndex];", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var staticDrawCalls = new List<CompositorDrawCall>();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dc in _drawCalls)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dc in sb.DrawCalls)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var cmd in diagContext.Commands)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var cmd in ctx.Commands)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var cmd in picture.Commands)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var cmd in commands)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var cmd in context.Commands)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var record in staticBuffer.TextRecords)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("staticBuffer.UpdateTextBuffer(_textVerticesList.ToArray())", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var runGlyph in layout.Glyphs)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var maskPass in _maskRenderPasses)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dc in maskPass.DrawCalls)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var tex in _masksToReturnToPool)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var kvp in _persistentTextureBindGroups)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var key in _persistentTextureBindGroups.Keys)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var key in cache.Keys)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var fe in _effectTextures.Keys)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var tuple in _effectTextures.Values)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var entry in _allocatedLayerTextures)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var cachedBg in _persistentTextureBindGroups.Values)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var bg in _maskBindGroups.Values)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var bg in _maskBindGroupsOffscreen.Values)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var ext in _registeredExtensions)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var figure in cmd.Path.Figures)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var figure in source.Figures)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var segment in figure.Segments)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dashSegment in quadraticSegments)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dashSegment in cubicSegments)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var dashSegment in arcSegments)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var layer in colorLayers)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var fig in layerOutline.Figures)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var seg in fig.Segments)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_maskDrawCallListPool", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private static void AddRemovalItem<T>(ref T[]? buffer, ref int count, int capacity, T item)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private static void ReturnRemovalBuffer<T>(T[]? buffer, int count)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("AddRemovalItem(ref keysToRemove", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("AddRemovalItem(ref detached", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("AddRemovalItem(ref stale", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var bindGroupEnumerator = _persistentTextureBindGroups.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var key = bindGroupEnumerator.Current.Key;", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var maskBindGroupEnumerator = cache.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var effectTextureEnumerator = _effectTextures.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var layerTextureEnumerator = _allocatedLayerTextures.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var effectTextureEnumerator = _effectTextures.Values.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var allocatedLayerTextureEnumerator = _allocatedLayerTextures.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var cachedBindGroupEnumerator = _persistentTextureBindGroups.Values.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var maskBindGroupEnumerator = _maskBindGroups.Values.GetEnumerator();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_maskBindGroupsOffscreen", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private void DisposeMaskTexturePool()", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var pooledMaskTextures = RentListSnapshot(_maskTexturePool", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_opacityStack.Push(_activeOpacity);", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_activeOpacity = _opacityStack.Pop();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_clipStack.ToArray()", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_clipScopeIsGeometryMask.ToArray()", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_opacityStack.ToArray()", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private static T[] RentStackSnapshot<T>(Stack<T>", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<Rect> _clipStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<bool> _clipScopeIsGeometryMask", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<float> _opacityStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<GpuBlendMode> _blendModeStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<GpuTexture> _maskStack", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Stack<List<CompositorDrawCall>> _drawCallListPool", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var opacity in _opacityStack)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_blendModeStack.ToArray()", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_maskStack.ToArray()", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var savedVectorVertices = _vectorVerticesList.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var dxfSavedVectorVertices = _vectorVerticesList.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var savedTextVertices = _textVerticesList.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var dxfSavedTextVertices = _textVerticesList.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var savedDrawCalls = _drawCalls.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var dxfSavedDrawCalls = _drawCalls.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var savedMaskRenderPasses = _maskRenderPasses.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var dxfSavedMaskRenderPasses = _maskRenderPasses.ToArray();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var maskDrawCalls = new List<CompositorDrawCall>();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("keysToRemove ??= new List<TextureCacheKey>();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("keysToRemove ??= new List<GpuTexture>();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("detached ??= new List<Visual>();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("stale ??= new List<Visual>();", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("_maskTexturePool.ToArray()", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public void Upload(ReadOnlySpan<float> interleavedCoords, int pointsCount)", proGpuSeriesBuffer, StringComparison.Ordinal);
        Assert.Contains("Buffer.Write(interleavedCoords)", proGpuSeriesBuffer, StringComparison.Ordinal);
        Assert.Contains("cachedBuffer.Upload(cachedBuffer.CachedInterleaved.AsSpan(0, requiredLength), pointsCount)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("cachedBuffer.Upload(cachedBuffer.CachedInterleaved.AsSpan(0, requiredLength), pointsCount)", proGpuLineSeriesPipeline, StringComparison.Ordinal);
        Assert.Contains("cachedBuffer.Upload(cachedBuffer.CachedInterleaved.AsSpan(0, requiredLength), pointsCount)", proGpuScatterSeriesPipeline, StringComparison.Ordinal);
        Assert.Contains("tempBuffer.Upload(floatsSpan.Slice(0, pointsCount * 2), pointsCount)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("tempBuffer.Upload(floatsSpan.Slice(0, pointsCount * 3), pointsCount)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("var array = ArrayPool<float>.Shared.Rent(pointsCount * 3)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<float>.Shared.Return(array)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("tempBuffer.Upload(floatsSpan.Slice(0, pointsCount * 2), pointsCount)", proGpuLineSeriesPipeline, StringComparison.Ordinal);
        Assert.Contains("tempBuffer.Upload(floatsSpan.Slice(0, pointsCount * 3), pointsCount)", proGpuScatterSeriesPipeline, StringComparison.Ordinal);
        Assert.Contains("var array = ArrayPool<float>.Shared.Rent(pointsCount * 3)", proGpuScatterSeriesPipeline, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<float>.Shared.Return(array)", proGpuScatterSeriesPipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("var array = new float[pointsCount * 2];", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var array = new float[pointsCount * 3];", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("var array = new float[pointsCount * 2];", proGpuLineSeriesPipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("var array = new float[pointsCount * 3];", proGpuScatterSeriesPipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedBuffer.Upload(cachedBuffer.CachedInterleaved, pointsCount)", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedBuffer.Upload(cachedBuffer.CachedInterleaved, pointsCount)", proGpuLineSeriesPipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedBuffer.Upload(cachedBuffer.CachedInterleaved, pointsCount)", proGpuScatterSeriesPipeline, StringComparison.Ordinal);
        Assert.Contains("private static void AddDashedLineFigures(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("DashPattern.Advance(\n                intervals,", proGpuCompositor, StringComparison.Ordinal);
        Assert.DoesNotContain("pattern.TryCreateLineSegments(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("private static int CountLineSegments(", proGpuDashPattern, StringComparison.Ordinal);
        Assert.Contains("private static void FillLineSegments(", proGpuDashPattern, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Collections.Generic;", proGpuDashPattern, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<LineDashSegment>", proGpuDashPattern, StringComparison.Ordinal);
        Assert.DoesNotContain("segments.ToArray()", proGpuDashPattern, StringComparison.Ordinal);
        Assert.Contains("private static bool TryPrepareDashSegments(", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.Contains("private static int CountDashParameterSpans(", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.Contains("private static int FillQuadraticBezierDashSegments(", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.Contains("private static int FillCubicBezierDashSegments(", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Collections.Generic;", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<DashParameterSpan>", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<QuadraticBezierDashSegment>", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<CubicBezierDashSegment>", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly struct DashParameterSpan", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("parameterSpans = spans.ToArray()", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("segments.ToArray()", proGpuBezierSegmentGeometry, StringComparison.Ordinal);
        Assert.Contains("private static int CountArcDashSpans(", proGpuArcSegmentGeometry, StringComparison.Ordinal);
        Assert.Contains("private static int FillArcDashSegments(", proGpuArcSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Collections.Generic;", proGpuArcSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<ArcDashSegment>", proGpuArcSegmentGeometry, StringComparison.Ordinal);
        Assert.DoesNotContain("dashSegments = segments.ToArray()", proGpuArcSegmentGeometry, StringComparison.Ordinal);
        Assert.Contains("cmd.Edges3D is { } edges", proGpuAcisPipeline, StringComparison.Ordinal);
        Assert.Contains("ReadOnlySpan<Line3D>.Empty", proGpuAcisPipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("cmd.Edges3D ?? new List<Line3D>()", proGpuAcisPipeline, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestDeviceIndex.TryCreate(_context, index, out GpuHitTestDeviceIndex? deviceIndex)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestEngine.TryHitTestPoint(_context, _pipelineCache, _lastHitTestDeviceIndex, point, out result)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestEngine.TryHitTestPointAll(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestEngine.TryQueryBoundsAll(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestEngine.TryQueryEllipseAll(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public bool TryHitTestPoint(Vector2 point, out GpuHitTestResult result)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public bool TryHitTestPointAll(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public bool TryQueryHitTestBoundsAll(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public bool TryQueryHitTestEllipseAll(", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("public ProGpuHitTestIndex? LastGpuHitTestIndex => Compositor.LastHitTestIndex;", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public ProGpuHitTestDeviceIndex? LastGpuHitTestDeviceIndex => Compositor.LastHitTestDeviceIndex;", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public WpfGpuHitTestOwnerMap GpuHitTestOwnerMap { get; } = new();", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("using System.Buffers;", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProGpuHitTestResult[", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("stackalloc ProGpuHitTestResult[resultCapacity]", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("RentHitTestResults(resultCapacity, out rentedResults)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("RentHitTestResults(expandedCapacity, out rentedExpandedResults)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<ProGpuHitTestResult>.Shared.Rent(resultCapacity)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<ProGpuHitTestResult>.Shared.Return(rentedResults)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public bool TryHitTestPoint(Vector2 logicalPoint, out ProGpuHitTestResult result)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("return Compositor.TryHitTestPoint(logicalPoint, out result);", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public bool TryHitTestOwner(Vector2 logicalPoint, out object? owner, out ProGpuHitTestResult result)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("TryResolveFirstHitTestOwner(results, hitCount, out owner, out result)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("resolvedCount: 0", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public bool TryHitTestOwners(", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("Compositor.TryHitTestPointAll(logicalPoint, results, out int hitCount, out summary)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("ShouldRetryHitTestOwnerResolution(ownerCount, owners.Length, hitCount, summary, resultCapacity)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("GpuHitTestOwnerMap.TryGetOwner(results[i].Id, out object? owner)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainsHitTestOwner", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainsGeometryHitTestCandidateOwner", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("GetExpandedHitTestResultCapacity(summary, resultCapacity)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("summary.Hit > (uint)hitCount", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("ProGpuHitTestDeviceIndex.MaxHitResultCount", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public bool TryQueryHitTestBoundsOwners(", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public bool TryQueryHitTestBoundsCandidates(", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("public bool TryQueryHitTestEllipseCandidates(", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("new PortableGeometryHitTestCandidate(", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfGeometryHitTestCandidate", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("Compositor.TryQueryHitTestBoundsAll(logicalMin, logicalMax, results, out int hitCount, out summary)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("Compositor.TryQueryHitTestEllipseAll(logicalMin, logicalMax, results, out int hitCount, out summary)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("WpfGpuHitTestOwnerMap? hitTestOwnerMap = null", proGpuDrawingFrame, StringComparison.Ordinal);
        Assert.Contains("_hitTestOwnerMap?.Clear();", proGpuDrawingFrame, StringComparison.Ordinal);
        Assert.Contains("internal int GetOrCreateHitTestOwnerId(object ownerVisual)", proGpuDrawingFrame, StringComparison.Ordinal);
        Assert.Contains("ownerVisual.HitTestId = hitTestOwnerId;", proGpuRetainedCompositionCommandSink, StringComparison.Ordinal);
        Assert.Contains("visual.HitTestId = hitTestId;", proGpuRetainedCompositionCommandSink, StringComparison.Ordinal);
        Assert.Contains("RetainedSinkStampsSourceOwnerHitTestIdOnCommands", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("RetainedSinkPropagatesSourceOwnerHitTestIdToCacheScopes", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("RetainedSinkPropagatesSourceOwnerHitTestIdToEffectScopes", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("RenderPassEncoderSetViewport(\n            pass,\n            viewport.X,\n            viewport.Y,\n            viewport.Width,\n            viewport.Height", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CurrentCanvasPixelX => _explicitRenderTargetViewport.HasValue", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CurrentCanvasPixelY => _explicitRenderTargetViewport.HasValue", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CurrentCanvasPixelWidth => _explicitRenderTargetViewport.HasValue", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CurrentCanvasPixelHeight => _explicitRenderTargetViewport.HasValue", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ExplicitPhysicalRenderTargetPinsViewportToPhysicalFramebuffer", proGpuCompositorReviewTests, StringComparison.Ordinal);
        Assert.Contains("ExplicitRenderTargetViewportOffsetsLogicalSceneWithinFramebuffer", proGpuCompositorReviewTests, StringComparison.Ordinal);
        Assert.Contains("ExplicitRenderTargetViewportFeedsClientRectToCanvasPixelHelpers", proGpuCompositorReviewTests, StringComparison.Ordinal);
        Assert.Contains("ConstructorKeepsWpfLayerBoundsAndTransformLogicalForHighDpiFrames", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("HighDpiRetainedWpfLayerPreservesLogicalMarkerOrigin", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("HighDpiRetainedWpfLayerRendersAcrossPhysicalFramebuffer", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("HighDpiSourceDrawingLayerRendersAcrossPhysicalFramebuffer", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("LegacyRenderOverloadPreservesLogicalHighDpiFrameAcrossPhysicalFramebuffer", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("AttachSkipsFrozenFreezableChangedSubscription", proGpuInvalidationTrackerTests, StringComparison.Ordinal);
        Assert.Contains("NativeResizeDoesNotLetPortablePresentationSourceOverrideSilkClientSize", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("NativeResizeAppliesMaximizedWslgClientSizeInsteadOfStaleCache", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveCachedLogicalClientDimensionPrefersLivePortableSource", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveLogicalClientSizeUsesFramebufferFallbackWhenNativeSizeIsMissing", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderSurfaceGeometryUsesMonitorScaleOnlyWhenFramebufferIsMissing", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderSurfaceGeometryUsesFullPhysicalViewportWhenFramebufferHasExtraPixels", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderSurfaceGeometryKeepsFullViewportWhenOnlyFramebufferHeightGrows", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderSurfaceGeometryUsesFullRetinaViewportForMvpWindow", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("NormalizeInputEventForRenderSurfaceGeometryMapsPhysicalPointerCoordinatesToLogicalDips", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("SetClientSizeSynchronizesBoundPortablePresentationSourceImmediately", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("SetInitialClientSizeCachesLogicalSizeWithoutPortableSourceRelayout", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("SynchronizePortablePresentationSourceGeometryCachesHighDpiSurfaceGeometry", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("UpdatingPortablePresentationSourceClientSizeForcesFullWpfReplay", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("UpdatingPortablePresentationSourceDpiScaleForcesFullWpfReplay", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ShouldRenderFrameReturnsTrueWhenLogicalSizeChanges", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ShouldRenderFrameReturnsTrueWhenDpiScaleChanges", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("TryAttachSynchronizesInitialWindowShapeBeforeFirstRender", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostInputDoesNotUseReflectedPortableWindowInputHandler", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostInputDoesNotUseReflectedDispatcherQueueFallback", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostInputDoesNotUseCompatiblePresentationFrameworkInputArgsFallback", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("RenderWakeupDoesNotUseReflectedDispatcherFlushForFallbackQueue", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostDragDropDoesNotUseReflectedPortableWindowDropHandler", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostDragDropDoesNotUseReflectedPortableFileDropFallback", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostDragDropDoesNotUseReflectedPortableWindowActivationService", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("float dpiScale", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("Compositor.RenderScene(\n            SceneRootVisual,\n            logicalWidth,\n            logicalHeight,\n            pixelWidth,\n            pixelHeight,\n            renderTargetViewport,\n            dpiScale,\n            targetView)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("IWpfWindowDecorationService WindowDecorations", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("bool TryBeginDragMove(object window)", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("IWpfMessageBoxService MessageBoxes", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("string Show(WpfMessageBoxOptions options)", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("_window.Update += OnUpdate", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("window.Update -= OnUpdate", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private void OnUpdate(double deltaSeconds)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TryProcessDispatcherWorkWakeup();", proGpuHost, StringComparison.Ordinal);
    }

    [Fact]
    public void InputManagerUsesPortableDevicesOutsideWindows()
    {
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");
        var inputManagerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "InputManager.cs");
        var mouseDevicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "MouseDevice.cs");
        var portableSourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "PortablePresentationSource.cs");
        var uiElementPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "UIElement.cs");
        var visualTreeHelperPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "VisualTreeHelper.cs");
        var visualPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Visual.cs");
        var geometryHitTestParametersPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "GeometryHitTestParameters.cs");
        var portableKeyboardPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "PortableKeyboardDevice.cs");
        var portableMousePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "PortableMouseDevice.cs");

        var project = File.ReadAllText(projectPath);
        var inputManager = File.ReadAllText(inputManagerPath);
        var mouseDevice = File.ReadAllText(mouseDevicePath);
        var portableSource = File.ReadAllText(portableSourcePath);
        var uiElement = File.ReadAllText(uiElementPath);
        var visualTreeHelper = File.ReadAllText(visualTreeHelperPath);
        var visual = File.ReadAllText(visualPath);
        var geometryHitTestParameters = File.ReadAllText(geometryHitTestParametersPath);
        var portableKeyboard = File.ReadAllText(portableKeyboardPath);
        var portableMouse = File.ReadAllText(portableMousePath);

        Assert.Contains(@"<Compile Include=""System\Windows\Input\PortableKeyboardDevice.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Input\PortableMouseDevice.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableKeyboardDevice : KeyboardDevice", portableKeyboard, StringComparison.Ordinal);
        Assert.Contains("protected override KeyStates GetKeyStatesFromSystem(Key key)", portableKeyboard, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableMouseDevice : MouseDevice", portableMouse, StringComparison.Ordinal);
        Assert.Contains("internal override MouseButtonState GetButtonStateFromSystem(MouseButton mouseButton)", portableMouse, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", inputManager, StringComparison.Ordinal);
        Assert.Contains("new Win32KeyboardDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("new Win32MouseDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("new PortableKeyboardDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("new PortableMouseDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("if(OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)", inputManager, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("_doubleClickDeltaTime = 500;", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("inputSource?.CompositionTarget != null && !inputSource.CompositionTarget.IsDisposed", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("LocalHitTest(clientUnits, pt, inputSource, out enabledHit, out originalHit)", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("portableSource.TryHitTestOverride(rootPt, out enabledHit, out originalHit)", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(hitTestResult, this)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal Func<Point, object[]> HitTestAllOverride { get; set; }", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal PortableHitTestAllBufferOverride HitTestAllBufferOverride { get; set; }", portableSource, StringComparison.Ordinal);
        Assert.Contains("TryGetHitTestAllResults(rootPoint, out object[] hitTestResults, out int hitTestResultCount, out bool shouldReturnHitTestResults)", portableSource, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object>.Shared.Rent(HitTestOwnerBufferCapacity)", portableSource, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<object>.Shared.Return(hitTestResults, clearArray: true)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal Func<Point, Point, object[]> HitTestBoundsOverride { get; set; }", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal PortableGeometryHitTestBufferOverride HitTestBoundsBufferOverride { get; set; }", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal Func<Point, Point, object[]> HitTestEllipseBoundsOverride { get; set; }", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal PortableGeometryHitTestBufferOverride HitTestEllipseBoundsBufferOverride { get; set; }", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal bool TryInputHitTestOverride(UIElement reference, Point referencePoint, out DependencyObject candidate, out HitTestResult hitTestResult)", portableSource, StringComparison.Ordinal);
        Assert.Contains("IsInputHitTestVisibleDescendantOf(visualHit, reference)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal bool TryPointHitTestOverride(Visual reference, Point referencePoint, bool include2DOn3D, out HitTestResult hitTestResult)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal bool TryPointHitTestOverride(Visual reference, Point referencePoint, HitTestFilterCallback filterCallback, HitTestResultCallback resultCallback, out HitTestResultBehavior result)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal bool TryGeometryHitTestOverride(Visual reference, GeometryHitTestParameters geometryParams, HitTestFilterCallback filterCallback, HitTestResultCallback resultCallback, out HitTestResultBehavior result)", portableSource, StringComparison.Ordinal);
        Assert.Contains("geometryParams.PortableHitTestGeometryKind == PortableHitTestGeometryKind.AxisAlignedEllipse", portableSource, StringComparison.Ordinal);
        Assert.Contains("TryGetGeometryHitTestResults(", portableSource, StringComparison.Ordinal);
        Assert.Contains("TryGetGeometryHitTestBufferResults(HitTestEllipseBoundsBufferOverride, rootBounds, out hitTestResults, out hitTestResultCount)", portableSource, StringComparison.Ordinal);
        Assert.Contains("TryGetGeometryHitTestBufferResults(HitTestBoundsBufferOverride, rootBounds, out hitTestResults, out hitTestResultCount)", portableSource, StringComparison.Ordinal);
        Assert.Contains("HitTestEllipseBoundsOverride(rootBounds.TopLeft, rootBounds.BottomRight)", portableSource, StringComparison.Ordinal);
        Assert.Contains("HitTestBoundsOverride(rootBounds.TopLeft, rootBounds.BottomRight)", portableSource, StringComparison.Ordinal);
        Assert.Contains("for (int i = 0; i < hitTestResultCount; i++)", portableSource, StringComparison.Ordinal);
        Assert.Contains("TryTransformBounds(reference, _rootVisual, bounds, out Rect rootBounds, out bool preservesAxisAlignedBounds)", portableSource, StringComparison.Ordinal);
        Assert.Contains("preservesAxisAlignedBounds = IsAxisAlignedRectangle(topLeft, topRight, bottomRight, bottomLeft);", portableSource, StringComparison.Ordinal);
        Assert.Contains("TryGetPortableGeometryHitCandidate(hitTestResults[i], out Visual visualHit, out IntersectionDetail intersectionDetail)", portableSource, StringComparison.Ordinal);
        Assert.Contains("candidate is not PortableGeometryHitTestCandidate portableCandidate", portableSource, StringComparison.Ordinal);
        Assert.Contains("ToIntersectionDetail(portableCandidate.IntersectionDetail)", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate is GeometryHitTestResult", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate is Visual visual", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(\"VisualHit\"", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(\"IntersectionDetail\"", portableSource, StringComparison.Ordinal);
        Assert.Contains("IsPointHitVisibleByFilter(", portableSource, StringComparison.Ordinal);
        Assert.Contains("Visual[] path = ArrayPool<Visual>.Shared.Rent(16);", portableSource, StringComparison.Ordinal);
        Assert.Contains("Visual[] expandedPath = ArrayPool<Visual>.Shared.Rent(path.Length * 2);", portableSource, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<Visual>.Shared.Return(path, clearArray: true);", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<Visual>()", portableSource, StringComparison.Ordinal);
        Assert.Contains("private readonly PortableHitTestGeometryKind _portableHitTestGeometryKind;", geometryHitTestParameters, StringComparison.Ordinal);
        Assert.Contains("internal PortableHitTestGeometryKind PortableHitTestGeometryKind", geometryHitTestParameters, StringComparison.Ordinal);
        Assert.Contains("geometry is EllipseGeometry ellipseGeometry", geometryHitTestParameters, StringComparison.Ordinal);
        Assert.Contains("return PortableHitTestGeometryKind.AxisAlignedEllipse;", geometryHitTestParameters, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableHitTestGeometryKind", geometryHitTestParameters, StringComparison.Ordinal);
        Assert.Contains("portableSource.TryInputHitTestOverride(this, pt, out DependencyObject portableCandidate, out rawHitResult)", uiElement, StringComparison.Ordinal);
        Assert.Contains("private void PromoteInputHit(Point pt, DependencyObject candidate, out IInputElement enabledHit, out IInputElement rawHit, ref HitTestResult rawHitResult)", uiElement, StringComparison.Ordinal);
        Assert.Contains("portableSource.TryPointHitTestOverride(reference, point, include2DOn3D, out HitTestResult hitTestResult)", visualTreeHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("filterCallback == null &&", visual, StringComparison.Ordinal);
        Assert.Contains("portableSource.TryPointHitTestOverride(this, pointParams.HitPoint, filterCallback, resultCallback, out _)", visual, StringComparison.Ordinal);
        Assert.Contains("portableSource.TryGeometryHitTestOverride(this, geometryParams, filterCallback, resultCallback, out _)", visual, StringComparison.Ordinal);
        AssertGuardBefore(mouseDevice, "if (OperatingSystem.IsWindows() && source != null", "UnsafeNativeMethods.WindowFromPoint");
        AssertGuardBefore(mouseDevice, "if (OperatingSystem.IsWindows() && source != null", "SafeNativeMethods.IsWindowEnabled");
        AssertGuardBefore(mouseDevice, "portableSource.TryHitTestOverride(rootPt, out enabledHit, out originalHit)", "root.InputHitTest(rootPt, out enabledHit, out originalHit)");
        AssertGuardBefore(uiElement, "portableSource.TryInputHitTestOverride(this, pt, out DependencyObject portableCandidate, out rawHitResult)", "VisualTreeHelper.HitTest(this");
        AssertGuardBefore(visualTreeHelper, "portableSource.TryPointHitTestOverride(reference, point, include2DOn3D, out HitTestResult hitTestResult)", "return reference.HitTest(point, include2DOn3D);");
        AssertGuardBefore(visual, "portableSource.TryPointHitTestOverride(this, pointParams.HitPoint, filterCallback, resultCallback, out _)", "HitTestPoint(filterCallback, resultCallback, pointParams)");
        AssertGuardBefore(visual, "portableSource.TryGeometryHitTestOverride(this, geometryParams, filterCallback, resultCallback, out _)", "HitTestGeometry(filterCallback, resultCallback, geometryParams)");
        Assert.True(
            inputManager.IndexOf("if (OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < inputManager.IndexOf("new Win32KeyboardDevice(this)", StringComparison.Ordinal),
            "InputManager must branch before constructing Win32 input devices.");
    }

    [Fact]
    public void MediaSystemSkipsMilCoreStartupOnNonWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaSystem.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("if (!s_isWindows)", source, StringComparison.Ordinal);
        Assert.Contains("return false;", source, StringComparison.Ordinal);
        Assert.Contains("UnsafeNativeMethods.MilVersionCheck", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.MilCompositionEngine_InitializePartitionManager", source, StringComparison.Ordinal);
        Assert.Contains("UnsafeNativeMethods.RenderOptions_EnableHardwareAccelerationInRdp", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.MilCompositionEngine_DeinitializePartitionManager", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("return false;", StringComparison.Ordinal)
                < source.IndexOf("UnsafeNativeMethods.MilVersionCheck", StringComparison.Ordinal),
            "The non-Windows startup path must return disconnected before any MILCore version check or partition startup.");

        var connectChannelsIndex = source.IndexOf("internal static bool ConnectChannels", StringComparison.Ordinal);
        var connectGuardIndex = source.IndexOf("if (!s_isWindows)", connectChannelsIndex, StringComparison.Ordinal);
        var createChannelsIndex = source.IndexOf("mc.CreateChannels()", StringComparison.Ordinal);
        Assert.True(
            connectGuardIndex > connectChannelsIndex && connectGuardIndex < createChannelsIndex,
            "The non-Windows channel path must return before creating DUCE channels.");

        var shutdownIndex = source.IndexOf("internal static void Shutdown", StringComparison.Ordinal);
        var shutdownGuardIndex = source.IndexOf("if (!s_isWindows)", shutdownIndex, StringComparison.Ordinal);
        var deinitializeIndex = source.IndexOf("SafeNativeMethods.MilCompositionEngine_DeinitializePartitionManager", StringComparison.Ordinal);
        Assert.True(
            shutdownGuardIndex > shutdownIndex && shutdownGuardIndex < deinitializeIndex,
            "The non-Windows shutdown path must return before MILCore partition deinitialization.");
    }

    [Fact]
    public void HwndTargetDoesNotRegisterWin32MessagesOnNonWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "InterOp",
            "HwndTarget.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("if (!s_isWindows)", source, StringComparison.Ordinal);
        Assert.Contains("UnsafeNativeMethods.RegisterWindowMessage(\"UpdateWindowSettings\")", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (!s_isWindows)", StringComparison.Ordinal)
                < source.IndexOf("UnsafeNativeMethods.RegisterWindowMessage(\"UpdateWindowSettings\")", StringComparison.Ordinal),
            "The non-Windows guard must run before registering HwndTarget Win32 window messages.");
        Assert.True(
            source.IndexOf("throw new PlatformNotSupportedException", StringComparison.Ordinal)
                < source.IndexOf("SafeNativeMethods.GetCurrentSessionId()", StringComparison.Ordinal),
            "The non-Windows constructor failure must run before Win32 session or HWND initialization.");
    }

    [Fact]
    public void PresentationFrameworkHasPortableWindowActivationBoundary()
    {
        var windowPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Window.cs");
        var applicationPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Application.cs");
        var activationServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableWindowActivationService.cs");
        var windowChromeWorkerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Shell",
            "WindowChromeWorker.cs");
        var portableInputPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableInputEventArgs.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");

        var window = File.ReadAllText(windowPath);
        var application = File.ReadAllText(applicationPath);
        var activationService = File.ReadAllText(activationServicePath);
        var windowChromeWorker = File.ReadAllText(windowChromeWorkerPath);
        var portableInput = File.ReadAllText(portableInputPath);
        var project = File.ReadAllText(projectPath);

        Assert.Contains("internal sealed class PortableInputEventArgs", portableInput, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableInputEventKind", portableInput, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableMouseButton", portableInput, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableInputModifiers", portableInput, StringComparison.Ordinal);
        Assert.Contains("public bool Handled { get; set; }", portableInput, StringComparison.Ordinal);

        Assert.Contains("internal static class PortableWindowActivationService", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void Register", activationService, StringComparison.Ordinal);
        Assert.Contains("Func<object, object> activate", activationService, StringComparison.Ordinal);
        Assert.Contains("!OperatingSystem.IsWindows()", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryActivate(Window window, out object activation)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, object> setWindowState", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetWindowState(object activation, WindowState windowState)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, string> setTitle", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetTitle(object activation, string title)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, double, double> setClientSize", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetClientSize(object activation, double width, double height)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, double, double> setPosition", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetPosition(object activation, double left, double top)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, bool> setTopmost", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetTopmost(object activation, bool topmost)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, object, object> setWindowBorder", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetWindowBorder(object activation, ResizeMode resizeMode, WindowStyle windowStyle)", activationService, StringComparison.Ordinal);
        Assert.Contains("Func<object, bool> dragMove", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryDragMove(object activation)", activationService, StringComparison.Ordinal);
        Assert.Contains("Func<object, IntPtr> getHandle = null", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static IntPtr GetHandle(object activation)", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetActivationState(Window window, bool isActive)", activationService, StringComparison.Ordinal);
        Assert.Contains("NotifyPortableInputProvidersDeactivated(window)", activationService, StringComparison.Ordinal);
        Assert.Contains("source.GetInputProvider(typeof(KeyboardDevice))?.NotifyDeactivate()", activationService, StringComparison.Ordinal);
        Assert.Contains("source.GetInputProvider(typeof(MouseDevice))?.NotifyDeactivate()", activationService, StringComparison.Ordinal);
        Assert.Contains("window.HandleActivate(isActive)", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void ProcessInput(Window window, PortableInputEventArgs input)", activationService, StringComparison.Ordinal);
        Assert.Contains("window.TryBeginPortableChromeDrag(mouseRootPoint)", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow)", activationService, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(application.Dispatcher, typedWindow.Dispatcher)", activationService, StringComparison.Ordinal);
        Assert.Contains("!application.Dispatcher.CheckAccess()", activationService, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(application.MainWindow, typedWindow)", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryCloseWindow(object window, out PortableWindowCloseResult result)", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.Close();", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.IsDisposed", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryBeginInvokeInput(object window, Action callback)", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.Dispatcher.CheckAccess()", activationService, StringComparison.Ordinal);
        Assert.Contains("typedWindow.Dispatcher.BeginInvoke(DispatcherPriority.Input, callback);", activationService, StringComparison.Ordinal);
        Assert.Contains("public bool TryProcessInputEvent(object window, PortableWindowInputEvent input)", activationService, StringComparison.Ordinal);
        Assert.Contains("(PortableInputEventKind)input.Kind", activationService, StringComparison.Ordinal);
        Assert.Contains("(PortableMouseButton)input.Button", activationService, StringComparison.Ordinal);
        Assert.Contains("(PortableInputModifiers)input.Modifiers", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.ProcessInput(typedWindow, mappedInput);", activationService, StringComparison.Ordinal);
        Assert.Contains("input.Handled = mappedInput.Handled;", activationService, StringComparison.Ordinal);
        Assert.Contains("PresentationSource.CriticalFromVisual(window)", activationService, StringComparison.Ordinal);
        Assert.Contains("input.Handled = ProcessInput(source, window, input)", activationService, StringComparison.Ordinal);
        Assert.Contains("InputManager.UnsecureCurrent", activationService, StringComparison.Ordinal);
        Assert.Contains("new RawKeyboardInputReport", activationService, StringComparison.Ordinal);
        Assert.Contains("new RawTextInputReport", activationService, StringComparison.Ordinal);
        Assert.Contains("new RawMouseInputReport", activationService, StringComparison.Ordinal);
        Assert.Contains("InputManager.PreviewInputReportEvent", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableKeyboardDevice", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableMouseDevice", activationService, StringComparison.Ordinal);
        Assert.Contains("RawMouseActions mouseActivation = GetMouseActivationAction(inputManager, mouseInputSource)", activationService, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(inputManager.PrimaryMouseDevice?.ActiveSource, source)", activationService, StringComparison.Ordinal);
        Assert.Contains("mouseActivation | mouseUpAction", activationService, StringComparison.Ordinal);
        Assert.DoesNotContain("RawMouseActions.Activate | mouseUpAction", activationService, StringComparison.Ordinal);
        Assert.DoesNotContain("RawMouseActions.Activate | RawMouseActions.AbsoluteMove | mouseUpAction", activationService, StringComparison.Ordinal);
        Assert.Contains("Mouse.MouseWheelDeltaForOneLine", activationService, StringComparison.Ordinal);
        Assert.DoesNotContain("Routed portable input is implemented in a later InputManager slice", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryRun(Window window)", activationService, StringComparison.Ordinal);
        Assert.Contains("window.PortableWindowActivation", activationService, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortableInputEventArgs.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortableWindowActivationService.cs"" />", project, StringComparison.Ordinal);

        Assert.Contains("private object              _portableWindowActivation", window, StringComparison.Ordinal);
        Assert.Contains("internal object PortableWindowActivation", window, StringComparison.Ordinal);
        Assert.Contains("internal void HandlePortableInput(PortableInputEventArgs input)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.ProcessInput(this, input)", window, StringComparison.Ordinal);
        Assert.Contains("TryCreatePortableWindowDuringShow()", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryActivate(this, out object activation)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetActivationState(this, true)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.Show(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.Hide(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetWindowState(_portableWindowActivation, windowState)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetTitle(_portableWindowActivation, Title)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetClientSize(_portableWindowActivation, Width, height)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetClientSize(_portableWindowActivation, width, Height)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetPosition(_portableWindowActivation, leftLogicalUnits, topLogicalUnits)", window, StringComparison.Ordinal);
        Assert.Contains("private void UpdatePortablePositionOnTopLeftChange(double leftLogicalUnits, double topLogicalUnits)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetTopmost(_portableWindowActivation, topmost)", window, StringComparison.Ordinal);
        Assert.Contains("internal void SetPortableCustomChrome(bool enabled)", window, StringComparison.Ordinal);
        Assert.Contains("internal bool TryBeginPortableChromeDrag(Point mousePosition)", window, StringComparison.Ordinal);
        Assert.Contains("chromeWorker.IsPortableCaptionHit(mousePosition)", window, StringComparison.Ordinal);
        Assert.Contains("internal bool IsPortableCaptionHit(Point mousePosition)", windowChromeWorker, StringComparison.Ordinal);
        Assert.Contains("WindowChrome.GetIsHitTestVisibleInChrome(inputElement)", windowChromeWorker, StringComparison.Ordinal);
        Assert.Contains("WindowChrome.GetResizeGripDirection(inputElement)", windowChromeWorker, StringComparison.Ordinal);
        Assert.Contains("GetPortableWindowStyle()", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.GetHandle(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("&& !w.IsPortableWindowActive", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryDragMove(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("if (PortableWindowActivationService.IsEnabled)", window, StringComparison.Ordinal);
        Assert.Contains("return ShowPortableDialog();", window, StringComparison.Ordinal);
        Assert.Contains("private Nullable<bool> ShowPortableDialog()", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryRun(this);", window, StringComparison.Ordinal);
        Assert.Contains("ComponentDispatcher.PushModal();", window, StringComparison.Ordinal);
        Assert.Contains("ComponentDispatcher.PopModal();", window, StringComparison.Ordinal);
        Assert.True(
            window.IndexOf("ComponentDispatcher.PushModal();", StringComparison.Ordinal)
                < window.IndexOf("PortableWindowActivationService.TryRun(this);", StringComparison.Ordinal),
            "Portable Window.ShowDialog must enter modal state before running the dialog native host.");
        Assert.Contains("if (_showingAsDialog)", window, StringComparison.Ordinal);
        Assert.Contains("DoDialogHide();", window, StringComparison.Ordinal);
        Assert.Contains("if (IsPortableWindowActive)", window, StringComparison.Ordinal);
        AssertGuardBefore(window, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SendMessage( Handle, WindowMessage.WM_SYSCOMMAND");
        Assert.Contains("ClosePortableWindowActivation();", window, StringComparison.Ordinal);
        Assert.Contains("private bool IsPortableWindowActive", window, StringComparison.Ordinal);
        Assert.Contains("if (value != null && value.IsSourceWindowNull && !value.IsPortableWindowActive)", window, StringComparison.Ordinal);
        Assert.Contains("private bool IsLayoutSourceUnavailable", window, StringComparison.Ordinal);
        Assert.Contains("return !IsPortableWindowActive && (IsSourceWindowNull || IsCompositionTargetInvalid)", window, StringComparison.Ordinal);
        Assert.Contains("private Size GetWindowFrameSizeInMeasureUnits()", window, StringComparison.Ordinal);
        Assert.Contains("private Size GetWindowSizeInMeasureUnits()", window, StringComparison.Ordinal);
        Assert.Contains("if (IsPortableWindowActive)", window, StringComparison.Ordinal);
        Assert.Contains("mm.maxWidth = MinWidth > MaxWidth ? MinWidth : MaxWidth", window, StringComparison.Ordinal);
        Assert.Contains("mm.maxHeight = MinHeight > MaxHeight ? MinHeight : MaxHeight", window, StringComparison.Ordinal);
        Assert.Contains("return IsPortableWindowActive ? new Size(0, 0) : GetHwndNonClientAreaSizeInMeasureUnits()", window, StringComparison.Ordinal);
        Assert.Contains("return new Size(ToNonNegativeFiniteSize(width), ToNonNegativeFiniteSize(height))", window, StringComparison.Ordinal);
        Assert.Contains("frameworkAvailableSize = GetPortableMeasureSizeInMeasureUnits(frameworkAvailableSize);", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Size portableFallbackSize = SizeToContent == SizeToContent.Manual", window, StringComparison.Ordinal);
        Assert.Contains("arrangeBounds = GetPortableArrangeSizeInMeasureUnits(DesiredSize, arrangeBounds);", window, StringComparison.Ordinal);
        Assert.Contains("private Size GetPortableMeasureSizeInMeasureUnits(Size windowSize)", window, StringComparison.Ordinal);
        Assert.Contains("? Double.PositiveInfinity", window, StringComparison.Ordinal);
        Assert.Contains("private Size GetPortableArrangeSizeInMeasureUnits(Size measuredSize, Size fallbackSize)", window, StringComparison.Ordinal);
        Assert.Contains("private void UpdatePortableSizeToContentFromLayout(Size fallbackSize)", window, StringComparison.Ordinal);
        Assert.Contains("_updatingPortableSizeToContent", window, StringComparison.Ordinal);
        Assert.Contains("_refreshingPortableRootVisualState", window, StringComparison.Ordinal);
        Assert.Contains("UpdatePortableSizeToContentFromLayout(arrangeBounds);", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetClientSize(_portableWindowActivation, arrangeSize.Width, arrangeSize.Height)", window, StringComparison.Ordinal);
        Assert.Contains("if (IsPortableWindowActive)", window, StringComparison.Ordinal);
        Assert.Contains("RefreshPortableRootVisualState();", window, StringComparison.Ordinal);
        Assert.Contains("private void RefreshPortableRootVisualState()", window, StringComparison.Ordinal);
        Assert.Contains("ApplyTemplate();", window, StringComparison.Ordinal);
        Assert.Contains("UpdateIsVisibleCache();", window, StringComparison.Ordinal);
        Assert.Contains("InvalidateForceInheritPropertyOnChildren(IsVisibleProperty);", window, StringComparison.Ordinal);
        Assert.Contains("Size windowSize = GetWindowSizeInMeasureUnits();", window, StringComparison.Ordinal);
        Assert.Contains("Size measureSize = GetPortableMeasureSizeInMeasureUnits(windowSize);", window, StringComparison.Ordinal);
        Assert.Contains("Measure(measureSize);", window, StringComparison.Ordinal);
        Assert.Contains("Size arrangeSize = GetPortableArrangeSizeInMeasureUnits(DesiredSize, windowSize);", window, StringComparison.Ordinal);
        Assert.Contains("Arrange(new Rect(arrangeSize));", window, StringComparison.Ordinal);
        Assert.Contains("UpdateLayout();", window, StringComparison.Ordinal);
        Assert.Contains("private void RefreshPortableInheritedVisibility()", window, StringComparison.Ordinal);
        Assert.Contains("if (Content is UIElement contentElement)", window, StringComparison.Ordinal);
        Assert.Contains("UIElement.SynchronizeForceInheritProperties(contentElement, null, null, this)", window, StringComparison.Ordinal);
        Assert.Contains("contentElement.InvalidateForceInheritPropertyOnChildren(IsVisibleProperty)", window, StringComparison.Ordinal);
        Assert.True(
            window.IndexOf("RefreshPortableRootVisualState();", StringComparison.Ordinal)
                < window.IndexOf("PortableWindowActivationService.Show(_portableWindowActivation)", StringComparison.Ordinal),
            "Portable Window.Show must refresh the WPF root template, inherited visibility, and layout before showing the native host.");
        Assert.True(
            window.IndexOf("if (TryCreatePortableWindowDuringShow())", StringComparison.Ordinal)
                < window.IndexOf("CreateSourceWindow(true);", StringComparison.Ordinal),
            "Window.Show must try the portable activation service before falling back to HWND creation.");

        Assert.Contains("if (!OperatingSystem.IsWindows())", application, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.IsEnabled", application, StringComparison.Ordinal);
        Assert.Contains("FlushPortableDispatcherOperations(DispatcherPriority.Send)", application, StringComparison.Ordinal);
        Assert.Contains("FlushPortableDispatcherOperations(DispatcherPriority.ApplicationIdle)", application, StringComparison.Ordinal);
        Assert.Contains("private void FlushPortableDispatcherOperations(DispatcherPriority markerPriority)", application, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(250)", application, StringComparison.Ordinal);
        Assert.Contains("wnd.Show();", application, StringComparison.Ordinal);
        Assert.Contains("wnd.Visibility = Visibility.Visible;", application, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.PushFrame(frame)", application, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryRun(MainWindow)", application, StringComparison.Ordinal);
        Assert.Contains("if (!_appIsShutdown)", application, StringComparison.Ordinal);
        Assert.True(
            application.IndexOf("if (!OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < application.IndexOf("new HwndWrapper", StringComparison.Ordinal),
            "Application.Run must skip the parking HWND before any HwndWrapper is created on non-Windows.");
        Assert.True(
            application.IndexOf("FlushPortableDispatcherOperations(DispatcherPriority.Send)", StringComparison.Ordinal)
                < application.IndexOf("window.Show();", StringComparison.Ordinal),
            "Application.Run must service queued startup work before synchronously showing the portable startup window.");
        Assert.True(
            application.IndexOf("window.Show();", StringComparison.Ordinal)
                < application.IndexOf("PortableWindowActivationService.TryRun(MainWindow)", StringComparison.Ordinal),
            "Application.Run must synchronously show the startup window before handing ownership to the portable native run loop.");
        int portableTryRun = application.IndexOf(
            "PortableWindowActivationService.TryRun(MainWindow)",
            StringComparison.Ordinal);
        int applicationIdleFlush = application.IndexOf(
            "FlushPortableDispatcherOperations(DispatcherPriority.ApplicationIdle)",
            StringComparison.Ordinal);
        Assert.True(
            portableTryRun < applicationIdleFlush,
            "Application.Run must leave normal and idle app work for the portable native run loop, then service queued shutdown work after it exits.");
        Assert.True(
            application.IndexOf("PortableWindowActivationService.TryRun(MainWindow)", StringComparison.Ordinal)
                < application.IndexOf("RunDispatcher(null);", StringComparison.Ordinal),
            "Application.Run must use the portable native run loop before falling back to WPF Dispatcher.Run.");
    }

    [Fact]
    public void PresentationFrameworkMessageBoxUsesPortableServiceOutsideWindows()
    {
        var messageBoxPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "MessageBox.cs");
        var messageBoxServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableMessageBoxService.cs");
        var moduleInitializerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "ModuleInitializer.cs");
        var portableWpfServiceRegistryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Wpf.Interop",
            "PortableWpfServiceRegistry.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var processMessageBoxServicePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "ProcessWpfMessageBoxService.cs");
        var runtimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");
        var applicationRunHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");
        var sdkRuntimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");

        var messageBox = File.ReadAllText(messageBoxPath);
        var messageBoxService = File.ReadAllText(messageBoxServicePath);
        var moduleInitializer = File.ReadAllText(moduleInitializerPath);
        var portableWpfServiceRegistry = File.ReadAllText(portableWpfServiceRegistryPath);
        var project = File.ReadAllText(projectPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var processMessageBoxService = File.ReadAllText(processMessageBoxServicePath);
        var runtimeHarness = File.ReadAllText(runtimeHarnessPath);
        var applicationRunHarness = File.ReadAllText(applicationRunHarnessPath);
        var sdkRuntimeHarness = File.ReadAllText(sdkRuntimeHarnessPath);

        Assert.Contains(@"<Compile Include=""System\Windows\PortableMessageBoxService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""ModuleInitializer.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal readonly struct PortableMessageBoxRequest", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableMessageBoxService", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Wpf.Interop;", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("internal static void RegisterPortableInteropService()", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.RegisterMessageBoxService(s_registrar)", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("private sealed class MessageBoxServiceRegistrar : IPortableMessageBoxServiceRegistrar", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("PortableMessageBoxService.RegisterPortableInteropService();", moduleInitializer, StringComparison.Ordinal);
        Assert.Contains("public interface IPortableMessageBoxServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortableMessageBoxRequest", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public object? Owner { get; }", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("IDisposable Register(Func<PortableMessageBoxRequest, string?> show)", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("MessageBoxServiceRegistered", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetMessageBoxService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<object, object> show)", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("CreateInteropRequest(", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("request.Owner,", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("Func<ProGPU.Wpf.Interop.PortableMessageBoxRequest, string>", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryShow(", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("return MessageBox.GetPortableFallbackResult(DefaultResult, Button)", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("return !s_isWindows && Volatile.Read(ref s_show) != null", messageBoxService, StringComparison.Ordinal);

        Assert.Contains("return ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, options)", messageBox, StringComparison.Ordinal);
        Assert.Contains("GetMessageBoxOwnerHandle(owner)", messageBox, StringComparison.Ordinal);
        Assert.Contains("if (ownerHandle == IntPtr.Zero && OperatingSystem.IsWindows())", messageBox, StringComparison.Ordinal);
        Assert.Contains("if (!OperatingSystem.IsWindows())", messageBox, StringComparison.Ordinal);
        Assert.Contains("PortableMessageBoxService.TryShow(", messageBox, StringComparison.Ordinal);
        Assert.Contains("return GetPortableFallbackResult(defaultResult, button)", messageBox, StringComparison.Ordinal);
        Assert.Contains("return new WindowInteropHelper(owner).Handle", messageBox, StringComparison.Ordinal);
        Assert.True(
            messageBox.IndexOf("if (!OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < messageBox.IndexOf("UnsafeNativeMethods.MessageBox", StringComparison.Ordinal),
            "MessageBox.ShowCore must try the portable service before the Win32 MessageBox call.");

        Assert.Contains("TryRegisterPresentationFrameworkMessageBoxService()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.MessageBoxServiceRegistered += OnMessageBoxServiceRegistered", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static void OnMessageBoxServiceRegistered(IPortableMessageBoxServiceRegistrar service)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.TryGetMessageBoxService(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableMessageBoxServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRegisterPresentationFrameworkMessageBoxServiceByReflection", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Func<object, object>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ShowPortableMessageBox", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ShowPortableMessageBox(PortableMessageBoxRequest request)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("new WpfMessageBoxOptions", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.MessageBoxes.Show(options)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FallbackResult = request.FallbackResult", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadRequestString", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadRequestValueName", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadRequestProperty", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static string CreateAppleScriptButtonList(IReadOnlyList<string> labels)", processMessageBoxService, StringComparison.Ordinal);
        Assert.Contains("var resultByLabel = new Dictionary<string, string>(labels.Length, StringComparer.OrdinalIgnoreCase);", processMessageBoxService, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<string> CreateTail(IReadOnlyList<string> values)", processMessageBoxService, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Linq;", processMessageBoxService, StringComparison.Ordinal);
        Assert.DoesNotContain("Labels.Skip(2).ToArray()", processMessageBoxService, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Skip(2).ToArray()", processMessageBoxService, StringComparison.Ordinal);
        Assert.DoesNotContain("labels.Select(", processMessageBoxService, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToDictionary(", processMessageBoxService, StringComparison.Ordinal);

        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(presentationFramework, window)", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox no-owner default result", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox owner fallback result", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", runtimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("RegisterPortableMessageBox(presentationFramework)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(_presentationFramework, typedActivation.Window)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox owner fallback result", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", applicationRunHarness, StringComparison.Ordinal);

        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("RegisterPortableMessageBox(presentationFramework)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(presentationFramework, window)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(_presentationFramework, typedActivation.Window)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox SDK no-owner default result", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox SDK owner fallback result", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", sdkRuntimeHarness, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationFrameworkBrowserLaunchUsesPortableServiceOutsideWindows()
    {
        var appSecurityManagerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "AppModel",
            "AppSecurityManager.cs");
        var launcherServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableLauncherService.cs");
        var moduleInitializerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "ModuleInitializer.cs");
        var portableWpfServiceRegistryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Wpf.Interop",
            "PortableWpfServiceRegistry.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");

        var appSecurityManager = File.ReadAllText(appSecurityManagerPath);
        var launcherService = File.ReadAllText(launcherServicePath);
        var moduleInitializer = File.ReadAllText(moduleInitializerPath);
        var portableWpfServiceRegistry = File.ReadAllText(portableWpfServiceRegistryPath);
        var project = File.ReadAllText(projectPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);

        Assert.Contains(@"<Compile Include=""System\Windows\PortableLauncherService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal readonly struct PortableLaunchRequest", launcherService, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableLauncherService", launcherService, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Wpf.Interop;", launcherService, StringComparison.Ordinal);
        Assert.Contains("internal static void RegisterPortableInteropService()", launcherService, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.RegisterLauncherService(s_registrar)", launcherService, StringComparison.Ordinal);
        Assert.Contains("private sealed class LauncherServiceRegistrar : IPortableLauncherServiceRegistrar", launcherService, StringComparison.Ordinal);
        Assert.Contains("PortableLauncherService.RegisterPortableInteropService();", moduleInitializer, StringComparison.Ordinal);
        Assert.Contains("public interface IPortableLauncherServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortableLaunchRequest", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("IDisposable Register(Func<PortableLaunchRequest, bool> launch)", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetLauncherService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<object, bool> launch)", launcherService, StringComparison.Ordinal);
        Assert.Contains("CreateInteropRequest(", launcherService, StringComparison.Ordinal);
        Assert.Contains("Func<ProGPU.Wpf.Interop.PortableLaunchRequest, bool>", launcherService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryLaunch(Uri uri, string targetFrame, bool isTopLevel, out bool launched)", launcherService, StringComparison.Ordinal);
        Assert.Contains("return !s_isWindows && Volatile.Read(ref s_launch) != null", launcherService, StringComparison.Ordinal);

        Assert.Contains("if (!OperatingSystem.IsWindows() && isSafeLaunch)", appSecurityManager, StringComparison.Ordinal);
        Assert.Contains("PortableLauncherService.TryLaunch(destinationUri, targetName, fIsTopLevel", appSecurityManager, StringComparison.Ordinal);
        Assert.Contains("PortableLauncherService.TryLaunch(uri, null, true", appSecurityManager, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(SR.FailToLaunchDefaultBrowser)", appSecurityManager, StringComparison.Ordinal);
        Assert.True(
            appSecurityManager.IndexOf("if (!OperatingSystem.IsWindows() && isSafeLaunch)", StringComparison.Ordinal)
                < appSecurityManager.IndexOf("UnsafeNativeMethods.ShellExecute", StringComparison.Ordinal),
            "Safe browser launch must try the portable launcher before the Win32 ShellExecute path.");
        Assert.True(
            appSecurityManager.IndexOf("if (!OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < appSecurityManager.IndexOf("UnsafeNativeMethods.ShellExecuteInfo", StringComparison.Ordinal),
            "Default browser launch must avoid ShellExecuteEx on non-Windows.");

        Assert.Contains("TryRegisterPresentationFrameworkLauncherService()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.TryGetLauncherService(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableLauncherServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRegisterPresentationFrameworkLauncherServiceByReflection", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Func<object, bool>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("LaunchPortableUri", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("LaunchPortableUri(PortableLaunchRequest request)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.Launcher", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains(".OpenUriAsync(request.Uri)", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadRequestUri", proGpuActivation, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationCoreClipboardUsesPortableServiceOutsideWindows()
    {
        var clipboardPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "clipboard.cs");
        var clipboardServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "PortableClipboardService.cs");
        var moduleInitializerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "ModuleInitializer.cs");
        var portableWpfServiceRegistryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Wpf.Interop",
            "PortableWpfServiceRegistry.cs");
        var portableManagedDataObjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "PortableManagedDataObject.cs");
        var dataObjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "dataobject.cs");
        var portableClipboardServiceTestsPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "tests",
            "UnitTests",
            "PresentationCore.Tests",
            "System",
            "Windows",
            "PortableClipboardServiceTests.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");
        var runtimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");
        var applicationRunHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");
        var sdkRuntimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var portableBootstrapPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "targets",
            "ProGPU.Wpf.Sdk.PortableBootstrap.cs");

        var clipboard = File.ReadAllText(clipboardPath);
        var clipboardService = File.ReadAllText(clipboardServicePath);
        var moduleInitializer = File.ReadAllText(moduleInitializerPath);
        var portableWpfServiceRegistry = File.ReadAllText(portableWpfServiceRegistryPath);
        var portableManagedDataObject = File.ReadAllText(portableManagedDataObjectPath);
        var dataObject = File.ReadAllText(dataObjectPath);
        var portableClipboardServiceTests = File.ReadAllText(portableClipboardServiceTestsPath);
        var project = File.ReadAllText(projectPath);
        var runtimeHarness = File.ReadAllText(runtimeHarnessPath);
        var applicationRunHarness = File.ReadAllText(applicationRunHarnessPath);
        var sdkRuntimeHarness = File.ReadAllText(sdkRuntimeHarnessPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var portableBootstrap = File.ReadAllText(portableBootstrapPath);

        Assert.Contains(@"<Compile Include=""System\Windows\PortableClipboardService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortableManagedDataObject.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableClipboardService", clipboardService, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Wpf.Interop;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static void RegisterPortableInteropService()", clipboardService, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.RegisterClipboardService(s_registrar)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("private sealed class ClipboardServiceRegistrar : IPortableClipboardServiceRegistrar", clipboardService, StringComparison.Ordinal);
        Assert.Contains("PortableClipboardService.RegisterPortableInteropService();", moduleInitializer, StringComparison.Ordinal);
        Assert.Contains("public interface IPortableClipboardServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetClipboardService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<string?> getText, Action<string?> setText)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryGetDataObject(out IDataObject? dataObject)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetDataObject(IDataObject dataObject, bool copy)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetData(string format, object data, bool autoConvert, bool copy)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetFileDropList(StringCollection fileDropList)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetObject(object data, bool copy)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryIsCurrent(IDataObject data, out bool isCurrent)", clipboardService, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class PortableDataObject", clipboardService, StringComparison.Ordinal);
        Assert.Contains("new PortableManagedDataObject()", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableManagedDataObject : ITypedDataObject", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<string, Entry> _data", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("using System.Text.Json;", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("internal void SetDataAsJson<T>(string format, T data)", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private static bool TryDeserializeJsonPayload<T>", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private sealed class JsonPayload", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("public bool TryGetData<T>", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private readonly ITypedDataObject? _portableData;", dataObject, StringComparison.Ordinal);
        Assert.Contains("_portableData = new PortableManagedDataObject();", dataObject, StringComparison.Ordinal);
        Assert.Contains("portableManagedData.SetDataAsJson(format, data);", dataObject, StringComparison.Ordinal);
        Assert.Contains("return !s_isWindows;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("s_dataObject = dataObject;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("private static bool s_hasManagedClipboardState;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("s_hasManagedClipboardState = true;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref s_setText)?.Invoke(hasUnicodeText ? text : null);", clipboardService, StringComparison.Ordinal);
        Assert.Contains("ClearKeepsManagedStateAuthoritativeOverStaleNativeText", portableClipboardServiceTests, StringComparison.Ordinal);
        Assert.Contains("SetFileDropListClearsNativeTextMirror", portableClipboardServiceTests, StringComparison.Ordinal);

        Assert.Contains("if (PortableClipboardService.TryClear())", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TryFlush())", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TryGetDataObject(out IDataObject? portableDataObject))", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TryIsCurrent(data, out bool isCurrent))", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TrySetObject(data, copy))", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TrySetFileDropList(fileDropList))", clipboard, StringComparison.Ordinal);
        Assert.Contains("bool autoConvert = IsDataFormatAutoConvert(format);", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TrySetData(format, data, autoConvert, copy: true))", clipboard, StringComparison.Ordinal);
        Assert.Contains("private static void SetOleDataObject(object data, bool copy)", clipboard, StringComparison.Ordinal);
        Assert.Contains("private static void SetOleDataInternal(string format, object data, bool autoConvert)", clipboard, StringComparison.Ordinal);
        Assert.True(
            clipboard.IndexOf("PortableClipboardService.TryGetDataObject", StringComparison.Ordinal)
                < clipboard.IndexOf("ClipboardCore.GetDataObject", StringComparison.Ordinal),
            "Clipboard.GetDataObject must try the portable service before the OLE clipboard path.");
        Assert.True(
            clipboard.IndexOf("PortableClipboardService.TrySetObject", StringComparison.Ordinal)
                < clipboard.IndexOf("ClipboardCore.SetData(dataObject, copy)", StringComparison.Ordinal),
            "Clipboard.SetDataObject must try the portable service before the OLE clipboard path.");

        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard data object unicode text", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", runtimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard current data object", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", applicationRunHarness, StringComparison.Ordinal);

        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK data object unicode text", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK current data object", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableRichClipboardFormats(presentationCore)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK file drop state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK custom data state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK audio state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK image data format state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableJsonDataObject(presentationCore)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("PortableClipboardJsonPayload", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK JSON DataObject typed retrieval state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK JSON clipboard typed retrieval state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", sdkRuntimeHarness, StringComparison.Ordinal);

        Assert.Contains("TryRegisterPresentationCoreClipboardService", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.TryGetClipboardService(", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableClipboardServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRegisterPresentationCoreClipboardServiceByReflection", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Func<string?>), typeof(Action<string?>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("GetPortableClipboardText", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("SetPortableClipboardText", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.Clipboard", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("return string.IsNullOrEmpty(text) ? null : text", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService()", portableBootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(Clipboard).Assembly", portableBootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationFrameworkFileDialogsUsePortableServiceOutsideWindows()
    {
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var commonDialogPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "CommonDialog.cs");
        var commonItemDialogPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "CommonItemDialog.cs");
        var fileDialogPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "FileDialog.cs");
        var servicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "PortableFileDialogService.cs");
        var moduleInitializerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "ModuleInitializer.cs");
        var portableWpfServiceRegistryPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Wpf.Interop",
            "PortableWpfServiceRegistry.cs");
        var activationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var processFileDialogServicePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "ProcessWpfFileDialogService.cs");
        var runtimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");
        var applicationRunHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");
        var sdkRuntimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");

        var project = File.ReadAllText(projectPath);
        var commonDialog = File.ReadAllText(commonDialogPath);
        var commonItemDialog = File.ReadAllText(commonItemDialogPath);
        var fileDialog = File.ReadAllText(fileDialogPath);
        var service = File.ReadAllText(servicePath);
        var moduleInitializer = File.ReadAllText(moduleInitializerPath);
        var portableWpfServiceRegistry = File.ReadAllText(portableWpfServiceRegistryPath);
        var activation = File.ReadAllText(activationPath);
        var processFileDialogService = File.ReadAllText(processFileDialogServicePath);
        var runtimeHarness = File.ReadAllText(runtimeHarnessPath);
        var applicationRunHarness = File.ReadAllText(applicationRunHarnessPath);
        var sdkRuntimeHarness = File.ReadAllText(sdkRuntimeHarnessPath);

        Assert.Contains(@"<Compile Include=""Microsoft\Win32\PortableFileDialogService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableFileDialogService", service, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Wpf.Interop;", service, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", service, StringComparison.Ordinal);
        Assert.Contains("internal static void RegisterPortableInteropService()", service, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.RegisterFileDialogService(s_registrar)", service, StringComparison.Ordinal);
        Assert.Contains("private sealed class FileDialogServiceRegistrar : IPortableFileDialogServiceRegistrar", service, StringComparison.Ordinal);
        Assert.Contains("PortableFileDialogService.RegisterPortableInteropService();", moduleInitializer, StringComparison.Ordinal);
        Assert.Contains("public interface IPortableFileDialogServiceRegistrar", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortableFileDialogRequest", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public sealed class PortableFileDialogResult", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("IDisposable Register(Func<PortableFileDialogRequest, string?> showDialog)", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("IDisposable RegisterResult(Func<PortableFileDialogRequest, PortableFileDialogResult?> showDialog)", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public ReadOnlySpan<string> SelectedPaths", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetFileDialogService(", portableWpfServiceRegistry, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<object, string> showDialog)", service, StringComparison.Ordinal);
        Assert.Contains("CreateInteropRequest(", service, StringComparison.Ordinal);
        Assert.Contains("Func<ProGPU.Wpf.Interop.PortableFileDialogRequest, string>", service, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryShowDialog(CommonItemDialog dialog, out string[] selectedPaths)", service, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableFileDialogRequest", service, StringComparison.Ordinal);
        Assert.Contains("public string Kind { get; }", service, StringComparison.Ordinal);
        Assert.Contains("public string SuggestedItemName { get; }", service, StringComparison.Ordinal);
        Assert.Contains("public bool AllowMultipleSelection { get; }", service, StringComparison.Ordinal);

        Assert.Contains("itemDialog.TryRunPortableDialog(out bool? portableResult)", commonDialog, StringComparison.Ordinal);
        Assert.Contains("private bool? ShowWin32Dialog()", commonDialog, StringComparison.Ordinal);
        Assert.Contains("private bool? ShowWin32Dialog(Window owner)", commonDialog, StringComparison.Ordinal);
        Assert.True(
            commonDialog.IndexOf("itemDialog.TryRunPortableDialog(out bool? portableResult)", StringComparison.Ordinal)
                < commonDialog.IndexOf("UnsafeNativeMethods.GetActiveWindow()", StringComparison.Ordinal),
            "CommonDialog.ShowDialog must try the portable service before reading Win32 active-window state.");
        Assert.True(
            commonDialog.IndexOf("itemDialog.TryRunPortableDialog(out bool? portableResult)", StringComparison.Ordinal)
                < commonDialog.IndexOf("new WindowInteropHelper(owner)", StringComparison.Ordinal),
            "CommonDialog.ShowDialog(owner) must try the portable service before requiring an HWND owner.");

        Assert.Contains("internal bool TryRunPortableDialog(out bool? result)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("private protected virtual bool TryHandlePortableItemOk(out object revertState)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("private bool HandlePortableItemOk(string[] selectedPaths)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("OnItemOk(cancelArgs)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("private protected override bool TryHandlePortableItemOk(out object restoreState)", fileDialog, StringComparison.Ordinal);
        Assert.Contains("return ProcessFileNames();", fileDialog, StringComparison.Ordinal);

        Assert.Contains("TryRegisterPresentationFrameworkFileDialogService()", activation, StringComparison.Ordinal);
        Assert.Contains("PortableWpfServiceRegistry.TryGetFileDialogService(", activation, StringComparison.Ordinal);
        Assert.DoesNotContain("PortableFileDialogServiceTypeName", activation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRegisterPresentationFrameworkFileDialogServiceByReflection", activation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.FileDialogs", activation, StringComparison.Ordinal);
        Assert.Contains("PortableFileDialogResult? ShowPortableFileDialog(PortableFileDialogRequest request)", activation, StringComparison.Ordinal);
        Assert.Contains("fileDialogs.OpenFilesAsync(options)", activation, StringComparison.Ordinal);
        Assert.Contains("fileDialogs.PickFoldersAsync(options)", activation, StringComparison.Ordinal);
        Assert.Contains("ReadFileDialogPatterns(request.Filter)", activation, StringComparison.Ordinal);
        Assert.Contains("internal static IReadOnlyList<string> ReadFileDialogPatterns(string filter)", activation, StringComparison.Ordinal);
        Assert.Contains("AddFileDialogPatterns(filter.AsSpan(segmentStart, i - segmentStart), ref patterns)", activation, StringComparison.Ordinal);
        Assert.DoesNotContain("filter.Split('|')", activation, StringComparison.Ordinal);
        Assert.DoesNotContain("tokens[i].Split(';')", activation, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<string> NormalizeFileTypePatterns", processFileDialogService, StringComparison.Ordinal);
        Assert.Contains("var normalized = new List<string>(patterns.Count);", processFileDialogService, StringComparison.Ordinal);
        Assert.Contains("FosAllowMultiSelect", processFileDialogService, StringComparison.Ordinal);
        Assert.Contains("with multiple selections allowed", processFileDialogService, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Linq;", processFileDialogService, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeFileTypePatterns(patterns).ToArray()", processFileDialogService, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeFileTypePatterns(options.FileTypePatterns).ToArray()", processFileDialogService, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable SaveFileDialog FileName", runtimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("portable OpenFolderDialog FolderName", applicationRunHarness, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(_presentationFramework, typedActivation.Window)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ownerPrefix = owner is null ? \"no-owner\" : \"owner\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable SDK {ownerPrefix} SaveFileDialog FileName", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable SDK {ownerPrefix} OpenFolderDialog FolderName", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableFileDialogServiceTypeName)", sdkRuntimeHarness, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedSubsystemBringupReusesRealWpfXamlFrameworkAndThemeProjects()
    {
        var systemXamlProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "System.Xaml",
            "System.Xaml.csproj");
        var presentationBuildTasksProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationBuildTasks",
            "PresentationBuildTasks.csproj");
        var presentationFrameworkProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var fluentThemeProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Themes",
            "PresentationFramework.Fluent",
            "PresentationFramework.Fluent.csproj");
        var fluentThemeRefProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Themes",
            "PresentationFramework.Fluent",
            "ref",
            "PresentationFramework.Fluent-ref.csproj");
        var ribbonProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "System.Windows.Controls.Ribbon",
            "System.Windows.Controls.Ribbon.csproj");
        var ribbonRefProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "System.Windows.Controls.Ribbon",
            "ref",
            "System.Windows.Controls.Ribbon-ref.csproj");
        var repoRoot = Path.GetDirectoryName(FindRepoPath("README.md"))!;
        var rootDirectoryBuildPropsPath = Path.Combine(repoRoot, "Directory.Build.props");
        var rootDirectoryBuildTargetsPath = Path.Combine(repoRoot, "Directory.Build.targets");
        var realPresentationCoreHarnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealPresentationCoreHarness",
            "ProGPU.Wpf.RealPresentationCoreHarness.csproj");

        var rootDirectoryBuildProps = XDocument.Load(rootDirectoryBuildPropsPath);
        var rootDirectoryBuildTargets = XDocument.Load(rootDirectoryBuildTargetsPath);
        var systemXamlProject = XDocument.Load(systemXamlProjectPath);
        var presentationBuildTasksProject = XDocument.Load(presentationBuildTasksProjectPath);
        var presentationFrameworkProject = XDocument.Load(presentationFrameworkProjectPath);
        var fluentThemeProject = XDocument.Load(fluentThemeProjectPath);
        var fluentThemeRefProject = XDocument.Load(fluentThemeRefProjectPath);
        var ribbonProject = XDocument.Load(ribbonProjectPath);
        var ribbonRefProject = XDocument.Load(ribbonRefProjectPath);
        var realPresentationCoreHarnessProject = XDocument.Load(realPresentationCoreHarnessProjectPath);

        Assert.Equal("System.Xaml", Assert.Single(systemXamlProject.Descendants("AssemblyName")).Value);
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Xaml", "XamlReader.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Xaml", "InfosetObjects", "XamlXmlReader.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Xaml", "InfosetObjects", "XamlObjectWriter.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Windows", "Markup", "MarkupExtension.cs");
        AssertProjectReference(systemXamlProject, @"System.Xaml\ref\System.Xaml-ref.csproj");

        var targetFrameworks = Assert.Single(presentationBuildTasksProject.Descendants("TargetFrameworks")).Value;
        Assert.Contains("$(BundledNETCoreAppTargetFramework)", targetFrameworks, StringComparison.Ordinal);
        Assert.Contains("$(NetFrameworkToolCurrent)", targetFrameworks, StringComparison.Ordinal);
        Assert.Contains(
            rootDirectoryBuildProps.Descendants("PbtTfm"),
            property => string.Equals(property.Value, "$(BundledNETCoreAppTargetFramework)", StringComparison.Ordinal)
                && string.Equals(property.Attribute("Condition")?.Value, "'$(MSBuildRuntimeType)' == 'Core'", StringComparison.Ordinal));
        Assert.Contains(
            rootDirectoryBuildProps.Descendants("ProGpuWpfPortableNetCoreAppRefVersion"),
            property => string.Equals(property.Value, "10.0.5", StringComparison.Ordinal)
                && string.Equals(property.Attribute("Condition")?.Value, "'$(ProGpuWpfPortableNetCoreAppRefVersion)' == '' And '$(ProGpuWpfTargetFramework)' == 'net10.0'", StringComparison.Ordinal));
        Assert.Contains(
            rootDirectoryBuildTargets.Descendants("MicrosoftNETCoreAppRefVersion"),
            property => string.Equals(property.Value, "$(ProGpuWpfPortableNetCoreAppRefVersion)", StringComparison.Ordinal)
                && string.Equals(property.Parent?.Attribute("Condition")?.Value, "'$(ProGpuWpfPortableNetCoreAppRefVersion)' != ''", StringComparison.Ordinal));
        AssertCompileInclude(presentationBuildTasksProject, @"MS\Internal\MarkupCompiler\MarkupCompiler.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"MS\Internal\MarkupCompiler\ParserExtension.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"Microsoft\Build\Tasks\Windows\MarkupCompilePass1.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"Microsoft\Build\Tasks\Windows\MarkupCompilePass2.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"PresentationFramework\System\Windows\Markup\BamlBinaryWriter.cs", link: true);
        AssertCompileInclude(presentationBuildTasksProject, @"PresentationFramework\System\Windows\Markup\BamlRecords.cs", link: true);

        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Application.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Window.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\ResourceDictionary.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Style.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\ControlTemplate.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Data\ObjectDataProvider.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "WindowsBase", "System", "Windows", "Data", "DataSourceProvider.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Controls\RichTextBox.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Documents\FlowDocument.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Markup\Baml2006\Baml2006Reader.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Markup\XamlReader.cs");
        AssertProjectReference(presentationFrameworkProject, @"System.Xaml\System.Xaml.csproj");
        AssertProjectReference(presentationFrameworkProject, @"PresentationCore\PresentationCore.csproj");
        AssertProjectReference(presentationFrameworkProject, @"WindowsBase\WindowsBase.csproj");
        AssertProjectReference(presentationFrameworkProject, @"ReachFramework\ReachFramework.csproj");

        Assert.Equal("true", Assert.Single(fluentThemeProject.Descendants("InternalMarkupCompilation")).Value);
        var pageItem = Assert.Single(
            fluentThemeProject.Descendants("Page"),
            item => string.Equals(item.Attribute("Include")?.Value, @"**\*.xaml", StringComparison.Ordinal));
        Assert.Equal("MSBuild:Compile", pageItem.Element("Generator")?.Value);
        AssertProjectReference(fluentThemeProject, @"System.Xaml\System.Xaml.csproj");
        AssertProjectReference(fluentThemeProject, @"PresentationCore\PresentationCore.csproj");
        AssertProjectReference(fluentThemeProject, @"PresentationFramework\PresentationFramework.csproj");
        AssertProjectReference(fluentThemeProject, @"Themes\PresentationFramework.Fluent\ref\PresentationFramework.Fluent-ref.csproj");
        Assert.Equal("PresentationFramework.Fluent", Assert.Single(fluentThemeRefProject.Descendants("AssemblyName")).Value);
        Assert.Equal("PresentationFramework.Fluent-ref", Assert.Single(fluentThemeRefProject.Descendants("PackageId")).Value);
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Themes", "Fluent.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Themes", "Fluent.Light.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Styles", "Button.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Styles", "RichTextBox.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Styles", "Window.xaml");

        Assert.Equal("true", Assert.Single(ribbonProject.Descendants("InternalMarkupCompilation")).Value);
        Assert.Contains(
            ribbonProject.Descendants("Page"),
            item => string.Equals(item.Attribute("Include")?.Value, @"Themes\Generic.xaml", StringComparison.Ordinal));
        Assert.Contains(
            ribbonProject.Descendants("Page"),
            item => string.Equals(item.Attribute("Include")?.Value, @"Themes\Aero2.NormalColor.xaml", StringComparison.Ordinal));
        AssertProjectReference(ribbonProject, @"System.Xaml\System.Xaml.csproj");
        AssertProjectReference(ribbonProject, @"PresentationCore\PresentationCore.csproj");
        AssertProjectReference(ribbonProject, @"PresentationFramework\PresentationFramework.csproj");
        AssertProjectReference(ribbonProject, @"Themes\PresentationFramework.Classic\PresentationFramework.Classic.csproj");
        AssertProjectReference(ribbonProject, @"System.Windows.Controls.Ribbon\ref\System.Windows.Controls.Ribbon-ref.csproj");
        Assert.Equal("System.Windows.Controls.Ribbon", Assert.Single(ribbonRefProject.Descendants("AssemblyName")).Value);
        Assert.Equal("System.Windows.Controls.Ribbon-ref", Assert.Single(ribbonRefProject.Descendants("PackageId")).Value);
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Windows.Controls.Ribbon", "Microsoft", "Windows", "Controls", "Ribbon", "Ribbon.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Windows.Controls.Ribbon", "Microsoft", "Windows", "Controls", "Ribbon", "RibbonButton.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Windows.Controls.Ribbon", "Themes", "Generic.xaml");

        AssertProjectReference(realPresentationCoreHarnessProject, @"ProGPU.Wpf\ProGPU.Wpf.csproj");
        var realPresentationCoreReference = AssertProjectReference(
            realPresentationCoreHarnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(realPresentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(realPresentationCoreReference, "PrivateAssets"));
    }

    [Fact]
    public void RealPresentationFrameworkHarnessExercisesManagedFrameworkAndProGpuBridge()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealPresentationFrameworkHarness",
            "ProGPU.Wpf.RealPresentationFrameworkHarness.csproj");
        var harnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealPresentationFrameworkHarness",
            "Program.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var harnessProgram = File.ReadAllText(harnessProgramPath);

        AssertPackageReference(harnessProject, "System.Configuration.ConfigurationManager");
        AssertPackageReference(harnessProject, "System.Formats.Nrbf");
        AssertPackageReference(harnessProject, "$(SystemIOPackagingPackage)");
        AssertPackageReference(harnessProject, "System.Windows.Extensions");

        AssertProjectReference(harnessProject, @"ProGPU.Wpf\ProGPU.Wpf.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Backend\ProGPU.Backend.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Scene\ProGPU.Scene.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Vector\ProGPU.Vector.csproj");

        var presentationCoreReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(presentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationCoreReference, "PrivateAssets"));

        var presentationFrameworkReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");
        Assert.Equal("false", GetItemMetadata(presentationFrameworkReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationFrameworkReference, "PrivateAssets"));

        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableRenderDataDrawingContextSinkProvider", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RegisterRealPortableObjectSinkProvider(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PushObjectSinkFactory", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuWpfCompositionTarget.CreateHeadless()", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.TextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.RichTextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.FlowDocument", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ControlTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.DrawingBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.DrawingVisual", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.GeometryDrawing", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.LinearGradientBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.RadialGradientBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.FormattedText", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.GlyphRun", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.ImageBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.Imaging.BitmapSource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfBitmapSourceImageAdapter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("VerifyPortableSpellerFallback(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(\"NullSpellerInterop\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RecordPortableSpellerSegment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable speller segment count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawLine\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawRoundedRectangle\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawEllipse\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawGeometry\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawDrawing\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawGlyphRun\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawText\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawImage\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushOpacity\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushClip\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushTransform\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushOpacityMask\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushGuidelineSet\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("VerifyRetainedDrawingVisualBranch(target)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawLine", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawRoundedRect", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawEllipse", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawGlyphRun", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawTexture", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushOpacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushGeometryClip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushOpacityMask", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained opacity mask", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained rounded rectangle radius X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained guideline snapped rect X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained drawing resource path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained glyph run", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained formatted text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained bitmap image", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained image brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained drawing brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained linear gradient brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained radial gradient brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained linear gradient start X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained radial gradient origin X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual transformed line X offset", harnessProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void RealXamlCompilerHarnessUsesWpfApplicationDefinitionAndPagePipeline()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "ProGPU.Wpf.RealXamlCompilerHarness.csproj");
        var appXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "App.xaml");
        var smokeResourcesXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeResources.xaml");
        var mainWindowXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "MainWindow.xaml");
        var smokeUserControlXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeUserControl.xaml");
        var smokePageXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokePage.xaml");
        var smokeSecondPageXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeSecondPage.xaml");
        var appCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "App.xaml.cs");
        var mainWindowCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "MainWindow.xaml.cs");
        var smokeUserControlCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeUserControl.xaml.cs");
        var smokePageCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokePage.xaml.cs");
        var smokeSecondPageCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeSecondPage.xaml.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var appXaml = File.ReadAllText(appXamlPath);
        var smokeResourcesXaml = File.ReadAllText(smokeResourcesXamlPath);
        var mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
        var smokeUserControlXaml = File.ReadAllText(smokeUserControlXamlPath);
        var smokePageXaml = File.ReadAllText(smokePageXamlPath);
        var smokeSecondPageXaml = File.ReadAllText(smokeSecondPageXamlPath);
        var appCodeBehind = File.ReadAllText(appCodeBehindPath);
        var mainWindowCodeBehind = File.ReadAllText(mainWindowCodeBehindPath);
        var smokeUserControlCodeBehind = File.ReadAllText(smokeUserControlCodeBehindPath);
        var smokePageCodeBehind = File.ReadAllText(smokePageCodeBehindPath);
        var smokeSecondPageCodeBehind = File.ReadAllText(smokeSecondPageCodeBehindPath);

        Assert.Equal("true", Assert.Single(harnessProject.Descendants("InternalMarkupCompilation")).Value);

        var applicationDefinition = Assert.Single(harnessProject.Descendants("ApplicationDefinition"));
        Assert.Equal("App.xaml", applicationDefinition.Attribute("Include")?.Value);
        Assert.Equal("MSBuild:Compile", applicationDefinition.Element("Generator")?.Value);

        var smokeResourcesPage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokeResources.xaml");
        Assert.Equal("MSBuild:Compile", smokeResourcesPage.Element("Generator")?.Value);

        var smokeUserControlPage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokeUserControl.xaml");
        Assert.Equal("MSBuild:Compile", smokeUserControlPage.Element("Generator")?.Value);

        var smokePage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokePage.xaml");
        Assert.Equal("MSBuild:Compile", smokePage.Element("Generator")?.Value);

        var smokeSecondPage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokeSecondPage.xaml");
        Assert.Equal("MSBuild:Compile", smokeSecondPage.Element("Generator")?.Value);

        var page = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "MainWindow.xaml");
        Assert.Equal("MainWindow.xaml", page.Attribute("Include")?.Value);
        Assert.Equal("MSBuild:Compile", page.Element("Generator")?.Value);

        AssertCompileInclude(harnessProject, "App.xaml.cs");
        AssertCompileInclude(harnessProject, "MainWindow.xaml.cs");
        AssertCompileInclude(harnessProject, "SmokePage.xaml.cs");
        AssertCompileInclude(harnessProject, "SmokeSecondPage.xaml.cs");
        AssertCompileInclude(harnessProject, "SmokeUserControl.xaml.cs");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\System.Xaml\System.Xaml.csproj");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\WindowsBase\WindowsBase.csproj");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");

        Assert.DoesNotContain(
            harnessProject.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", @"ProGPU.Wpf\ProGPU.Wpf.csproj"));
        Assert.DoesNotContain(
            harnessProject.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", @"external\ProGPU\src\ProGPU.Scene\ProGPU.Scene.csproj"));

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.App\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("StartupUri=\"MainWindow.xaml\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceDictionary.MergedDictionaries", appXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceDictionary Source=\"SmokeResources.xaml\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"AccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=SmokeComponentAccentBrush", appXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#2F6B54\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"ReplacementAccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"UnsharedAccentBrush\" x:Shared=\"False\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"FreezableAccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#B15E3B\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("LinearGradientBrush", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FreezableGradientBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SpreadMethod=\"Reflect\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#FF2F6B54\" Offset=\"0\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#FFB15E3B\" Offset=\"0.5\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate x:Key=\"SmokeButtonTemplate\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate.Resources", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"TemplateBorderBrush\" Color=\"#6B4E9B\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource AccentBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{StaticResource TemplateBorderBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"2\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding Content}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.VisualStateGroups", appXaml, StringComparison.Ordinal);
        Assert.Contains("VisualStateGroup x:Name=\"CommonStates\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("VisualState x:Name=\"Pressed\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.73\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate.Triggers", appXaml, StringComparison.Ordinal);
        Assert.Contains("Trigger Property=\"IsEnabled\" Value=\"False\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter TargetName=\"TemplateBorder\" Property=\"Opacity\" Value=\"0.42\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"SmokeTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"BasedOnTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource SmokeTextBoxStyle}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"based on text box style\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"TriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style.Triggers", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsWarning}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"PropertyTriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"property trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiPropertyTriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiTrigger>", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Property=\"IsDefault\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"multi property trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"TriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Trigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("Trigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.41\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"DataTriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsTriggerActionActive}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.52\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiDataTriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsMultiTriggerActionReady}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsMultiTriggerActionArmed}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.63\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiTriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiTrigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiTrigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.74\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiTriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsWarning}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsCritical}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"multi trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class App : Application", appCodeBehind, StringComparison.Ordinal);

        Assert.Contains("ResourceDictionary", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"MergedAccentBrush\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Thickness x:Key=\"MergedBlockMargin\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Style TargetType=\"{x:Type CheckBox}\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"implicit merged style\"", smokeResourcesXaml, StringComparison.Ordinal);

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.MainWindow\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeMenu\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileMenuItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_File\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuCommandItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"menu command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=SmokeMenu}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Run _Command\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Separator />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuClickItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnMenuClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Click\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuCheckableItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsCheckable=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnMenuCheckableChecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnMenuCheckableUnchecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Checkable\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeToolBarTray\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ToolBar x:Name=\"SmokeToolBar\" Header=\"Smoke tools\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolBarCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"toolbar command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Run toolbar\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Separator x:Name=\"ToolBarSeparator\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolBarToggle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Toggle toolbar\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeStatusBar\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusReadyItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Ready\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"status text\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RangeValueSlider\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TickFrequency=\"25\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding RangeValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RangeValueProgress\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"12\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding RangeValue}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextMenuButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Button.ContextMenu", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ContextMenu x:Name=\"ContextButtonMenu\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextCommandItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"context menu command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Run Context _Command\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextClickItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnContextMenuClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Context _Click\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Button.ToolTip", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextButtonToolTip\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Placement=\"Right\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextButtonToolTipText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled ToolTip content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TextBox", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CredentialBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"12\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged=\"OnPasswordChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChar=\"#\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RichTextBox", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BasedOnTextBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource BasedOnTextBoxStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled BasedOn TextBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FlowDocument", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompiledDocument\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IntroParagraph\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Bold>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Italic>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Underline>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Span x:Name=\"DocumentSpan\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<LineBreak />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("after line break", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentLink\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigateUri=\"https://example.test/progpu-wpf\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"OnDocumentLinkRequestNavigate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LoadCompleted=\"OnSourceNavigationFrameLoadCompleted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Navigated=\"OnSourceNavigationFrameNavigated\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Navigating=\"OnSourceNavigationFrameNavigating\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Figure x:Name=\"DocumentFigure\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("figure anchored text", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Floater x:Name=\"DocumentFloater\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("floater anchored text", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("InlineUIContainer x:Name=\"InlineActionContainer\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentInlineButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"inline document button\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Section x:Name=\"DocumentSection\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("section block text", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BlockUIContainer x:Name=\"DocumentBlockContainer\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentBlockButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"block document button\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Table", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentTable\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Table.Columns", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("table alpha", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("table beta", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<List MarkerStyle=\"Decimal\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("first document item", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("second document item", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Greeting}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PriorityBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<PriorityBinding>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"MissingPriorityText\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiBinding StringFormat=\"{}{0} / {1}\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"Greeting\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"ButtonText\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeUpperConverter", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeUpperConverter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeJoinConverter", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeJoinConverter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConvertedBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource SmokeUpperConverter}", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=converted", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConvertedMultiBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Converter=\"{StaticResource SmokeJoinConverter}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=\"converted-multi\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AncestorBindingBorder\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"ancestor binding source\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RelativeSourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RelativeSource={RelativeSource AncestorType={x:Type Border}}, Path=Tag", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingTransferBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SourceUpdated=\"OnBindingTransferSourceUpdated\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TargetUpdated=\"OnBindingTransferTargetUpdated\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BindingTransferText, Mode=TwoWay, UpdateSourceTrigger=Explicit, NotifyOnSourceUpdated=True, NotifyOnTargetUpdated=True", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidatedBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Validation.Error=\"OnValidatedBoxValidationError\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ValidatedText, UpdateSourceTrigger=Explicit, ValidatesOnDataErrors=True, NotifyOnValidationError=True", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RuleValidatedBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Validation.Error=\"OnRuleValidatedBoxValidationError\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"RuleValidatedText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding.ValidationRules", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokePrefixValidationRule RequiredPrefix=\"rule:\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingGroupPanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StackPanel.BindingGroup", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BindingGroup Name=\"SmokeBindingGroup\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BindingGroup.ValidationRules", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeBindingGroupValidationRule", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FirstProperty=\"BindingGroupFirstName\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RequiredPrefix=\"group:\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SecondProperty=\"BindingGroupLastName\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingGroupFirstBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BindingGroupFirstName, UpdateSourceTrigger=Explicit}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingGroupLastBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BindingGroupLastName, UpdateSourceTrigger=Explicit}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProviderGreetingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Source={StaticResource ProviderGreeting}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"XmlProviderBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Source={StaticResource ProviderXml}, XPath=@Text}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StoryboardTargetBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnStoryboardTargetLoaded\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TextBlock.Triggers", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("EventTrigger RoutedEvent=\"FrameworkElement.Loaded\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BeginStoryboard", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetName=\"StoryboardTargetBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.37\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Duration=\"0:0:0\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FillBehavior=\"HoldEnd\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StoryboardTriggerButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Button.Triggers", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RoutedEvent=\"Button.Click\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.64\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MarkupExtensionBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{local:SmokeText Prefix=compiled, Value=markup}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StackPanel.Resources", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"ScopedAccentBrush\" Color=\"#6B4E9B\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Thickness x:Key=\"ScopedBlockMargin\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MergedResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource MergedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource MergedBlockMargin}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScopedResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource ScopedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource ScopedBlockMargin}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled scoped resource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComponentResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource {ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=SmokeComponentAccentBrush}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled component resource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnsharedResourceBorderA\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnsharedResourceBorderB\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource UnsharedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeUserControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NestedControl\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridShorthandDescription\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("commaâ€‘separated", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AttachedLayoutGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,Auto\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"Auto 80 *\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridFirstCell\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LayoutPanelSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DockPanelSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LastChildFill=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Left\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Right\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CanvasSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Left=\"12\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Top=\"6\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WrapPanelSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemWidth=\"64\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridSplitterGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridSplitterSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ResizeBehavior=\"PreviousAndNext\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ResizeDirection=\"Columns\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ShowsPreview=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollingSmokePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollViewerSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Visible\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanContentScroll=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollViewerSixthItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VerticalScrollBarSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ViewportSize=\"2\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DateSelectionSmokePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CalendarSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDate=\"2026-06-17\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"SingleDate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DatePickerSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"160\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDate=\"2026-06-18\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDateFormat=\"Short\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitStyleCheckBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToggleChoicePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToggleChoiceCheckBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnToggleChoiceChecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnToggleChoiceUnchecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"toggle choice\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RadioChoiceAlpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RadioChoiceBeta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GroupName=\"SmokeChoiceGroup\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnChoiceRadioChecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnChoiceRadioUnchecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"choice alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"choice beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EventButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnXamlClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GotMouseCapture=\"OnXamlGotMouseCapture\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LostMouseCapture=\"OnXamlLostMouseCapture\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MouseWheel=\"OnXamlMouseWheel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeRootPanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("primitives:Thumb.DragDelta=\"OnBubbledThumbDragDelta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RepeatActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRepeatButtonClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Delay=\"250\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Interval=\"75\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("primitives:Thumb", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DragManagerThumb\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DragStarted=\"OnDragManagerThumbDragStarted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DragDelta=\"OnDragManagerThumbDragDelta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DragCompleted=\"OnDragManagerThumbDragCompleted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"drag manager thumb\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StyledEventButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource EventSetterButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SmokeCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CanExecuteCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ToggleCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"can execute payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RequeryCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RequeryCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"requery payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:componentModel=\"clr-namespace:System.ComponentModel;assembly=WindowsBase\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:local=\"clr-namespace:ProGPU.Wpf.RealXamlCompilerHarness\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:primitives=\"clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SortedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.SortDescriptions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("componentModel:SortDescription", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Direction=\"Descending\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyName=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"LiveSortedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveSortingRequested=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.LiveSortingProperties", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>Name</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilteredItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Filter=\"OnFilteredItemsViewFilter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"LiveFilteredItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveFilteringRequested=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.LiveFilteringProperties", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GroupedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GroupDescriptions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription PropertyName=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"LiveGroupedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveGroupingRequested=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.LiveGroupingProperties", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>Category</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CurrencyItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate DataType=\"{x:Type local:SmokeDetail}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitDetailTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"implicit data template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Title}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SelectedDetailTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FallbackDetailTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedDetailTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"content template selector selected\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"content template selector fallback\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDetailTemplateSelector", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeDetailTemplateSelector\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTemplate=\"{StaticResource SelectedDetailTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FallbackTemplate=\"{StaticResource FallbackDetailTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderHeaderTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpanderHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"expander header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GroupBoxHeaderTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupBoxHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group box header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HierarchicalDataTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeNodeTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type local:SmokeNode}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Children}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NodeTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"hierarchical template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AlphaItemTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DefaultItemTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorTemplateTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"selector alpha template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"selector default template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemTemplateSelector", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeItemTemplateSelector\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlphaTemplate=\"{StaticResource AlphaItemTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DefaultTemplate=\"{StaticResource DefaultItemTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AlphaItemContainerSelectorStyle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DefaultItemContainerSelectorStyle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemContainerStyleSelector", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeItemContainerStyleSelector\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlphaStyle=\"{StaticResource AlphaItemContainerSelectorStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DefaultStyle=\"{StaticResource DefaultItemContainerSelectorStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ProviderGreeting\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsAsynchronous=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MethodName=\"CreateProviderGreeting\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectType=\"{x:Type local:ProviderDataFactory}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider.MethodParameters", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>provider</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>7</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("XmlDataProvider", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ProviderXml\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsAsynchronous=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("XPath=\"/Smoke/Message\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:XData", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Message Text=\"xml provider text\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"EventSetterButtonStyle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"event setter style\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("EventSetter Event=\"Click\" Handler=\"OnStyledButtonClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Window.CommandBindings", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.SmokeRoutedCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanExecute=\"OnSmokeCommandCanExecute\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Executed=\"OnSmokeCommandExecuted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Window.InputBindings", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<KeyBinding", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"input binding payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F6\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RoutedCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=InputBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassCommandTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.SmokeClassRoutedCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"class command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=ClassCommandTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TemplatedButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Template=\"{StaticResource SmokeButtonTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding TriggerButtonText}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PropertyTriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource PropertyTriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiPropertyTriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiPropertyTriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DataTriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource DataTriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiDataTriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiDataTriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiTriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiTriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiTriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiTriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedItem, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SortedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource SortedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveSortedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource LiveSortedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FilteredItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource FilteredItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveFilteredItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource LiveFilteredItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource GroupedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.GroupStyle", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GroupStyle.HeaderTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveGroupedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource LiveGroupedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveGroupHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"live group header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrencyItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsSynchronizedWithCurrentItem=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource CurrencyItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeCompositeItemsProvider.Items", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompositeItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<CompositeCollection>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionContainer Collection=\"{x:Static local:SmokeCompositeItemsProvider.Items}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("composite header", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("composite footer", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlternationItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlternationCount=\"2\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StringFormatItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemStringFormat=\"formatted {0}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Labels}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DisplayMemberItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding SelectedCategory, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsComboBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding ComboSelectedCategory, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorEventPanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionEventListBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnSelectionEventListBoxChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"selection alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"selection beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionEventComboBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnSelectionEventComboBoxChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"combo alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"combo beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiSelectionEventListBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Multiple\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnMultiSelectionEventListBoxChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"multi alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"multi beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"multi gamma\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridItemsListView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ListView.View>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GridView AllowsColumnReorder=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Category}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsDataGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AutoGenerateColumns=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanUserAddRows=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardCopyMode=\"IncludeHeader\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CopyingRowClipboardContent=\"OnItemsDataGridCopyingRowClipboardContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GridLinesVisibility=\"Horizontal\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeadersVisibility=\"Column\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedItem, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGrid.Columns", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridTextColumn", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridCheckBoxColumn", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardContentBinding=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardContentBinding=\"{Binding Category}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardContentBinding=\"{Binding IsActive}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsActive}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Active\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NodeTree\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource SmokeNodeTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Nodes}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTree\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTreeAlpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"explicit alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Expanded=\"OnExplicitTreeExpanded\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Collapsed=\"OnExplicitTreeCollapsed\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Selected=\"OnExplicitTreeSelected\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unselected=\"OnExplicitTreeUnselected\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTreeAlphaChild\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"explicit alpha child\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTreeBeta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"explicit beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource SmokeItemTemplateSelector}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StyleSelectorItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyleSelector=\"{StaticResource SmokeItemContainerStyleSelector}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StyleSelectorItemTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"style selector item template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitTemplateHost\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Detail}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorTemplateHost\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ContentTemplateSelector=\"{StaticResource SmokeDetailTemplateSelector}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeTabControl\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"alpha tab\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlphaTabContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"tab alpha content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"alpha tab content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"beta tab\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BetaTabContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"tab beta content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"beta tab content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeExpander\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Detail}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderTemplate=\"{StaticResource ExpanderHeaderTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpanderContentText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"expander content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Greeting}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeGroupBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderTemplate=\"{StaticResource GroupBoxHeaderTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupBoxContentText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group box content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ButtonText}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeAdornerDecorator\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdornedButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"adorned button\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyScopePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDependencyProperties.InheritedLabel=\"inherited smoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDependencyPropertyControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyTarget\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CoercedLevel=\"15\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDependencyPropertyOwnerControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyOwnerTarget\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("OwnerLevel=\"35\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeOverrideMetadataControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyMetadataTarget\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CustomRoutedEventScopePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeRoutedEventSource.SmokeBubbled=\"OnCustomRoutedEventScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeRoutedEventSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CustomRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeBubbled=\"OnCustomRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassRoutedEventScopePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeClassRoutedEventSource.SmokeClassBubbled=\"OnClassRoutedEventScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeClassRoutedEventSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeClassBubbled=\"OnClassRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessKeyFocusScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.IsFocusScope=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=AccessTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlternateAccessTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessTargetLabel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"_Access target\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"{Binding ElementName=AccessTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StandaloneAccessText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"_Standalone access text\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MouseBindingSurface\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TextBlock.InputBindings>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MouseBinding", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"mouse binding payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MouseAction=\"RightClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationFrame\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigationUIVisibility=\"Hidden\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"SmokePage.xaml\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.SmokePage\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"compiled source page\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationPagePanel\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationPageText\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled source page content\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationPageButton\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPageButtonClick\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"compiled page button\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"compiled second page\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationSecondPagePanel\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationSecondPageText\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled second page content\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.ItemContainerStyle", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style TargetType=\"{x:Type ListBoxItem}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"container trigger inactive\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"container trigger active\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.ItemsPanel", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsPanelTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate DataType=\"{x:Type local:SmokeItem}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate.Triggers", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding Name}\" Value=\"item beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter TargetName=\"ItemTextBlock\" Property=\"Tag\" Value=\"template trigger active\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StaticResource AccentBrush", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DataContext = new SmokeViewModel();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static RoutedUICommand SmokeRoutedCommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static RoutedUICommand SmokeClassRoutedCommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeClassCommandTextBox : TextBox", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RegisterClassCommandBinding", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RegisterClassInputBinding", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Key.F7, ModifierKeys.Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandParameter = \"class input payload\"", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new CommandBinding(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnSmokeClassCommandCanExecute", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnSmokeClassCommandExecuted", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class ProviderDataFactory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string CreateProviderGreeting", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix} data {value}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeTextExtension : MarkupExtension", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override object ProvideValue(IServiceProvider serviceProvider)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{Prefix} {Value} extension\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeAdorner : Adorner", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("protected override void OnRender(DrawingContext drawingContext)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("drawingContext.DrawRectangle(null, new Pen(Brushes.LimeGreen, 1.0), adornedBounds)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static class SmokeDependencyProperties", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty.RegisterAttached(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("FrameworkPropertyMetadataOptions.Inherits", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDependencyPropertyControl : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty.Register(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static object CoerceLevel(DependencyObject dependencyObject, object baseValue)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("control.CoercedLevelChangedCount++", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDependencyPropertyOwnerControl : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeDependencyPropertyControl.CoercedLevelProperty.AddOwner(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int OwnerLevel", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).BaseValueSource.ToString()", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).IsCoerced", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).IsCurrent", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClearValue(OwnerLevelProperty)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(OwnerLevelProperty, value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public class SmokeMetadataBaseControl : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ModeLabelProperty.OverrideMetadata(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeOverrideMetadataControl : SmokeMetadataBaseControl", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public delegate void SmokeRoutedEventHandler(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRoutedEventArgs : RoutedEventArgs", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string Payload { get; }", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRoutedEventSource : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EventManager.RegisterRoutedEvent(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategy.Bubble", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public event SmokeRoutedEventHandler SmokeBubbled", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AddHandler(SmokeBubbledEvent, value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RemoveHandler(SmokeBubbledEvent, value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeRoutedEventArgs RaiseSmokeBubbled(string payload)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RaiseEvent(args)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int CustomRoutedEventSourceCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int CustomRoutedEventScopeCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnCustomRoutedEventSource(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnCustomRoutedEventScope(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastCustomRoutedEventScopeOriginalSourceName = DescribeElementName(e.OriginalSource)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastCustomRoutedEventScopePayload = e.Payload", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClassRoutedEventScopePanel.AddHandler(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnClassRoutedEventSource(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnClassRoutedEventScope(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnClassRoutedEventScopeHandledToo(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeClassRoutedEventSource : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static readonly RoutedEvent SmokeClassBubbledEvent = EventManager.RegisterRoutedEvent(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EventManager.RegisterClassHandler(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new SmokeRoutedEventHandler(OnSmokeClassBubbledClassHandler)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public event SmokeRoutedEventHandler SmokeClassBubbled", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ClassHandlerCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeRoutedEventArgs RaiseSmokeClassBubbled(string payload)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static void OnSmokeClassBubbledClassHandler(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandCanExecute", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandExecuted", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RoutedCommandExecutionCount++", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlGotMouseCaptureCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlGotMouseCapture", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlGotMouseCaptureRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlLostMouseCaptureCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlLostMouseCapture", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlLostMouseCaptureRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlMouseWheelCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlMouseWheel", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlMouseWheelDelta = e.Delta", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlMouseWheelRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int RepeatButtonClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnRepeatButtonClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastRepeatButtonClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Controls.Primitives;", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Navigation;", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragStartedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragDeltaCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragCompletedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BubbledThumbDragDeltaCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragStarted(object sender, DragStartedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragDelta(object sender, DragDeltaEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragCompleted(object sender, DragCompletedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBubbledThumbDragDelta(object sender, DragDeltaEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastBubbledThumbDragDeltaOriginalSourceName = e.OriginalSource is FrameworkElement source ? source.Name : null", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int DocumentLinkRequestNavigateCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateSenderName = sender switch", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateUri = e.Uri?.ToString()", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FrameNavigatingCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FrameNavigatedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FrameLoadCompletedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSourceNavigationFrameNavigating(object sender, NavigatingCancelEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSourceNavigationFrameNavigated(object sender, NavigationEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSourceNavigationFrameLoadCompleted(object sender, NavigationEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastFrameNavigatingNavigationMode = e.NavigationMode.ToString()", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastFrameNavigatedContentType = e.Content?.GetType().FullName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastFrameLoadCompletedContentType = e.Content?.GetType().FullName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int StyledClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnStyledButtonClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastStyledClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastMenuClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuCheckableCheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuCheckableChecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuCheckableUncheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuCheckableUnchecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ContextMenuClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnContextMenuClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastContextMenuClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int PasswordChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnPasswordChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastPasswordChangedRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ToggleChoiceCheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnToggleChoiceChecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnToggleChoiceUnchecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ChoiceRadioCheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnChoiceRadioChecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnChoiceRadioUnchecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExplicitTreeExpandedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeExpanded", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeCollapsed", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExplicitTreeSelectedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeSelected", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeUnselected", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ListBoxSelectionChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSelectionEventListBoxChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MultiListBoxSelectionChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMultiSelectionEventListBoxChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ComboBoxSelectionChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSelectionEventComboBoxChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static string? DescribeSelectionItem(IList items)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BindingTransferTargetUpdatedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBindingTransferTargetUpdated", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BindingTransferSourceUpdatedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBindingTransferSourceUpdated", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static string? DescribeElementName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ValidatedBoxValidationErrorCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnValidatedBoxValidationError", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int RuleValidatedBoxValidationErrorCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnRuleValidatedBoxValidationError", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastRuleValidatedBoxValidationErrorRuleName = e.Error.RuleInError?.GetType().Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int StoryboardTargetLoadedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnStoryboardTargetLoaded", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastStoryboardTargetLoadedRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FilteredItemsFilterCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnFilteredItemsViewFilter", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Accepted = e.Item is SmokeItem smokeItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("string.Equals(smokeItem.Name, \"item beta\", StringComparison.Ordinal)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeViewModel : INotifyPropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("IDataErrorInfo", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SmokeItem> Items", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SmokeNode> Nodes", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeDetail Detail", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string TriggerButtonText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string ValidatedText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string RuleValidatedText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string BindingTransferText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string BindingGroupFirstName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string BindingGroupLastName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidatedText is required", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsWarning", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsCritical", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsTriggerActionActive", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsMultiTriggerActionReady", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsMultiTriggerActionArmed", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeItem? SelectedItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string SelectedCategory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private string _selectedCategory = \"secondary group\"", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string ComboSelectedCategory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private string _comboSelectedCategory = \"secondary group\"", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public double RangeValue", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private double _rangeValue = 42.0", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDetailTemplateSelector : DataTemplateSelector", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? SelectedTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override DataTemplate? SelectTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemContainerStyleSelector : StyleSelector", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public Style? AlphaStyle", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override Style? SelectStyle", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItem : INotifyPropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("PropertyChangedEventHandler? PropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string Category", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsActive", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDetail", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeNode", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public ObservableCollection<SmokeNode> Children", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemTemplateSelector : DataTemplateSelector", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? AlphaTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? DefaultTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override DataTemplate? SelectTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("string.Equals(smokeItem.Name, \"item alpha\", StringComparison.Ordinal)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeUpperConverter : IValueConverter", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeJoinConverter : IMultiValueConverter", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix}:{text.ToUpperInvariant()}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix}:{string.Join(\"|\", parts)}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokePrefixValidationRule : ValidationRule", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string RequiredPrefix", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidationResult.ValidResult", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeBindingGroupValidationRule : ValidationRule", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("value is not BindingGroup bindingGroup", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("bindingGroup.GetValue(item, propertyName)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeToggleCommand ToggleCommand { get; } = new();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeToggleCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool CanExecuteValue { get; private set; }", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public void SetCanExecute(bool value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeRequeryCommand RequeryCommand { get; } = new();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRequeryCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RequerySuggested += value", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RequerySuggested -= value", mainWindowCodeBehind, StringComparison.Ordinal);

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.SmokeUserControl\"", smokeUse×vÓŞ›Ê×¬¢h­µçHÜ”ÜX›PÛÛ[X[™Ú[šĞœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈ›ÛÛPÜ™X]SX[˜YÙYX]š^˜[œÙ›Ü›J‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\Ûİ\˜ÙH\ÈÜX›U˜[œÙ›Ü›SX]š^Ûİ\˜ÙHÜX›U˜[œÙ›Ü›H‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›U˜[œÙ›Ü›K•QÙ]ÜX›U˜[œÙ›Ü›SX]š^
İ]˜\ˆÜX›SX]š^
H‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•ÕÜ“X]š^‘
ÜX›SX]š^
H‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈYYXSX]š^˜[œÙ›Ü›J‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈYYXSX]š^‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•QÙ]›Ü\U˜[YJ™\Ûİ\˜ÙK•˜[YWˆ‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•T™XYX]š^
‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•PÜ™X]U˜[œÙ›Ü›SX]š^‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•PÜ™X]U˜[œÙ›Ü›QÜ›İ\X]š^‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•T™XYÜ[Û˜[İX›T›Ü\H‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•\S˜[YQ[™ÕÚ]
˜[œÙ›Ü›K•˜[œÛ]U˜[œÙ›Ü›WŠH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•\S˜[YQ[™ÕÚ]
˜[œÙ›Ü›K”ØØ[U˜[œÙ›Ü›WŠH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•\S˜[YQ[™ÕÚ]
˜[œÙ›Ü›K”›İ]U˜[œÙ›Ü›WŠH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•\S˜[YQ[™ÕÚ]
˜[œÙ›Ü›K”ÚÙ]Õ˜[œÙ›Ü›WŠH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•\S˜[YQ[™ÕÚ]
˜[œÙ›Ü›K•˜[œÙ›Ü›QÜ›İ\ŠH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š\ÜÙ[X›K‘Ù]\J”Ş\İ[K•Ú[™İÜË“YYXK“X]š^˜[œÙ›Ü›WŠH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠXİ]˜]Ü‹Ü™X]R[œİ[˜ÙH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š‘Ù]ÛÛœİXİÜœÊY[X™\‘›YÜÊH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›H[˜ÏØš™XİËYYXR[XYÙTÛİ\˜ÙOÏÈÚ[XYÙTÛİ\˜ÙPY\\ˆ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T™\^U[Pœ\Ú™Xİ[™ÛJœ\Ú[‹™Xİ[™ÛJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ô™Xİ[™ÛT[Y\•[Pœ\Ú
[‹™Xİ[™ÛJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T™Xİ[™ÛJ[[‹Ô™\^T™Xİ
™Xİ[™ÛJJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹Ü™X]T™Xİ[™ÛT]
™Xİ[™ÛJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T™\^U[Pœ\ÚÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]ÑÙ[ÛY]T[Y\•[Pœ\Ú
[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T\Ú˜]]™SYYXQÙ[ÛY]PÛ\
Û\Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË”\Ú˜]]™QÙ[ÛY]PÛ\
Û\Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™S[™QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™TÛ[[™QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™T™Xİ[™ÛQÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™Q[\ÙQÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ó˜]]™TÜX›QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]H\ÈÜX›QÙ[ÛY]T]Ûİ\˜ÙHÜX›QÙ[ÛY]TÛİ\˜ÙH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË‘˜]Ó˜]]™QÙ[ÛY]Jœ\Ú[‹ÜX›QÙ[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË‘˜]Ó˜]]™QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]š[Z]]™T™Xİ[™ÛTİ›ÚÙQÙ[ÛY]JÙ[ÛY]Kİ]™Xİ[™ÛJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜œ\ÚOH[ˆ[ˆOH[‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜œ\ÚOH[ˆ[ˆOH[‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXS[™QÙ[ÛY]T™XY\‹•QÙ]Û[[™TÙYÛY[ÊÙ[ÛY]Kİ]˜\ˆÙYÛY[ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]ÔÛ[[™TÙYÛY[Ê[‹ÙYÛY[ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛTİ›ÚÙP›İ[™ÊÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T\Úš[Z]]™T™Xİ[™ÛPÛ\
Û\Ù[ÛY]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊÛ\Ù[ÛY]Kİ]˜\ˆÛ\›İ[™ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXS[™QÙ[ÛY]T™XY\‹•QÙ][™TÚ[ÊÙ[ÛY]Kİ]İ\Ú[İ][™Ú[
H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]H\ÈYYXT™Xİ[™ÛQÙ[ÛY]H™Xİ[™ÛQÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹•QÙ][\ÙQÙ[ÛY]JÙ[ÛY]Kİ]Ù[\‹İ]˜Y]\Öİ]˜Y]\ÖJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™PÛ\Ú[šË”\Ú˜]]™PÛ\
Û\›İ[™ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™S[™J‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T™Xİ[™ÛJœ\Ú[‹™\^T™Xİ[™ÛJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T›İ[™Y™Xİ[™ÛJœ\Ú[‹™\^T™Xİ[™ÛK˜Y]\Ö˜Y]\ÖJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™Q[\ÙJœ\Ú[‹™]ÈÜ”™\^TÚ[
Ù[\‹–Ù[\‹–JK˜Y]\Ö˜Y]\ÖJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY™YÚ\İ\”™]Z[™Y\[™[˜ÚY\ÊØš™XİÈ\[™[˜ŞJH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY™YÚ\İ\”™]Z[™Y\[™[˜ÚY\ÊØš™XİÈš\œİØš™XİÈÙXÛÛ™
H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY™YÚ\İ\”™]Z[™Y\[™[˜ÚY\ÊØš™XİÈš\œİØš™XİÈÙXÛÛ™Øš™XİÈ\™
H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYİ]RY[JØš™XİÈİ]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYİ]RY[JØš™XİÈš\œİØš™XİÈÙXÛÛ™
H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYİ]RY[JØš™XİÈš\œİØš™XİÈÙXÛÛ™Øš™XİÈ\™
H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”™YÚ\İ\”™]Z[™Y\[™[˜ÚY\Ê\˜[\ÈØš™XİÖ×H\[™[˜ÚY\ÊH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠÛİ[[œİ\ÜYİ]RY[J\˜[\ÈØš™XİÖ×H[œİ\ÜYİ]JH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•Ü”™]Z[™Yš\İX[\[™[˜ŞT™YÚ\İ˜\‹”™YÚ\İ\ŠÜÚ[šË\[™[˜ÚY\ÊNÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T™\^S[™QÙ[ÛY]Q˜]Ú[™Ê‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T™\^TÛ[[™QÙ[ÛY]Q˜]Ú[™Ê‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜œ\Ú˜[YK‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\Ğœ\Ú‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİ]\ÈH\Ğœ\Ú	‰ˆ
œ\ÚOH[\Õ[Pœ\Ú
œ\Ú˜[YJJH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\Ğœ\Úˆ[ˆOH[‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]\™XİÛ[[™QÙ[ÛY]JÙ[ÛY]U˜[YKİ]˜\ˆÙYÛY[ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]ÔÛ[[™QÙ[ÛY]JÚ[šË[‹ÙYÛY[ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]\™Xİ™Xİ[™ÛTİ›ÚÙQÙ[ÛY]JÙ[ÛY]U˜[YKİ]˜\ˆİ›ÚÙT™Xİ[™ÛJH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]\™Xİ™Xİ[™ÛTİ›ÚÙQÙ[ÛY]JÙ[ÛY]U˜[YKİ]™Xİ[™ÛJH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\Ğœ\Úˆ[ˆOH[‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]\™Xİ[™QÙ[ÛY]P›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ó[™QÙ[ÛY]JÚ[šË[‹İ\Ú[[™Ú[
H‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXS[™QÙ[ÛY]T™XY\‹•QÙ][™TÚ[ÊYYXQÙ[ÛY]Kİ]İ\Ú[İ][™Ú[
H‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\ÜÔ\X[[™T™\^UÚ[œ\Ú\Õ[œİ\ÜY‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ“YYXS[™QÙ[ÛY]T™XY\ˆ‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]Ù[ÛY]U˜[œÙ›Ü›JÙ[ÛY]Kİ]˜\ˆ˜[œÙ›Ü›JH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹•PY\˜[œÙ›Ü›SX]š^
˜[œÙ›Ü›U˜[YKİ]˜[œÙ›Ü›JH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[œÙ›Ü›TÚ[
İ\Ú[˜[œÙ›Ü›Kİ]˜\ˆ˜[œÙ›Ü›YYİ\Ú[
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[œÙ›Ü›TÚ[
˜]ÔÙYÛY[Ú[ÖÚWK˜[œÙ›Ü›Kİ]˜\ˆ™^Ú[
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]H\ÈYYXS[™QÙ[ÛY]H[™QÙ[ÛY]H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]H\ÈYYXT]Ù[ÛY]H]Ù[ÛY]H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ™][Û˜[ÙXZÕX›OYYXQÙ[ÛY]KÙ[ÛY]Tš[Z]]™PØXÚOˆ‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ØXÚK•QÙ][™TÚ[ÊÙ[ÛY]Kİ]İ\Ú[İ][™Ú[
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ×Üš[Z]]™PØXÚK‘Ù]ÜÜ™X]U˜[YJÙ[ÛY]JK”Ù][™TÚ[Êš[Z]]™JH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛPÛÛ\]S[™TÚ[Ê‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛT™XY[™Tš[Z]]™Qš[™Ù\œš[
‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›H™XÛÜ™İXİ[™Tš[Z]]™Qš[™Ù\œš[
‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[Y]S[™TÚ[ÊÙ[ÛY]K\ÊH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ø[YU˜[œÙ›Ü›JØXÚK—Û[™U˜[œÙ›Ü›Kš[™Ù\œš[•˜[œÙ›Ü›JH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ØXÚK•QÙ]Û[[™TÙYÛY[ÊÙ[ÛY]Kİ]ÙYÛY[ÊH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ×Üš[Z]]™PØXÚK‘Ù]ÜÜ™X]U˜[YJÙ[ÛY]JK”Ù]Û[[™TÙYÛY[Êš[Z]]™JH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÚ[×OÈÜÛ[[™TÙYÛY[Ú[ÎÈ‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛT™XYÛ[[™TÙYÛY[Ú[Ê‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›H™XÛÜ™İXİÛ[[™Tš[Z]]™J‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[Y]TÛ[[™TÙYÛY[ÊÙ[ÛY]K\ËØXÚYØXÚYÚ[ÊH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ø[YU˜[œÙ›Ü›JØXÚK—ÜÛ[[™U˜[œÙ›Ü›K˜[œÙ›Ü›JH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ø[YTÚ[
ØXÚYÚ[ÖÚWK™^Ú[
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•U˜[Y]TÛ[[™TÙYÛY[ÊÙ[ÛY]KØXÚY
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”Ø[YTÚ[
ØXÚYØØXÚY[™^K”İ\Ú[İ\œ™[Ú[
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š\œ˜^K”™\Ú^™J™Yˆ[™TÙYÛY[È‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™šYİ\™K’\ĞÛÜÙYšYİ\™K”ÙYÛY[ËÛİ[OHH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™šYİ\™K”ÙYÛY[ÖÌH\ÈYYXS[™TÙYÛY[[™TÙYÛY[‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›[™TÙYÛY[’\Ôİ›ÚÙY‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ]XÈ›ÛÛQÙ]Û[[™TÙYÛY[Ê‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛTİ›ÚÙP›İ[™Ê]Ù[ÛY]Kİ]ÊH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆ[™TÙYÛY[ÈH™]ÈÜ”™\^S[™TÙYÛY[ÜÙYÛY[Ûİ[
È
šYİ\™K’\ĞÛÜÙY	‰ˆXÛÜÙ\ÕÔİ\ÈHˆ
WH‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›[™TÙYÛY[ÖİÜš][”ÙYÛY[Ûİ[
Ê×HH™]ÈÜ”™\^S[™TÙYÛY[
‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
šYİ\™K’\ĞÛÜÙY	‰ˆXÛÜÙ\ÕÔİ\
H‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[™XYÛ›H™XÛÜ™İXİÜ”™\^S[™TÙYÛY[‹Ü“YYXS[™QÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ“YYXQ[\ÙQÙ[ÛY]T™XY\ˆ‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]^\Ô™\Ù\š[™ÑÙ[ÛY]U˜[œÙ›Ü›JÙ[ÛY]Kİ]˜\ˆ˜[œÙ›Ü›JH‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹•PY\˜[œÙ›Ü›SX]š^
˜[œÙ›Ü›U˜[YKİ]˜[œÙ›Ü›JH‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[œÙ›Ü›TÚ[
ØØ[Ù[\‹˜[œÙ›Ü›Kİ]Ù[\ŠH‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[œÙ›Ü›T˜YZJØØ[˜Y]\ÖØØ[˜Y]\ÖK˜[œÙ›Ü›Kİ]˜Y]\Öİ]˜Y]\ÖJH‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“™X\›V™\›Ê˜[œÙ›Ü›K“LLŠH	‰ˆ™X\›V™\›Ê˜[œÙ›Ü›K“LŒJH‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“™X\›V™\›Ê˜[œÙ›Ü›K“LLJH	‰ˆ™X\›V™\›Ê˜[œÙ›Ü›K“LŒŠH‹Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊYYXQÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛTİ›ÚÙP›İ[™ÊYYXQÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊH‹Ü“Z[™[™\‘]QXÛÙ\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛTİ›ÚÙP›İ[™ÊÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹Ü“Z[™[™\‘]QXÛÙ\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹•QÙ][\ÙQÙ[ÛY]JÙ[ÛY]Kİ]˜\ˆÙ[\‹İ]˜\ˆ˜Y]\Öİ]˜\ˆ˜Y]\ÖJH‹Ü“Z[™[™\‘]QXÛÙ\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T™Xİ[™ÛJœ\Ú[‹™\^T™Xİ[™ÛJH‹Ü“Z[™[™\‘]QXÛÙ\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]ÓYYXQÙ[ÛY]JÚ[šËœ\Ú[‹Ù[ÛY]JH‹Ü“Z[™[™\‘]QXÛÙ\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ“YYXT™Xİ[™ÛPÛ\™XY\ˆ‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]™Xİ[™ÛTİ›ÚÙP›İ[™ÊYYXQÙ[ÛY]HÙ[ÛY]Kİ]Ü”™\^T™Xİ›İ[™ÊH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\]Z\™Qš[YˆYH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\]Z\™Tİ›ÚÙYÙYÛY[ÎˆYH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\]Z\™Qš[Y	‰ˆYšYİ\™K’\Ñš[Y‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\]Z\™Tİ›ÚÙYÙYÛY[È	‰ˆ[[™TÙYÛY[’\Ôİ›ÚÙY‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Xİ[™ÛQÙ[ÛY]K”˜Y]\ÖOH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Xİ[™ÛQÙ[ÛY]K”˜Y]\ÖHOH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]H\ÈYYXT]Ù[ÛY]H]Ù[ÛY]H‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÚ[HšYİ\™K”İ\Ú[‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™šYİ\™K”ÙYÛY[ÖÌH\È›İYYXS[™TÙYÛY[[™TÙYÛY[‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•PÜ™X]T™Xİ[™ÛQœ›ÛTÛYÛÛŠÚ[Ú[KÚ[‹Ú[Ëİ]›İ[™ÊH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•SX\šÔ™Xİ[™ÛPÛÜ›™\ŠÚ[YÜšYÚ›İÛH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\Ğ^\Ğ[YÛ™Y™Xİ[™ÛQYÙJÚ[ËÚ[
H‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\ÕÜY	‰ˆ\ÕÜšYÚ	‰ˆ\Ğ›İÛTšYÚ	‰ˆ\Ğ›İÛSY‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š˜\ˆÚ[ÈH™]ÈÚ[ÍH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”Ú[×HÚ[È‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠœÚ[ÖÊH
ÈJH	HÚ[Ë“[™İH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü‘˜]Ú[™Ô™\^K•T™\^U[Pœ\Úš[
‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T™\^U[Pœ\Ú™Xİ[™ÛJœ\Ú[‹™Xİ[™ÛKYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ô™Xİ[™ÛT[Y\•[Pœ\Ú
YYXT[‹YYXT™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™S[™QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]KYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™S[™QÙ[ÛY]T[ŠÙ[ÛY]KYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™TÛ[[™QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]KYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™TÛ[[™QÙ[ÛY]T[ŠÙ[ÛY]KYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜œ\ÚOH[ˆYYXT[ˆOH[‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXS[™QÙ[ÛY]T™XY\‹•QÙ]Û[[™TÙYÛY[ÊYYXQÙ[ÛY]Kİ]ÙYÛY[ÊH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™T™Xİ[™ÛQÙ[ÛY]Jœ\Ú[‹Ù[ÛY]KYYXPœ\ÚYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™T™Xİ[™ÛQÙ[ÛY]T[ŠÙ[ÛY]KYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T™XY™Xİ[™ÛTİ›ÚÙQÙ[ÛY]JÙ[ÛY]Kİ]™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜œ\ÚOH[ˆYYXT[ˆOH[‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™Q[\ÙQÙ[ÛY]Jœ\Ú[‹Ù[ÛY]KYYXPœ\ÚYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ôš[Z]]™Q[\ÙQÙ[ÛY]T[ŠÙ[ÛY]KYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊYYXQÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXT™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛTİ›ÚÙP›İ[™ÊYYXQÙ[ÛY]Kİ]˜\ˆ™Xİ[™ÛP›İ[™ÊH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹Ü™X]T™Xİ[™ÛT]
YYXT™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÚ[šÈ\ÈUÜ“˜]]™Tš[Z]]™PÛÛ[X[™Ú[šÈ˜]]™TÚ[šÈ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™S[™JYYXT[‹™\^TÚ[™\^TÚ[JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXS[™QÙ[ÛY]T™XY\‹•QÙ][™TÚ[ÊYYXQÙ[ÛY]Kİ]İ\Ú[İ][™Ú[
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T™Xİ[™ÛJYYXPœ\ÚYYXT[‹™\^T™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T™Xİ[™ÛJ[[‹Ô™\^T™Xİ
™Xİ[™ÛJJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™T›İ[™Y™Xİ[™ÛJYYXPœ\ÚYYXT[‹™\^T™Xİ[™ÛKYYXT˜Y]\ÖYYXT˜Y]\ÖJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™Q[\ÙJYYXPœ\ÚYYXT[‹™\^PÙ[\‹YYXT˜Y]\ÖYYXT˜Y]\ÖJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]H\ÈYYXT™Xİ[™ÛQÙ[ÛY]H™Xİ[™ÛQÙ[ÛY]H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXQ[\ÙQÙ[ÛY]T™XY\‹•QÙ][\ÙQÙ[ÛY]JYYXQÙ[ÛY]Kİ]Ù[\‹İ]˜Y]\Öİ]˜Y]\ÖJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ó˜]]™Tš[Z]]™T™Xİ[™ÛJ˜]]™TÚ[šËYYXPœ\ÚYYXT[‹™Xİ[™ÛK˜Y]\Ö˜Y]\ÖJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™Q[\ÙJYYXPœ\ÚYYXT[‹™]ÈÜ”™\^TÚ[
Ù[\‹–Ù[\‹–JK˜Y]\Ö˜Y]\ÖJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™R[XYÙJYYXR[XYÙTÛİ\˜ÙK™\^T™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹•PY\˜]]™QÛ\[ŠÛ\[‹İ]˜\ˆ˜]]™QÛ\[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™TÚ[šË‘˜]Ó˜]]™QÛ\[ŠYYXPœ\Ú˜]]™QÛ\[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY™YÚ\İ\”™]Z[™Y\[™[˜ÚY\ÊØš™XİÈ\[™[˜ŞJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY™YÚ\İ\”™]Z[™Y\[™[˜ÚY\ÊØš™XİÈš\œİØš™XİÈÙXÛÛ™
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY™YÚ\İ\”™]Z[™Y\[™[˜ÚY\ÊØš™XİÈš\œİØš™XİÈÙXÛÛ™Øš™XİÈ\™
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYİ]RY[JØš™XİÈİ]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYİ]RY[JØš™XİÈš\œİØš™XİÈÙXÛÛ™
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYİ]RY[JØš™XİÈš\œİØš™XİÈÙXÛÛ™Øš™XİÈ\™
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYY”™\Ù[
Øš™XİÈİ]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYY”™\Ù[
Øš™XİÈš\œİØš™XİÈÙXÛÛ™
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛİ[[œİ\ÜYY”™\Ù[
Øš™XİÈš\œİØš™XİÈÙXÛÛ™Øš™XİÈ\™
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”™YÚ\İ\”™]Z[™Y\[™[˜ÚY\Ê\˜[\ÈØš™XİÖ×H\[™[˜ÚY\ÊH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠÛİ[[œİ\ÜYİ]RY[J\˜[\ÈØš™XİÖ×H[œİ\ÜYİ]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠÛİ[[œİ\ÜYY”™\Ù[
\˜[\ÈØš™XİÖ×H[œİ\ÜYİ]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•Ü”™]Z[™Yš\İX[\[™[˜ŞT™YÚ\İ˜\‹”™YÚ\İ\ŠÜÚ[šË\[™[˜ÚY\ÊNÈ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹•PY\˜]]™QÛ\[ŠÛ\[‹İ]˜\ˆ˜]]™QÛ\[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Q˜]Ó˜]]™TÜX›QÙ[ÛY]Jœ\Ú[‹Ù[ÛY]KYYXPœ\ÚYYXT[ŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË‘˜]Ó˜]]™QÙ[ÛY]JYYXPœ\ÚYYXT[‹ÜX›QÙ[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË‘˜]Ó˜]]™QÙ[ÛY]JYYXPœ\ÚYYXT[‹YYXQÙ[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]ÓYYXQÙ[ÛY]JYYXPœ\ÚYYXT[‹YYXQÙ[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T\Úš[Z]]™T™Xİ[™ÛPÛ\
Û\Ù[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™PÛ\Ú[šË”\Ú˜]]™PÛ\
Ô™\^T™Xİ
™Xİ[™ÛJJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T\Ú˜]]™TÜX›PÛ\
Û\Ù[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹•QÙ]™Xİ[™ÛPÛ\›İ[™ÊÜX›QÙ[ÛY]Kİ]˜\ˆÛ\›İ[™ÊH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™PÛ\Ú[šË”\Ú˜]]™PÛ\
Û\›İ[™ÊH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË”\Ú˜]]™QÙ[ÛY]PÛ\
ÜX›QÙ[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•T\Ú˜]]™SYYXQÙ[ÛY]PÛ\
YYXQÙ[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]]™QÙ[ÛY]TÚ[šË”\Ú˜]]™QÙ[ÛY]PÛ\
Û\Ù[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ”ÜX›T™Xİ[™ÛPÛ\™XY\ˆ‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]K’Ú[™OHÜX›QÙ[ÛY]T]Ú[™”]‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÚ[HšYİ\™K”İ\Ú[‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÙYÛY[HšYİ\™K”ÙYÛY[ÖÌH‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙYÛY[’Ú[™OHÜX›T]ÙYÛY[Ú[™“[™H‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•PÜ™X]T™Xİ[™ÛPÛ\œ›ÛTÛYÛÛŠÚ[Ú[KÚ[‹Ú[Ëİ]›İ[™ÊH‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•SX\šÔ™Xİ[™ÛPÛÜ›™\ŠÚ[YÜšYÚ›İÛH‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\Ğ^\Ğ[YÛ™Y™Xİ[™ÛQYÙJÚ[ËÚ[
H‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\ÕÜY	‰ˆ\ÕÜšYÚ	‰ˆ\Ğ›İÛTšYÚ	‰ˆ\Ğ›İÛSY‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š˜\ˆÚ[ÈH™]ÈÜX›TÚ[ÍH‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”ÜX›TÚ[×HÚ[È‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠœÚ[ÖÊH
ÈJH	HÚ[Ë“[™İH‹Ü”ÜX›T™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\^TİX™YRÙY\Ô™]˜XÙYÜX›T]Û\İ]Ù”™]Z[™Y™Xİ[™ÛTİ]H‹š[K”™XY[^
š[™™\Ô]
ˆœÜ˜È‹ˆ”›ÑÔK•Ü‹•\İÈ‹ˆÛÛ\ÜÚ][Ûˆ‹ˆ“Z[‹ˆ•Ü•š\İX[™YT™[™\™\•\İË˜ÜÈŠJKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]^\Ğ[YÛ™YÙ[ÛY]U˜[œÙ›Ü›JÙ[ÛY]Kİ]˜\ˆ˜[œÙ›Ü›JH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[œÙ›Ü›P^\Ğ[YÛ™Y›İ[™Ê›İ[™Ë˜[œÙ›Ü›Kİ]›İ[™ÊH‹Ü“YYXT™Xİ[™ÛPÛ\™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ”ÜX›T]›İ[™Ô™XY\ˆ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ]XÈ›ÛÛQÙ]]›İ[™ÊÜX›QÙ[ÛY]T]Ù[ÛY]Kİ]Ü”™\^T™Xİ›İ[™ÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]^\Ğ[YÛ™Y˜[œÙ›Ü›JÙ[ÛY]K•˜[œÙ›Ü›Kİ]˜\ˆ˜[œÙ›Ü›JH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•U˜[œÙ›Ü›P^\Ğ[YÛ™Y›İ[™ÊØØ[›İ[™Ë˜[œÙ›Ü›Kİ]›İ[™ÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹•QÙ]˜]]™T]›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ”ÜX›T]Ù[ÛY]PÛÛ™\\ˆ‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ]XÈ›ÛÛQÙ]˜]]™T]›İ[™ÊÜX›QÙ[ÛY]T]ÜX›T]İ]Ü”™\^T™Xİ›İ[™ÊH‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆšYİ\™\ÈHÙ[ÛY]K‘šYİ\™\ÎÈ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Üˆ
˜\ˆšYİ\™R[™^HÈšYİ\™R[™^šYİ\™\Ë“[™İÈšYİ\™R[™^
ÊÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÙYÛY[ÈHšYİ\™K”ÙYÛY[ÎÈ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Üˆ
˜\ˆÙYÛY[[™^HÈÙYÛY[[™^ÙYÛY[Ë“[™İÈÙYÛY[[™^
ÊÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š™›Ü™XXÚ
˜\ˆšYİ\™H[ˆÙ[ÛY]K‘šYİ\™\ÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š™›Ü™XXÚ
˜\ˆÙYÛY[[ˆšYİ\™K”ÙYÛY[ÊH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÜX›QšYİ\™\ÈHÜX›T]‘šYİ\™\ÎÈ‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Üˆ
˜\ˆšYİ\™R[™^HÈšYİ\™R[™^ÜX›QšYİ\™\Ë“[™İÈšYİ\™R[™^
ÊÊH‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÙYÛY[ÈHÜX›QšYİ\™K”ÙYÛY[ÎÈ‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Üˆ
˜\ˆÙYÛY[[™^HÈÙYÛY[[™^ÙYÛY[Ë“[™İÈÙYÛY[[™^
ÊÊH‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š™›Ü™XXÚ
˜\ˆÜX›QšYİ\™H[ˆÜX›T]‘šYİ\™\ÊH‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š™›Ü™XXÚ
˜\ˆÙYÛY[[ˆÜX›QšYİ\™K”ÙYÛY[ÊH‹Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÜX›TÙYÛY[ÈHÜX›QšYİ\™K”ÙYÛY[ÎÈ‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Üˆ
˜\ˆÙYÛY[[™^HÈÙYÛY[[™^ÜX›TÙYÛY[Ë“[™İÈÙYÛY[[™^
ÊÊH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š™›Ü™XXÚ
˜\ˆÜX›QšYİ\™H[ˆÜX›T]‘šYİ\™\ÊH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š™›Ü™XXÚ
˜\ˆÙYÛY[[ˆÜX›QšYİ\™K”ÙYÛY[ÊH‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù[ÛY]K’Ú[™OHÜX›QÙ[ÛY]T]Ú[™ÛÛXš[™Y‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛQÙ]ÛÛXš[™Y›İ[™È‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛR[\œÙXİ›İ[™È‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø\ÙHÜX›T]ÙYÛY[Ú[™“[™Nˆ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø\ÙHÜX›T]ÙYÛY[Ú[™”]XY˜]XĞ™^šY\ˆ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø\ÙHÜX›T]ÙYÛY[Ú[™İXšXĞ™^šY\ˆ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø\ÙHÜX›T]ÙYÛY[Ú[™\˜Îˆ‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛR[˜ÛYT]XY˜]XÑ^™[][H‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛR[˜ÛYPİXšXÑ^™[XH‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•™XİÜ\˜ÔÙYÛY[Ù[ÛY]K•QÙ]\˜Ğ›İ[™È‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊŠ\ÙYÛY[’\Ôİ›ÚÙY	‰ˆYšYİ\™K’\Ñš[Y
H‹Ü”ÜX›T]›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹•QÙ]Ù[ÛY]P›İ[™ÊÜX›QÙ[ÛY]Kİ]˜\ˆÜX›P›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜ”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹•QÙ]Ù[ÛY]P›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊNÈ‹Ü•š\İX[™YT™[™\™\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ”ÜX›QÙ[ÛY]P›İ[™Ô™XY\ˆ‹Ü”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”ÜX›T]›İ[™Ô™XY\‹•QÙ]]›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊH‹Ü”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”ÜX›T]Ù[ÛY]PÛÛ™\\‹•QÙ]˜]]™T]›İ[™ÊÙ[ÛY]Kİ]›İ[™ÊH‹Ü”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊˆUÜ”ÜX›QÙ[ÛY]T]]K’\Ô]]JÙ[ÛY]JH‹Ü”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ\ÜÈÜ”ÜX›QÙ[ÛY]T]]H‹Ü”ÜX›QÙ[ÛY]T]]Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ]XÈ›ÛÛ\Ô]]JÜX›QÙ[ÛY]T]Ù[ÛY]JH‹Ü”ÜX›QÙ[ÛY]T]]Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXQÙ[ÛY]P›İ[™Ô™XY\‹•QÙ]Ù[ÛY]P›İ[™ÊÙ[ÛY]Kİ]˜\ˆÙ[ÛY]P›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXQÙ[ÛY]P›İ[™Ô™XY\‹•QÙ]Ù[ÛY]P›İ[™ÊYYXQÙ[ÛY]Kİ]˜\ˆYYXQÙ[ÛY]P›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü“YYXQÙ[ÛY]P›İ[™Ô™XY\‹•QÙ]Ù[ÛY]P›İ[™ÊÛ\Ù[ÛY]Kİ]˜\ˆÙ[ÛY]PÛ\›İ[™ÊH‹Ü‘˜]Ú[™Ô™\^Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜ”ÜX›QÙ[ÛY]P›İ[™Ô™XY\‹•QÙ]Ù[ÛY]P›İ[™ÊÜX›QÙ[ÛY]Kİ]›İ[™ÊNÈ‹Ü“YYXQÙ[ÛY]P›İ[™Ô™XY\‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛQÙ]ÜX›QÙ[ÛY]T]
Øš™XİÈÙ[ÛY]Kİ]ÜX›QÙ[ÛY]T]ÜX›QÙ[ÛY]JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛT™XY™\^TÚ[
Øš™XİÈÚ[˜[YKİ]Ü”™\^TÚ[Ú[
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛT™XY™\^T™Xİ
Øš™XİÈ™Xİ˜[YKİ]Ü”™\^T™Xİ™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÚ[˜[YH\ÈÚ[YYXTÚ[‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Xİ˜[YH\È™XİYYXT™Xİ	‰ˆ[YYXT™Xİ’\Ñ[\H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÚ[˜[YH\ÈÜX›TÚ[ÜX›TÚ[‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Xİ˜[YH\ÈÜX›T™XİÜX›T™Xİ	‰ˆ\ÜX›T™Xİ’\Ñ[\H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛT™XYÚ[
Øš™XİÈÚ[˜[YKİ]Ú[Ú[
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÛÛT™XY™Xİ
Øš™XİÈ™Xİ˜[YKİ]™Xİ™Xİ[™ÛJH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Xİ˜[YH\ÈÜ”™\^T™Xİ™\^T™Xİ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÚ[˜[YH\ÈÚ[YYXTÚ[‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Xİ˜[YH\È™XİYYXT™Xİ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•T™XYØØ[™\^TÚ[‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•T™XYØØ[™\^T™Xİ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š˜Ø]Ú
\SØY^Ù\[ÛŠH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š\Ú[™ÈŞ\İ[K”™Y›Xİ[Ûˆ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Šš[™[™Ñ›YÜÈ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•\K‘Ù]\H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•T™XYÚ[H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•T™XYİX›T›Ü\H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š‘Ù]›Ü\J‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
YYXPœ\ÚOH[
H‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÚ[šÈ\ÈUÜ“˜]]™U˜[œÙ›Ü›PÛÛ[X[™Ú[šÈ˜]]™U˜[œÙ›Ü›TÚ[šÈ‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ü”™\Ûİ\˜ÙT™\ÛÛ™\‹•PY\˜[œÙ›Ü›SX]š^
˜[œÙ›Ü›Kİ]˜\ˆ˜]]™U˜[œÙ›Ü›JH‹Ü“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Ü[ÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^
UÜ’[XYÙTÛİ\˜ÙPY\\È[XYÙTÛİ\˜ÙPY\\ŠH‹›ÑÜUÜ‘˜]Ú[™Ñœ˜[YKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Ü[ÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^
Xİ]™UÜ’[XYÙTÛİ\˜ÙPY\\ŠH‹›ÑÜUÜ•Ú[™İÒÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^™\^\ÓYYXQ˜]Ú[™Ğœ\Ú™Y›Ü™QÙ[™\šXÓYYXPœ\Ú]‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”\Ú˜]]™U˜[œÙ›Ü›Wˆ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^™\^\ÓYYXR[XYÙPœ\Ú™Xİ[™ÛU›İYÚ[XYÙTÛİ\˜ÙPY\\ˆ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ[Pœ\Ú™Xİ[™ÛT[\Ó˜]]™T™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ[Pœ\ÚÙ[ÛY]T[\Ó˜]]™SYYXQÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜[Ğ˜XÚÕÑÙ[™\šXÓYYXPœ\ÚÚ[•[T™\^U[œİ\ÜY‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÓ[™T]Ù[ÛY]P\Ó˜]]™S[™UÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YY[™QÙ[ÛY]P\Ó˜]]™S[™H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔÛ[[™T]Ù[ÛY]P\Ó˜]]™S[™\ÕÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YYÛ[[™T]Ù[ÛY]P\Ó˜]]™S[™\È‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜĞÛÜÙYÛ[[™T]Ù[ÛY]P\Ó˜]]™S[™\ÕÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔ™Xİ[™ÛQÙ[ÛY]P\Ó˜]]™T™Xİ[™ÛUÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔ™Xİ[™ÛT]Ù[ÛY]P\Ó˜]]™T™Xİ[™ÛUÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ[™š[Y™Xİ[™ÛT]Ù[ÛY]P\Ó˜]]™T™Xİ[™ÛTİ›ÚÙUÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔ™Xİ[™ÛQÙ[ÛY]P\Ô™Xİ[™ÛUÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔ›İ[™Y™Xİ[™ÛQÙ[ÛY]P\Ó˜]]™T›İ[™Y™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YY™Xİ[™ÛQÙ[ÛY]P\Ó˜]]™T™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÑ[\ÙQÙ[ÛY]P\Ó˜]]™Q[\ÙUÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔÜX›T]Ù[ÛY]P\Ó˜]]™QÙ[ÛY]UÚ]İ]X[˜YÙYÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÓ›Û”š[Z]]™T]Ù[ÛY]P\Ó˜]]™SYYXQÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÑ[\ÙQÙ[ÛY]P\Ñ[\ÙUÚ]İ]Ù[™\šXÑÙ[ÛY]Q˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YY[\ÙQÙ[ÛY]P\Ó˜]]™Q[\ÙH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^˜]ÜÔÚÙ]ÙY[\ÙQÙ[ÛY]P\Ó˜]]™SYYXQÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[™Xİ[™ÛQÙ[ÛY]P\Ó˜]]™T™Xİ[™ÛUÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[[™T]Ù[ÛY]P\Ó˜]]™S[™UÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YY[™QÙ[ÛY]P\Ó˜]]™S[™H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[Û[[™T]Ù[ÛY]P\Ó˜]]™S[™\ÕÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜĞÛÜÙYÛ[[™T]Ù[ÛY]P\Ó˜]]™S[™\ÕÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[™QÙ[ÛY]T™Yœ™\Ú\Ôš[Z]]™S[™TÚ[ØXÚUÚ[”Ú\PÚ[™Ù\È‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[™QÙ[ÛY]T™Yœ™\Ú\Ôš[Z]]™S[™TÚ[ØXÚUÚ[•˜[œÙ›Ü›PÚ[™Ù\È‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Û[[™T]Ù[ÛY]T™]\Ù\Ôš[Z]]™TÙYÛY[ØXÚUÚ[”Ú\R\Õ[˜Ú[™ÙY‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Û[[™T]Ù[ÛY]T™Yœ™\Ú\Ôš[Z]]™TÙYÛY[ØXÚUÚ[”Ú\PÚ[™Ù\È‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[™Xİ[™ÛT]Ù[ÛY]P\Ó˜]]™T™Xİ[™ÛUÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÕ[™š[Y™Xİ[™ÛT]Ù[ÛY]P\Ó˜]]™T™Xİ[™ÛTİ›ÚÙUÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[›İ[™Y™Xİ[™ÛQÙ[ÛY]P\Ó˜]]™T›İ[™Y™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[›İ[™Y™Xİ[™ÛQÙ[ÛY]P\Ô›İ[™Y™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YY™Xİ[™ÛQÙ[ÛY]P\Ó˜]]™T™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[[\ÙQÙ[ÛY]P\Ó˜]]™Q[\ÙUÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓ›Û”š[Z]]™T]Ù[ÛY]P\Ó˜]]™SYYXQÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÓØØ[[\ÙQÙ[ÛY]P\Ñ[\ÙUÚ]İ]Ù[™\šXÑÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÕ˜[œÙ›Ü›YY[\ÙQÙ[ÛY]P\Ó˜]]™Q[\ÙH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÔÚÙ]ÙY[\ÙQÙ[ÛY]P\Ó˜]]™SYYXQÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\^\Õ˜[œÙ›Ü›YYØØ[[™QÙ[ÛY]P\Ó˜]]™S[™UÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\^\ÓØØ[[™T]Ù[ÛY]P\Ó˜]]™S[™UÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\^\ÓØØ[Û[[™T]Ù[ÛY]P\Ó˜]]™S[™\ÕÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\^\ĞÛÜÙYÛ[[™T]Ù[ÛY]P\Ó˜]]™S[™\ÕÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\^\ÓØØ[™Xİ[™ÛT]Ù[ÛY]P\Ó˜]]™T™Xİ[™ÛUÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜]Ñ˜]Ú[™Ô™\^\Õ[™š[Y™Xİ[™ÛT]Ù[ÛY]P\Ó˜]]™T™Xİ[™ÛTİ›ÚÙUÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^\Ú\Ô™Xİ[™ÛQÙ[ÛY]PÛ\\Ó˜]]™PÛ\Ú]İ]Ù[™\šXĞÛ\˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^\Ú\Ô™Xİ[™ÛT]Ù[ÛY]PÛ\\Ó˜]]™PÛ\Ú]İ]Ù[™\šXĞÛ\˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^\Ú\Ô›İ[™Y™Xİ[™ÛQÙ[ÛY]PÛ\\Ó˜]]™QÙ[ÛY]PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^\Ú\Õ˜[œÙ›Ü›YY™Xİ[™ÛT]Ù[ÛY]PÛ\\Ó˜]]™PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^\Ú\Ó›Û”™Xİ[™ÛT]Ù[ÛY]PÛ\\Ó˜]]™QÙ[ÛY]PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù[™\˜]Y˜]Ú[™ĞÛÛ^\Ú\Ò[˜ÛÛ\]T™Xİ[™ÛT]Ù[ÛY]PÛ\\Ó˜]]™QÙ[ÛY]PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ú\ÓØØ[™Xİ[™ÛQÙ[ÛY]PÛ\\Ó˜]]™PÛ\Ú]İ]Ù[™\šXĞÛ\˜[˜XÚÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ú\ÔÜX›T™XİÛ\\Ó˜]]™PÛ\Ú]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ú\Ô›İ[™Y™Xİ[™ÛQÙ[ÛY]PÛ\\Ó˜]]™QÙ[ÛY]PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ú\Õ˜[œÙ›Ü›YY™Xİ[™ÛQÙ[ÛY]PÛ\\Ó˜]]™PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜[Ğ˜XÚÕÑÙ[™\šXÓYYXPœ\ÚÚ[•[T™\^U[œİ\ÜY‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ú\ÔÜX›U˜[œÙ›Ü›\Õ›İYÚ˜]]™TÚ[šÈ‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^Y\Õ\Yš[Z]]™U˜[Y\È‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^Y\Ô™Y›XİYš[Z]]™U˜[Y\È‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ù\Ó˜]]™Tš[Z]]™\ÕÚ[]˜Z[X›H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ù\Ó˜]]™TÜX›QÙ[ÛY]UÚ[]˜Z[X›H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÔÜX›T™XİÙ[ÛY]P\Ó˜]]™T™Xİ[™ÛUÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÔ™\^T™XİÙ[ÛY]P\Ô™Xİ[™ÛUÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÔÜX›T™Xİ[Pœ\Ú[\Ô™Xİ[™ÛUÚ]İ]X[˜YÙYÙ[ÛY]H‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^˜]ÜÕ[Pœ\Ú™Xİ[™ÛT[\Ó˜]]™T™Xİ[™ÛH‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ú\ÔÜX›T™Xİ[™ÛPÛ\\Ó˜]]™PÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ™[™\‘]Q˜]Ú[™ĞÛÛ^\Ù\Ó˜]]™TÜX›QÙ[ÛY]PÛ\›Ü“›Û”™Xİ[™ÛPÛ\‹ÜÛÛ\ÜÚ][Û‘˜]Ú[™ĞÛÛ^\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š‘XÛÙT\Ú˜[œÙ›Ü›Q˜[Ğ˜XÚÕÓØØ[X]š^˜[œÙ›Ü›UÚ[‘›Ü™ZYÛ\ÜÙ[X›TÚYİÜÕ\H‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\•\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š—”Ş\İ[K•Ú[™İÜË“YYXK“X]š^˜[œÙ›Ü›Wˆ‹Ü”™\Ûİ\˜ÙT™\ÛÛ™\•\İËİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‰
ÓTĞZ[N’\ÓÔÔ]›Ü›J	ÕÚ[™İÜÉÊJH‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÔİÙ\”Ú[^HÛÛ™][ÛW‰É
ÔİÙ\”Ú[^JIÈOH	É×œÜÚ×ÔİÙ\”Ú[^Oˆ‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SX[˜YÙYÜ•˜[œÜÜ^[ØY‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‰
XÚØYÙS˜[YKÛÛZ[œÊ	ÓXœ™UÔ‹•˜[œÜÜ	ÊJH‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—	
\™Ù]œ˜[Y]ÛÜšÊWZXÜ›ÜÛÙ•Ú[ŒÌ‹”Ş\İ[Q]™[Ë™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Y—	
\™Ù]œ˜[Y]ÛÜšÊWZXÜ›ÜÛÙ•Ú[ŒÌ‹”Ş\İ[Q]™[Ë™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—	
\™Ù]œ˜[Y]ÛÜšÊW™\Ù[][Û‘œ˜[Y]ÛÜšË™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Y—	
\™Ù]œ˜[Y]ÛÜšÊW™\Ù[][Û‘œ˜[Y]ÛÜšË™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—	
\™Ù]œ˜[Y]ÛÜšÊWŞ\İ[K•Ú[™İÜË”™\Ù[][Û‹™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Y—	
\™Ù]œ˜[Y]ÛÜšÊWŞ\İ[K•Ú[™İÜË”™\Ù[][Û‹™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—	
\™Ù]œ˜[Y]ÛÜšÊWXØÙ\ÜÚXš[]K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠÔ™\]Z\™YX[˜YÙYÜ•˜[œÜÜ^[ØY[˜ÛYOHˆ‰
ÓX[˜YÙYÜ•˜[œÜÜ›Ûİ
W™Y—	
\™Ù]œ˜[Y]ÛÜšÊWXØÙ\ÜÚXš[]K™ˆˆÏˆ‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÓX[˜YÙYÜ•˜[œÜÜXØÙ\ÜÚXš[]T™Y‰
ÓX[˜YÙYÜ•˜[œÜÜ›Ûİ
W™Y—	
\™Ù]œ˜[Y]ÛÜšÊWXØÙ\ÜÚXš[]K™×ÓX[˜YÙYÜ•˜[œÜÜXØÙ\ÜÚXš[]T™Yˆ‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[]Hš[\ÏHˆ‰
ÓX[˜YÙYÜ•˜[œÜÜXØÙ\ÜÚXš[]T™YŠHˆˆ‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—	
\™Ù]œ˜[Y]ÛÜšÊW™\Ù[][Û‘œ˜[Y]ÛÜšË‘›Y[™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ›Ü™XXÚ
İš[™È[YP\ÜÙ[X›H[ˆÜ•[YP\ÜÙ[X›Y\ÊBˆÂˆ\ÜÙ\ÛÛZ[œÊ	›X—	
\™Ù]œ˜[Y]ÛÜšÊWİ[YP\ÜÙ[X›_K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ	œ™Y—	
\™Ù]œ˜[Y]ÛÜšÊWİ[YP\ÜÙ[X›_K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆBˆ\ÜÙ\ÛÛZ[œÊ	›X—	
\™Ù]œ˜[Y]ÛÜšÊWÜšX˜›Û\ÜÙ[X›_K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ	œ™Y—	
\™Ù]œ˜[Y]ÛÜšÊWÜšX˜›Û\ÜÙ[X›_K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYX[˜YÙYÜ•˜[œÜÜš]˜]UÚ[‘›Ü›\Ô^[ØY‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‰
ÙÓZXÜ›ÜÛÙÔš]˜]WÕÚ[™›Ü›\ÊH‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYX[˜YÙYÜ•˜[œÜÜŞ\İ[Q˜]Ú[™ĞÛÜ™T^[ØY‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYX[˜YÙYÜ•˜[œÜÜXØÙ\ÜÚXš[]T^[ØY‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‰
ÙĞXØÙ\ÜÚXš[]JH‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—™]ÛÜ™X\ËŒXØÙ\ÜÚXš[]K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙT™Y™\™[˜ÙH[˜ÛYOW‰
Ş\İ[Q˜]Ú[™ĞÛÛ[[Û”XÚØYÙJWˆ‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‰
ÙÔŞ\İ[WÑ˜]Ú[™×ĞÛÛ[[ÛŠH‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœŞ\İ[K™˜]Ú[™Ë˜ÛÛ[[Û—	
Ş\İ[Q˜]Ú[™ĞÛÛ[[Û•™\œÚ[ÛŠH‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X—	
\™Ù]œ˜[Y]ÛÜšÊWŞ\İ[K”š]˜]K•Ú[™İÜËÛÜ™K™‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜ˜ËÓZXÜ›ÜÛÙ‘İ™]•Ü‹ÜÜ˜ËÔŞ\İ[K•Ú[™İÜË”™\Ù[][Û‹ÔŞ\İ[K•Ú[™İÜË”™\Ù[][Û‹˜ÜÜ›Úˆ‹˜[Y][Û‘Ü˜\Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊZ[HX[˜YÙYÔˆ\ÜÙ[X›Y\È›Üˆ	
ÛÛ™šYİ\˜][ÛŠ_	
\™Ù]œ˜[Y]ÛÜšÊH™Y›Ü™HXÚÚ[™È	
XÚØYÙS˜[YJKˆ‹Ü•˜[œÜÜ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û”™Yš^ÛÛ™][ÛW‰É
XÚØYÙU™\œÚ[ÛŠIÈOH	ÌŒKŒ\™]šY]ËŒÍI×ŒŒKŒÕ™\œÚ[Û”™Yš^ˆ‹XÚØYÚ[™Õ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û”İY™š^ÛÛ™][ÛW‰É
XÚØYÙU™\œÚ[ÛŠIÈOH	ÌŒKŒ\™]šY]ËŒÍI×œ™]šY]ËŒÍOÕ™\œÚ[Û”İY™š^ˆ‹XÚØYÚ[™Õ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[ÛˆÛÛ™][ÛW‰É
™\œÚ[ÛŠIÈOH	É
XÚØYÙU™\œÚ[ÛŠIÈ[™	É
XÚØYÙU™\œÚ[ÛŠIÈOH	É×‰
XÚØYÙU™\œÚ[ÛŠOÕ™\œÚ[Ûˆ‹XÚØYÚ[™Õ\™Ù]Ëİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û”™Yš^ŒŒKŒÕ™\œÚ[Û”™Yš^ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û”İY™š^œ™]šY]ËŒÍOÕ™\œÚ[Û”İY™š^ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û‰
™\œÚ[Û”™Yš^
KI
™\œÚ[Û”İY™š^
OÕ™\œÚ[Ûˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙU™\œÚ[Û‰
™\œÚ[ÛŠOÔXÚØYÙU™\œÚ[Ûˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙT™XYYQš[O”‘PQQK›YÔXÚØYÙT™XYYQš[Oˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÚ[™ĞÛÛ[[˜ÛYOW”‘PQQK›YˆİX‘›Û\Wœ›ÛİˆÏˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Û™H[˜ÛYOW”‘PQQK›YˆXÚÏWYWˆXÚØYÙT]W—ˆÏˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[˜ÛYP\ÜÙ[X›Y\Ò[\˜Ú™]]˜[XÚØYÙOYOÒ[˜ÛYP\ÜÙ[X›Y\Ò[\˜Ú™]]˜[XÚØYÙOˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]›Ü›R[™\[™[XÚØYÙOYOÔ]›Ü›R[™\[™[XÚØYÙOˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\™Ù]˜[YOW”™[[İ™Tİ[SXœ™UÜ•˜[œÜÜ^[ØYˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y›Ü™U\™Ù]ÏWÜ™X]PÛÛ[›Û\—ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[˜ÛYOW‰
\Y˜XİÔXÚØYÚ[™Ñ\ŠI
›Ü›X[^™YXÚØYÙS˜[YJWX—
Š—
—ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^ÛYOW‰
\Y˜XİÔXÚØYÚ[™Ñ\ŠI
›Ü›X[^™YXÚØYÙS˜[YJWX—	
\™Ù]œ˜[Y]ÛÜšÊW
Š—
—ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[˜ÛYOW‰
\Y˜XİÔXÚØYÚ[™Ñ\ŠI
›Ü›X[^™YXÚØYÙS˜[YJW™Y—
Š—
—ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^ÛYOW‰
\Y˜XİÔXÚØYÚ[™Ñ\ŠI
›Ü›X[^™YXÚØYÙS˜[YJW™Y—	
\™Ù]œ˜[Y]ÛÜšÊW
Š—
—ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[˜ÛYOW‰
\Y˜XİÔXÚØYÚ[™Ñ\ŠI
›Ü›X[^™YXÚØYÙS˜[YJW[[YKšœÛÛ—ˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[]Hš[\ÏW
Ôİ[SXœ™UÜ•˜[œÜÜ^[ØY
WˆÏˆ‹Ü•˜[œÜÜ\˜Ú™]]˜[›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙS˜[YO“Xœ™UÔ‹•˜[œÜÜ	
˜[œÜÜXÚØYÙS˜[YTİY™š^
OÔXÚØYÙS˜[YOˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û”™Yš^ŒŒKŒÕ™\œÚ[Û”™Yš^ˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û”İY™š^œ™]šY]ËŒÍOÕ™\œÚ[Û”İY™š^ˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\œÚ[Û‰
™\œÚ[Û”™Yš^
KI
™\œÚ[Û”İY™š^
OÕ™\œÚ[Ûˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙU™\œÚ[Û‰
™\œÚ[ÛŠOÔXÚØYÙU™\œÚ[Ûˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙUYÜÏ›Xœ™]ÜÜ›ÙÜNŞ[[İ[Y\Îİ˜[œÜÜÔXÚØYÙUYÜÏˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙT™XYYQš[O”‘PQQK›YÔXÚØYÙT™XYYQš[Oˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÚ[™ĞÛÛ[[˜ÛYOW”‘PQQK›YˆİX‘›Û\Wœ›ÛİˆÏˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Û™H[˜ÛYOW”‘PQQK›YˆXÚÏWYWˆXÚØYÙT]W—ˆÏˆ‹Ü•˜[œÜÜ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙYÔˆ˜[œÜÜ\ÜÙ[X›Y\È‹Ü•˜[œÜÜ™XYYKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÜUÜ“X[˜YÙYXÚØYÙRY‹Ü•˜[œÜÜ™XYYKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™XZ[HX[˜YÙYÔˆ˜[œÜÜ^[ØY‹Ü•˜[œÜÜ™XYYKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊ›Ú™XİÙÏW“ZXÜ›ÜÛÙ“‘U”Ù×ˆ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊİ]]\O‘^OÓİ]]\Oˆ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\™Ù]œ˜[Y]ÛÜšÏ‰
›ÑÜUÜ”[[YR\›™\ÜÕ\™Ù]œ˜[Y]ÛÜšÊOÕ\™Ù]œ˜[Y]ÛÜšÏˆ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[[YQœ˜[Y]ÛÜšÕ™\œÚ[ÛˆÛÛ™][ÛW‰˜\ÜÎÉ
›ÑÜUÜ”[[YQœ˜[Y]ÛÜšÕ™\œÚ[ÛŠI˜\ÜÎÈOH	˜\ÜÎÉ˜\ÜÎ×‰
›ÑÜUÜ”[[YQœ˜[Y]ÛÜšÕ™\œÚ[ÛŠOÔ[[YQœ˜[Y]ÛÜšÕ™\œÚ[Ûˆ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈÛ[ÚÙU\™Ù]œ˜[Y]ÛÜšÈH›™]LŒ]Ú[™İÜ×È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈXœ™UÜ”XÚØYÙU™\œÚ[ÛˆHŒŒKŒ\™]šY]ËŒÍWÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™È›ÑÜTXÚØYÙU™\œÚ[ÛˆHŒŒKŒ\™]šY]ËŒÍ×È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙRY\È“Xœ™UÔ‹•˜[œÜÜˆÜˆ“Xœ™UÔ‹”›ÑÔW—ˆÈXœ™UÜ”XÚØYÙU™\œÚ[Û—ˆˆ›ÑÜTXÚØYÙU™\œÚ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÜSØØ[ØÚÑš[P\ÜÙ[X›Y\ÏYOĞÛÜSØØ[ØÚÑš[P\ÜÙ[X›Y\Ïˆ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š“ZXÜ›ÜÛÙ“‘U”ÙË•Ú[™İÜÑ\ÚİÜ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”›ÑÔK•Ü‹”ÙÈ‹[[YR\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙÔİÚ]ÚÛ[ÚÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Xœ˜\P\ÜÙ[X›S˜[YHH”›ÑÔK•Ü‹”ÙÔİÚ]ÚXœ˜\Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈİÚ]ÚXœ˜\H\ÜÙ[X›H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™Sİ]][[YP\ÜÙ]Ê\İ]]›ÛİXÚØYÙQ™YY
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\XÚØYÙY›ÑÜQ\™XİÜQ[š\›Û›Y[˜\šXX›HH”“ÑÔWÕÔ—Ô‘TPÒĞQÑQÔ“ÑÔWÑT—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J™\XÚØYÙY›ÑÜQ\™XİÜQ[š\›Û›Y[˜\šXX›JH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØØ[›ÑÜTXÚØYÙT›İ™[˜[˜ÙJˆ™\Ô›ÛİˆXÚØYÙQ™YYˆ™\XÚØYÙY›ÑÜQ\™XİÜJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØØ[XÚØYÙSX]Ú\Ô™\XÚØYÙYÛİ\˜ÙJ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™Q\™XİÜJ™\XÚØYÙY›ÑÜQ\™XİÜK™^Xİ™\XÚØYÙY›ÑÔHXÚØYÙHÛİ\˜ÙWŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\]Qš[TÚLMŠ™\XÚØYÙYÛİ\˜ÙT]
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØØ[ÜXÚØYÙRYHXÚØYÙHX]Ú\È^Xİ™\XÚØYÙYÛİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊˆ\İš[™Ë‘\]X[Ê\ÜÙ[X›S˜[YK”›ÑÔK•Ü—‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØØ[Ü”XÚØYÙSX]Ú\Ğ]˜Z[X›T™\ÜÚ]ÜPZ[ÊÜ”›ÛİXÚØYÙQ™YY
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\ÜÚ]ÜT›ÑÜP\ÜÙ[X›T]
™\Ô›Ûİ\ÜÙ[X›S˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØØ[ÜXÚØYÙRYHXÚØYÙHX]Ú\ÈÙ^XİY\ÜÙ[X›Q\ØÜš\[ÛŸH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\ÜÚ]ÜHÔˆ˜[œÜÜØ\ÜÙ[X›S˜[Y_K™‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Ü™XXÚ
İš[™È\ÜÙ[X›S˜[YH[ˆ›ÑÜT[[YP\ÜÙ[X›Y\ÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]XÚØYÙRY›Ü”[[YP\ÜÙ[X›J\ÜÙ[X›S˜[YJKˆ\ÜÙ[X›S˜[YKˆ›™]LŒŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™Sİ]]\ÜÙ[X›SX]Ú\ÓØØ[XÚØYÙJˆ\İ]]›ÛİˆXÚØYÙQ™YYˆ“Xœ™UÔ‹•˜[œÜÜ‹ˆ\ÜÙ[X›S˜[YKˆ›™]LŒŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈİÚ]Úİ]]Ø\ÜÙ[X›TÚ[\S˜[Y_K™X]Ú\ÈØØ[ÜXÚØYÙRYHXÚØYÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\]Qš[TÚLMŠİ]]]
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™\ÜÙ[X›JØ\İ]]›Ûİš[S˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØY[›X[˜YÙY
İš[™È[›X[˜YÙY˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù][›X[˜YÙYØ[™Y]\Ê[›X[˜YÙY˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[ØYÙÕÚ[™İÚ[™Ô]›Ü›JØYÛÛ^[œ]Ë\İ]]›Ûİ
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™YÚ\İ\”ÙÓ˜]]™T™\ÛÛ™\ŠÛĞ\ÜÙ[X›K\İ]]›Ûİ
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ú[Ë“‘U•Ú[™İÚ[™Ë‘ÛÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛX\”ÜX›PXİ]˜][ÛŠXİ]˜][Û”Ù\šXÙU\JNÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\[YØ]U\™Ù]
—ÜÚİ×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘›\Ú\Ü]Ú\“Ü\˜][ÛœÊÚ[™İË\XØ][Û’YWŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—˜\Y˜Xİ×‹‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”™[X\ÙW‹‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—“Xœ™UÔ‹•˜[œÜÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—“Xœ™UÔ‹•˜[œÜÜ‘XY×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—•RP]]ÛX][Û•\\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”Ş\İ[K•Ú[™İÜË”š[Z]]™\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”™\Ù[][Û‘œ˜[Y]ÛÜšË‘›Y[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”™\Ù[][Û‘œ˜[Y]ÛÜšËY\›Ì—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ›Ü™XXÚ
İš[™È[YP\ÜÙ[X›H[ˆÜ•[YP\ÜÙ[X›Y\ÊBˆÂˆ\ÜÙ\ÛÛZ[œÊ	—İ[YP\ÜÙ[X›_Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ	—İ[YP\ÜÙ[X›_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆBˆ\ÜÙ\ÛÛZ[œÊ	—ÜšX˜›Û\ÜÙ[X›_Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ	—ÜšX˜›Û\ÜÙ[X›_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”›ÑÔKÛÛ\]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”›ÑÔK‘\™Xİˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”›ÑÔK•Ü‹’[\›Üˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”›ÑÔK•˜[œÜ[\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”Ú[Ë“‘U•Ú[™İÚ[™ËÛÛ[[Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”Ú[Ë“‘U•ÙX‘ÔWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—“Ü[‘›ÛÚ\œˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]˜]]™P\ÜÙ]Ø[™Y]\ÊÙÜWŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]˜]]™P\ÜÙ]Ø[™Y]\Ê™Û×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™P[Qš[H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—œ›ÙÜK]Ü‹\ÙË\Û[ÚÙW‹œ›ÙÜWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙU›ÚY
\’[š]X[^™PÛÛ\Û™[ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”[”ÙÔÜX›P›Ûİİ˜\Û[ÚÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙË”›ÑÜUÜ”ÙÔÜX›P›Ûİİ˜\‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”[[YR[\œË”[“[Ù[PÛÛœİXİÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜX›H›Ûİİ˜\Xİ]˜][Ûˆ[˜X›Y‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜX›H›Ûİİ˜\Y\ÜØYÙP›Ş[˜X›Y‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜX›H›Ûİİ˜\š[HX[ÙÈ[˜X›Y‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜX›H›Ûİİ˜\ØYY›ÑÔK•Üˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›TŞ\İ[PÛÛ[X[™Ê™\Ù[][Û‘œ˜[Y]ÛÜšËÚ[™İÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”Ş\İ[PÛÛ[X[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÑÈŞ\İ[PÛÛ[X[™È™\İÜ™Hİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊ›Ú™XİÙÏW“ZXÜ›ÜÛÙ“‘U”Ù×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\™Ù]œ˜[Y]ÛÜšÏ‰
›ÑÜUÜ”[[YR\›™\ÜÕ\™Ù]œ˜[Y]ÛÜšÊOÕ\™Ù]œ˜[Y]ÛÜšÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[[YQœ˜[Y]ÛÜšÕ™\œÚ[ÛˆÛÛ™][ÛW‰˜\ÜÎÉ
›ÑÜUÜ”[[YQœ˜[Y]ÛÜšÕ™\œÚ[ÛŠI˜\ÜÎÈOH	˜\ÜÎÉ˜\ÜÎ×‰
›ÑÜUÜ”[[YQœ˜[Y]ÛÜšÕ™\œÚ[ÛŠOÔ[[YQœ˜[Y]ÛÜšÕ™\œÚ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠŒLKŒŒ\™]šY]ËŒŒŒLŒLLH‹^\›˜[ÙÒ\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”›ÑÔK•Ü‹”ÙÈ‹^\›˜[ÙÒ\›™\ÜÔ›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]‘Ù][\]

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙÑ^\›˜[Û[ÚÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈÜšYÚ[˜[Ü”ÙÈH“ZXÜ›ÜÛÙ“‘U”Ù×È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈÜšYÚ[˜[Ú[™İÜÑ\ÚİÜÜ”ÙÈH“ZXÜ›ÜÛÙ“‘U”ÙË•Ú[™İÜÑ\ÚİÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÙĞ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\İ]]\ÜÙ[X›S˜[YHH‘^\›˜[ÙÔÚ[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÙÓXœ˜\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Xœ˜\Sİ]]\ÜÙ[X›S˜[YHH‘^\›˜[ÙĞÛÛ›Û×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÙ[˜[XÚØYÙSX[˜YÙ[Y[\ÜÙ[X›S˜[YHH‘^\›˜[ÜTÙĞ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÙ[˜[XÚØYÙSX[˜YÙ[Y[İ]]\ÜÙ[X›S˜[YHH‘^\›˜[ÜTÙÔÚ[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\\™Q^\›˜[Ù[˜[XÚØYÙSX[˜YÙ[Y[\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[Ù[˜[XÚØYÙSX[˜YÙ[Y[›Ú™XİÚ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊX[˜YÙTXÚØYÙU™\œÚ[ÛœĞÙ[˜[OYOÓX[˜YÙTXÚØYÙU™\œÚ[ÛœĞÙ[˜[Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙU™\œÚ[Ûˆ[˜ÛYOW”Ş\İ[K”™XXİ]™Wˆ™\œÚ[ÛW‹ŒŒWˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈÙÕ™\œÚ[ÛˆHŒŒKŒ\™]šY]ËŒÍWÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™È›ÑÜTXÚØYÙU™\œÚ[ÛˆHŒŒKŒ\™]šY]ËŒÍ×È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙRY\È“Xœ™UÔ‹”Ù×ˆÜˆ“Xœ™UÔ‹•˜[œÜÜˆÜˆ“Xœ™UÔ‹”›ÑÔW—ˆÈÙÕ™\œÚ[Û—ˆˆ›ÑÜTXÚØYÙU™\œÚ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙT™Y™\™[˜ÙH[˜ÛYOW”Ş\İ[K”™XXİ]™WˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\İÜ™W‹Ù[˜[XÚØYÙSX[˜YÙ[Y[›Ú™Xİ]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Z[‹Ù[˜[XÚØYÙSX[˜YÙ[Y[›Ú™Xİ]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Ù\Ó›İÛÛZ[ŠÙ[˜[XÚØYÙ\Ë“Xœ™UÔ‹•˜[œÜÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Ù\Ó›İÛÛZ[ŠÙ[˜[XÚØYÙ\Ë”›ÑÔK‘\™Xİˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\P\ÜÙ[X›S˜[YHH‘^\›˜[ÙÑY˜][][\ÓXœ˜\Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Ú™XİÙÏWÓÜšYÚ[˜[Ü”ÙßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Ú™XİÙÏWÓÜšYÚ[˜[Ú[™İÜÑ\ÚİÜÜ”ÙßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ÜšYÚ[˜[Ú[™İÜÑ\ÚİÜÜ”ÙËˆ™^\›˜[ÑÈ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İÚ]ÚÜ”ÙÓÛ›H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆİÚ]ÚÜ”ÙÓÛ›J›Ü›X[Ü”›Ú™XİÜšYÚ[˜[Ü”ÙË\ØÜš\[ÛŠNÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİš[™ÈÜšYÚ[˜[ÙÈH	›Ú™XİÙÏWÛÜšYÚ[˜[ÙÓ˜[Y_W—È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİš[™È›ÑÜTÙÈH	›Ú™XİÙÏW“Xœ™UÔ‹”ÙËŞÔÙÕ™\œÚ[ÛŸW—È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ú[™ÙY[Ü™H[ˆ]È›ÛİÑÈ\š[™È›ÑÔHÑÈİÚ]Ú[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊİ]]\O•Ú[‘^OÓİ]]\Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™È^\›˜[\\™Ù]œ˜[Y]ÛÜšÈH›™]LŒ]Ú[™İÜ×È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\™Ù]œ˜[Y]ÛÜšÏÑ^\›˜[\\™Ù]œ˜[Y]ÛÜšßOÕ\™Ù]œ˜[Y]ÛÜšÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ[X›S˜[YOĞ\İ]]\ÜÙ[X›S˜[Y_OĞ\ÜÙ[X›S˜[YOˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ[X›S˜[YOÓXœ˜\Sİ]]\ÜÙ[X›S˜[Y_OĞ\ÜÙ[X›S˜[YOˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\™Ù]œ˜[Y]ÛÜšÜÏÑ^\›˜[\\™Ù]œ˜[Y]ÛÜšßOÕ\™Ù]œ˜[Y]ÛÜšÜÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[Xœ˜\HÚ[™İÜÈ\™Ù]œ˜[Y]ÛÜšÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[˜X›QY˜][][\Ï™˜[ÙOÑ[˜X›QY˜][][\Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOWŠŠ‹Ê‹˜Ü×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‘Yš[š][Ûˆ[˜ÛYOW\[[ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYÙH[˜ÛYOWŠŠ‹Ê‹[[ˆ^ÛYOW\[[ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Û™H[˜ÛYOW\˜ÛÛ™šY×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙT™Y™\™[˜ÙH[˜ÛYOW‘^[™Y•Ü‹•ÛÛÚ]ˆ™\œÚ[ÛWKŒKŒ—ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙT™Y™\™[˜ÙH[˜ÛYOW”›ÑÔK”Ş\İ[K‘˜]Ú[™ËÛÛ[[Û—ˆ™\œÚ[ÛWÔ›ÑÜTXÚØYÙU™\œÚ[ÛŸWˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›TŞ\İ[Q˜]Ú[™ĞÛÛ\[\”™Y™\™[˜Ù\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—Ñ^\›˜[›ÑÜTŞ\İ[Q˜]Ú[™Ô™Y™\™[˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—Ñ^\›˜[™]ÛÜ™TŞ\İ[Q˜]Ú[™Ñ˜XØYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•H^\›˜[ÑÈÛ[ÚÙH™]Z[™YHZXÜ›ÜÛÙ“‘UÛÜ™K\”™YˆŞ\İ[K‘˜]Ú[™È˜XØYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›Q˜]Ú[™ĞÛÛ˜Xİ˜ÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™È˜\ˆš]X\H™]Èš]X\
KJNÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOW‘^\›˜[[™[[[˜Ü×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOW‘^\›˜[[YYÛÛ›Û˜Ü×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOW”›Ü\Y\ËĞ\ÜÙ[X›R[™›Ë˜Ü×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYÙH[˜ÛYOW‘^\›˜[[™[[[ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYÙH[˜ÛYOW•[Y\ËÑÙ[™\šXË[[ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[Xœ˜\H^XÚ]][H[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÙUÔYOÕ\ÙUÔˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\İ]]\ÜÙ[X›S˜[YH
È‹™ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]\Üİš[S˜[YJ\İ]]\ÜÙ[X›S˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—™^\›˜[ÑÈ\Üİˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”[\Üİ]™U˜[Y][Û”›Ø™J‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”“ÑÔWÕÔ—ÑVT“SÓU‘WÕSQUWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—‘^\›˜[ÑÈ\Üİ]™H[œ]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”“ÑÔWÕÔ—ÑVT“SÑQUSÓU‘WÑÑSÓQU–WÕSQUWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÜUÜ‘XYÛ›ÜİXÜË•QÙ]™[™\”İ\™˜XÙQÙ[ÛY]J\Ëİ]˜\ˆÙ[ÛY]JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—‘^\›˜[ÑÈY˜][Z][H\Üİ]™HÙ[ÛY]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ÜÙ[X›OQ^\›˜[ÙĞÛÛ›ÛÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ÜÙ[X›OQ^\›˜[ÙÑY˜][][\ÓXœ˜\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œÎİÏWš‹ËÜØÚ[X\ËÙYY˜ÛÛKİÜ‹Ş[[İÛÛÚ]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œÎØYWš‹ËÜØÚ[X\ËÙYY˜ÛÛKİÜ‹Ş[[Ø]˜[Û™ØÚ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]Ø]\›X\šÕ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ][YÙ\•\İÛˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]ÛÛÜ”XÚÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]Ø[İ[]Ü•\İÛˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]\ŞR[™XØ]Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]›Ü\QÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]›ÜİÛ]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]Ü]]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]šXÚ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]][S[™U^Y]Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]]Û”Ü[›™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]Ú^˜\™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]Ú[™İĞÛÛZ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛÚ]Ú[™İĞÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYY\ŞR[™XØ]ÜˆÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYY›Ü\QÜšYÙ[XİYØš™Xİš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYYšXÚ^›ŞÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYY][S[™U^Y]ÜˆÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYY]Û”Ü[›™\ˆ™\İÜ™YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYYÚ^˜\™š[š\Úİ]\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYYÚ[™İĞÛÛ›Û^Ûİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYYÚ[™İĞÛÛ›ÛÛÜÙH]ÛˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[ÛÛÚ]›ÜİÛ]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[ÛÛÚ]Ü]]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[ÛÛÚ]]Û”Ü[›™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[ÛÛÚ]Ú^˜\™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[ÛÛÚ]Ú[™İĞÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYY›ÜİÛ]Ûˆ›ÜİÛˆÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙYYÜ]]Ûˆ›ÜİÛˆÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØÚÓX[˜YÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØYY\›Õ[YHÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]VÙYYÛÛÚ][™]˜[Û‘ØÚÊÚ[™İË^XİØYYˆ˜[ÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]VÙYYÛÛÚ][™]˜[Û‘ØÚÊÚ[™İË^XİØYYˆYJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™Qš[J]ÛÛXš[™Jİ]]›Ûİ–ÙYY•Ü‹•ÛÛÚ]™ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\ÛÛZ[œÊ\ÒœÛÛ‹‘^[™Y•Ü‹•ÛÛÚ]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Xœ˜\Sİ]]\ÜÙ[X›S˜[YH
È‹™ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\P\ÜÙ[X›S˜[YH
È‹™ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Ú™Xİ™Y™\™[˜ÙH[˜ÛYOW‹‹‹ŞÓXœ˜\P\ÜÙ[X›S˜[Y_KŞÓXœ˜\P\ÜÙ[X›S˜[Y_K˜ÜÜ›Ú—ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Ú™Xİ™Y™\™[˜ÙH[˜ÛYOW‹‹‹ŞÑY˜][][\ÓXœ˜\P\ÜÙ[X›S˜[Y_KŞÑY˜][][\ÓXœ˜\P\ÜÙ[X›S˜[Y_K˜ÜÜ›Ú—ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\T[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\TYÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\U[YYÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\U[YU^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\T™\Ûİ\˜Ù\Ë[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‹Ñ^\›˜[ÙÑY˜][][\ÓXœ˜\NØÛÛ\Û™[Ô™\Ûİ\˜Ù\ËÑY˜][][\ÓXœ˜\T™\Ûİ\˜Ù\Ë[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓXœ˜\T™\Ûİ\˜ÙPœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\Ûİ\˜Ù\ËÑY˜][][\Ğ\™\Ûİ\˜Ù\Ë[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ğ\Xİ[Û˜\U^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ğ\Xİ[Û˜\Pœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[˜[ZXÔ™\Ûİ\˜ÙHœ\Ú[˜[Y][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H\[˜[ZXÔ™\Ûİ\˜ÙHœ\Ú[˜[Y][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñœ™Y^˜X›Pœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[œÚ\™Yœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][Hœ™Y^˜X›Hœ\ÚY]Y]Kˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][Hœ™Y^˜X›Hœ\Úœ›Ş™[ˆİ]Kˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][Hœ™Y^˜X›Hœ\ÚÛÛ™H]]Xš[]Kˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][H”Ú\™YY˜[ÙH™\Ûİ\˜ÙHÛÚİ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑØÚÔ[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞØ[˜\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[šY›Ü›QÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜšY]XÚY›İÈ[™ÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HØÚÔ[™[]XÚYØÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HØ[˜\È]XÚYÜÚ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[šY›Ü›QÜšYY]Y]H[™Ú[™[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘Y˜][][\Õ˜[œÙ›Ü›YY^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[™\•˜[œÙ›Ü›SÜšYÚ[WŒKWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[œÙ›Ü›QÜ›İ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØØ[U˜[œÙ›Ü›HØØ[VWŒKŒWˆØØ[VOWŒÍWˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›İ]U˜[œÙ›Ü›H[™ÛOWŒMWˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[œÛ]U˜[œÙ›Ü›HWŒ×ˆOWˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚÙ]Õ˜[œÙ›Ü›H[™ÛVWWˆ[™ÛVOWŒˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][H˜[œÙ›Ü›H\™Ù]Y]Y]Kˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][H™[™\•˜[œÙ›Ü›HÜ›İ\Ú[Ûİ[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H^[İ]˜[œÙ›Ü›HÚÙ]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][H˜[œÙ›Ü›H˜[Y\Ëˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[\]R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]U[\]H]U\OWŞ•\HØØ[‘Y˜][][\Ò][_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ò[\XÚ][\]U^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[\XÚ]]U[\]Hš\İX[™YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[\XÚ]]U[\]Hš[™[™È^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó›ÙU[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊY\˜\˜ÚXØ[]U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’][\ÔÛİ\˜ÙOWĞš[™[™ÈÚ[™[ŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ™YUšY]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó›ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HY\˜\˜ÚXØ[]U[\]Hš[™[™È^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÙ[™\˜]Y™YUšY]È›ÛİÛÛZ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H™YUšY]ÈÚ[][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓØš™Xİ]T›İšY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô›İšY\”Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓØš™Xİ›İšY\•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HØš™Xİ]T›İšY\ˆ›İ[™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ö[]T›İšY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–]Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ö[›İšY\•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[]T›İšY\ˆ›İ[™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ğ›İ[™İ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ\™Ù]\]Yİ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“›İYSÛ•\™Ù]\]YUYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™Ë•\™Ù]\]YW“Û‘Y˜][][\Ğš[™[™Õ\™Ù]\]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑY]X›Tİ]\Õ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÛİ\˜ÙU\]Yİ]\Õ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“›İYSÛ”Ûİ\˜ÙU\]YUYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™Ë”Ûİ\˜ÙU\]YW“Û‘Y˜][][\Ğš[™[™ÔÛİ\˜ÙU\]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ˜[Y]Yİ]\Õ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô™\]Z\™Y^[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™Ë•˜[Y][Û”[\Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\]TÛİ\˜ÙUšYÙÙ\T›Ü\PÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ›Ü›X]Yİ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ˜[˜XÚÔİ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ\™Ù][İ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜[˜XÚÕ˜[YOQY˜][][H˜[˜XÚÈ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\™Ù][˜[YOQY˜][][H\™Ù][[˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][Hš[™[™È\™Ù][˜[YHÈÛX\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ôš[Üš]Tİ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[Üš]Pš[™[™Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][Hš[Üš]Pš[™[™È˜[˜XÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÙ[š[™[™Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ğ[˜Ù\İÜš[™[™Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[]]™TÛİ\˜ÙHÙ[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[]]™TÛİ\˜ÙH[˜Ù\İÜ•\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H™[]]™TÛİ\˜ÙH[˜Ù\İÜ•\Hš[™[™È™Yœ™\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ò][S˜[YPÛÛ™\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ôİ]\ÔÙ[Xİ[ÛÛÛ™\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó˜[YU[\]TÙ[XİÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ™\YÙ[Xİ[Û•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÙ[XİY[\]PÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÙ[XİY[U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÙ[XİY™]U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó][Pš[™[™Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“][Pš[™[™ÈÛÛ™\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ôİ]\ÕšYÙÙ\”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]UšYÙÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÕšYÙÙ\™Yİ]\Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô›Ü\UšYÙÙ\”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›Ü\OW’\Ñ[˜X›Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô›Ü\UšYÙÙ\™Y^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ğ˜\ÙU^İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ğ˜\ÙYÛ•^İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ÙYÛWÔİ]XÔ™\Ûİ\˜ÙHY˜][][\Ğ˜\ÙU^İ[_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H˜\ÙYÛˆİ[H˜\ÙHÙ]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[\]Y]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ›Û[\]H\™Ù]\OWŞ•\H]ÛŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[\]P]Û”›Ûİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[\]P]ÛÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Õ[\]Y]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó\İ›Ş][\Ô[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó\İ›Ş][Tİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H][\Ô[™[[\]HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÙ[™\˜]Y\İ›Ş][HÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÛÜY][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[Û•šY]ÔÛİ\˜ÙK”ÛÜ\ØÜš\[ÛœÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\Û™[[Ù[”ÛÜ\ØÜš\[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÛÜY\İ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\ÔŞ[˜Ú›Ûš^™YÚ]İ\œ™[][OW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛXİ[Û•šY]ÔÛİ\˜ÙHÛÜYÜ™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛXİ[Û•šY]ÔÛİ\˜ÙHİ\œ™[][Hœ›ÛHÙ[Xİ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÜY\İ›ŞÛÛXİ[Ûˆ\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó\İšY]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜšYšY]ĞÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü^SY[X™\š[™[™ÏWĞš[™[™È˜[Y_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü^SY[X™\š[™[™ÏWĞš[™[™ÈÚ[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜšYšY]È˜[YHÛÛ[[ˆš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H\İšY]ÈÛÛXİ[Ûˆ\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ôİ]\ÓX™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\™Ù]WĞš[™[™È[[Y[˜[YOQY˜][][\ÑY]X›Tİ]\Õ^›ŞWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HX™[\™Ù]Y]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑÜ›İ\›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ›İ\›ŞÛÛ[š[™[™ÈÈØœÙ\™HS›İYT›Ü\PÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔØÜ›ÛšY]Ù\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØ[ÛÛ[ØÜ›ÛW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HØÜ›ÛšY]Ù\ˆY]Y]H[™ÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ]ËÑY˜][][\Ò[XYÙKœ™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ò[XYÙPœ\Ú™Xİ[™ÛH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[XYÙPœ\Ú[XYÙTÛİ\˜ÙOWœXÚÎ‹ËØ\XØ][Û‹Ğ\ÜÙ]ËÑY˜][][\Ò[XYÙKœ™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HSS™\Ûİ\˜ÙH[XYÙH^[È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[XYÙPœ\Ú[XYÙH^[È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ]ËÑY˜][][\Ğİ\œÛÜ‹˜İ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊİ\œÛÜW\ÜÙ]ËÑY˜][][\Ğİ\œÛÜ‹˜İ\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HSSİ\œÛÜˆ™\Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][Hİ\œÛÜˆXÚÈ™\Ûİ\˜ÙHİ™X[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔšXÚ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][H\İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÜTšXÚ^]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™W\XØ][ÛÛÛ[X[™ËÛÜWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HšXÚ^›ŞÙÙÛP›ÛÛÛ[X[™]˜Z[Xš[]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HšXÚ^›ŞÛÜYYÛ\›Ø\™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HšXÚ^›Ş\İYÛ\›Ø\™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ›İÑØİ[Y[ØÜ›ÛšY]Ù\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HšXÚ^›Ş^˜[™ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H›İÑØİ[Y[ØÜ›ÛšY]Ù\ˆ^˜[™ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÜ[ÚXÚÕ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ü[ÚXÚË’\Ñ[˜X›YW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ[ÚXÚÈY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ[ÚXÚÈİ\İÛHXİ[Û˜\HY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ[ÚXÚÈ›Ë[ÜÜ[[™È\œ›Üˆ]Y\šY\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÚXÚĞ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\ĞÚXÚÙYWĞš[™[™È\Ñ›Ü›SÜ[Û‘[˜X›Y[ÙOUÛÕØ^_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÚXÚĞ›ŞÛË]Ø^Hš[™[™ÈÈ\]HÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô˜Y[Ô[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ü›İ\˜[YOW‘Y˜][][\Ó[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H˜Y[Ğ]ÛˆÜ›İ\^Û\Ú]š]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÛY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[YOWĞš[™[™È›Ü›T›ÙÜ™\ÜË[ÙOUÛÕØ^K\]TÛİ\˜ÙUšYÙÙ\T›Ü\PÚ[™ÙYWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô›ÙÜ™\ÜĞ˜\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H˜[™ÙHÛÛ›ÛÈÈØœÙ\™HÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ô\ÜİÛÜ™›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”\ÜİÛÜ™Ú[™ÙYW“Û‘Y˜][][\Ô\ÜİÛÜ™Ú[™ÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H\ÜİÛÜ™›ŞÚ[™ÙY[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞØ[[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[Xİ[Û“[ÙOW”Ú[™ÛQ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ]TXÚÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY]OWĞš[™[™ÈÙ[XİY]K[ÙOUÛÕØ^_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]HÛÛ›ÛÈÈØœÙ\™HÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÜ\İÛ™\]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ôİ[™[Û™TÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”XÙ[Y[\™Ù]WĞš[™[™È[[Y[˜[YOQY˜][][\ÔÜ\İÛ™\]ÛŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ\ÈÜ[ˆ›İYÚÜX›HÜ\Ù\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ\ÈÛÜÙH›İYÚÜX›HÜ\Ù\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÓY[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ[X[™Y[R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HY[R][HÛXÚÈ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ^Y[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ^ÛÛ[X[™Y[R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ^Y[H[˜ÚXÚÙY[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÕÛÛ˜\•˜^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÕÛÛ˜\ÛÛ[X[™]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ˜\ˆÛÛ[X[™Y]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ôİ]\Ğ˜\•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][Hİ]\Ğ˜\ˆš[™[™ÈÈØœÙ\™HS›İYT›Ü\PÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÕXÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY[™^WŒWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊX’][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ]Z[ÕX•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÙ[XİYX’][Hš[™[™ÈÈØœÙ\™HS›İYT›Ü\PÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ^[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^[™YW“Û‘Y˜][][\Ñ^[™\‘^[™Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H^[™\ˆÛÛ\ÙY]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ]QÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]]ÑÙ[™\˜]PÛÛ[[œÏW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]QÜšY^ÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]QÜšYÚXÚĞ›ŞÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÏWĞš[™[™È\ĞXİ]™_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]QÜšYXİ]™HÛÛ[[ˆš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]QÜšYÛÛXİ[Ûˆ\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó\™ÙQ]QÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’][\ÔÛİ\˜ÙOWĞš[™[™È\™ÙR][\ßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY][OWĞš[™[™ÈÙ[XİY\™ÙR][K[ÙOUÛÕØ^_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[˜X›T›İÕš\X[^˜][ÛW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\X[^š[™Ô[™[•š\X[^˜][Û“[ÙOW”™XŞXÛ[™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\X[^š[™Ô[™[”ØÜ›Û[š]W”^[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó\™ÙR][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]S\™ÙR][\Ê
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊŒLÌ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H\™ÙH]QÜšY][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H\™ÙH]QÜšY™\XØ[Ù™œÙ]Y\ˆ\™ÙHØÜ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]QY˜][][\Ó\™ÙQ]QÜšY™X[^™Y›İÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑÜ›İ\XY\•[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑÜ›İ\Y][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Ü\QÜ›İ\\ØÜš\[Ûˆ›Ü\S˜[YOW’Ú[™ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÑÜ›İ\Y\İ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ü›İ\İ[HXY\•[\]OWÔİ]XÔ™\Ûİ\˜ÙHY˜][][\ÑÜ›İ\XY\•[\]_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛXİ[Û•šY]ÔÛİ\˜ÙH[š]X[Ü›İ\][HÛİ[È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÜ›İ\İ[HXY\ˆš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛXİ[Û•šY]ÔÛİ\˜ÙHÛÛXİ[Û‹XÚ[™ÙHÜ›İ\][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ\ÜÚ]S\İ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\ÜÚ]PÛÛXİ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[ÛÛÛZ[™\ˆÛÛXİ[ÛWŞ”İ]XÈØØ[‘Y˜][][\ĞÛÛ\ÜÚ]T›İšY\‹’][\ßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ\ÜÚ]T›İšY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ\ÜÚ]PÛÛXİ[Ûˆ[š]X[›][™Y][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ\ÜÚ]PÛÛXİ[ÛˆÛÛXİ[Û‹XÚ[™ÙH\[™YÛİ\˜ÙH][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ó\İ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛX›Ğ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY][OWĞš[™[™ÈÙ[XİY][K[ÙOUÛÕØ^_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÕšY]Ó[Ù[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØœÙ\˜X›PÛÛXİ[ÛY˜][][\Ò][Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈY˜][][\Ò][HÙ[XİY][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][Hš[™[™È\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][Hš[™[™Ë•\™Ù]\]YÈš\™HY\ˆÛİ\˜ÙH›İYšXØ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][Hš[™[™Ë”Ûİ\˜ÙU\]YÈš\™HY\ˆ^XÚ]Ûİ\˜ÙH˜[œÙ™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H^›ŞÛË]Ø^Hš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][H^›ŞÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H˜[Y][Ûˆ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][H˜[Y]YÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]UšYÙÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][HšYÙÙ\ˆ™\Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H›Ü\HšYÙÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ›Û[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H[\]H\ØX›Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ™\\ˆš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]U[\]TÙ[XİÜˆ[H[\]Hš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]U[\]TÙ[XİÜˆ™]H[\]Hš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H][Pš[™[™ÈÛÛ™\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛXİ[Ûˆš[™[™È][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÙ[XİÜˆÛË]Ø^Hš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ú[™İËÛÛ[X[™š[™[™ÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ú[™İË’[œ]š[™[™ÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ[X[™]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\Ñ]™[Ù]\]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]™[Ù]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘Y˜][][\Ñ]™[Ù]\ÛXÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H]™[Ù]\ˆ›İ]Y[™\ˆ^Xİ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›İ]YRPÛÛ[X[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ĞÛÛ[X[™Ø[‘^Xİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H›İ]YÛÛ[X[™^Xİ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔÙÔÙ][™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][HÑÈ\ÛÛ™šYÈ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][\ÔYÙK[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\ÜÏW‘^\›˜[ÙÑY˜][][\Ğ\‘Y˜][][\ÔYÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘Y˜][][\Ñœ˜[YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ûİ\˜ÙOW‘Y˜][][\ÔYÙK[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][HÛÛ\[YYÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][HÛÛ\[YYÙHœ˜[YHÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹“ØYÛÛ\Û™[
‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‹Ñ^\›˜[ÙÑY˜][][\Ğ\ØÛÛ\Û™[ÑY˜][][\Ô[™[[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H\XØ][Û‹“ØYÛÛ\Û™[[[Y[˜[YHš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‹Ñ^\›˜[ÙÑY˜][][\ÓXœ˜\NØÛÛ\Û™[ÑY˜][][\ÓXœ˜\T[™[[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‹Ñ^\›˜[ÙÑY˜][][\ÓXœ˜\NØÛÛ\Û™[ÑY˜][][\ÓXœ˜\TYÙK[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H™Y™\™[˜ÙYXœ˜\H\XØ][Û‹“ØYÛÛ\Û™[[[Y[˜[YHš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Y˜][Z][H™Y™\™[˜ÙYXœ˜\HÛÛ\[YYÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[Y˜][Z][H\^XÚ]\ÛÛ™šYÈ][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][H\ÛÛ™šYÈİ]]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][H™Y™\™[˜ÙYXœ˜\HØ\[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][HXœ˜\H[YH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][][HXœ˜\H™\Ûİ\˜ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][HXœ˜\H[YR[™›ÈÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][HXœ˜\HY˜][İ[HÙ^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][HXœ˜\HÙ[™\šXË[[Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][HXœ˜\HÛÛ\Û™[™\Ûİ\˜ÙHXİ[Û˜\HÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[Y˜][Z][HXœ˜\HY˜][][HÜ[İ]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[Y˜][Z][HXœ˜\H^XÚ]ÛÛ\[H][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[Y˜][Z][HXœ˜\H^XÚ]YÙH][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][H™Y™\™[˜ÙYXœ˜\H\ÜÙ[X›H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][H™Y™\™[˜ÙYXœ˜\H\[™[˜ŞH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\Ûİ\˜ÙH[˜ÛYOW\ÜÙ]ËÑ^\›˜[™\Ûİ\˜ÙKˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\Ûİ\˜ÙH[˜ÛYOW\ÜÙ]ËÑ^\›˜[[XYÙKœ™×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[[˜ÛYOW\ÜÙ]ËÑ^\›˜[ÛÛ[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÜUÓİ]]\™XİÜO”™\Ù\™S™]Ù\İĞÛÜUÓİ]]\™XİÜOˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\™Ù]]\ÜÙ]ËÑ^\›˜[ÛÛ[Õ\™Ù]]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ\ÜÙ]×‹‘^\›˜[™\Ûİ\˜ÙKŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ\ÜÙ]×‹‘^\›˜[[XYÙKœ™×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ\ÜÙ]×‹‘^\›˜[ÛÛ[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜYYÛÛ[İ]]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ\˜ÛÛ™šY×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ™šYİ\˜][Û“X[˜YÙ\‹\Ù][™ÜÖ×‘^\›˜[ÙĞ\Ù][™×—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\İ]]\ÜÙ[X›S˜[YH
È‹™˜ÛÛ™šY×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•[YR[™›Ê™\Ûİ\˜ÙQXİ[Û˜\SØØ][Û‹“›Û™K™\Ûİ\˜ÙQXİ[Û˜\SØØ][Û‹”Ûİ\˜ÙP\ÜÙ[X›JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[YYÛÛ›ÛˆÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][İ[RÙ^T›Ü\K“İ™\œšYSY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™JXœ˜\T›Ûİ•[Y\×‹‘Ù[™\šXË[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\Û™[™\Ûİ\˜ÙRÙ^H\R[•\™Ù]\ÜÙ[X›O^Ş•\HØØ[‘^\›˜[[YYÛÛ›ÛH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[YP›Ü™\œ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”“ÑÔWÕÔ—ÑVT“SÕSQUH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”“ÑÔWÕÔ—ÑVT“SÔ•S—ÕSQUH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—™^\›˜[\İ\\X[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—™^\›˜[İ\\™]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ\\\™İ[Y[ÈHK\™ÜÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›Ü\Y\Ö×‘^\›˜[İ\\\™İ[Y[Ûİ[—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›Ü\Y\Ö×‘^\›˜[İ\\š\œİ\™İ[Y[—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›Ü\Y\Ö×‘^\›˜[İ\\İ]W—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Ûˆİ\\š\œİ\™İ[Y[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Ûˆİ\\ÙXÛÛ™\™İ[Y[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Ûˆİ\\İ]H›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈ\XØ][Û‹”[ˆ˜[Y][ÛˆİXØÙYYYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\Üİ\XØ][Û‹”[ˆ˜[Y][Ûˆİ]]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈ\Üİ]™H[œ]˜[Y][ÛˆİXØÙYYYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[]S[İ\ÙSY]Û‘İÛˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”˜Z\ÙRÜİ[œ]
]™RÜİ“[İ\ÙQİÛ—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”˜Z\ÙRÜİ[œ]
]™RÜİ•^[œ]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›]™U˜[Y][Û•^›Ş‘Ù]š[™[™Ñ^™\ÜÚ[ÛŠ^›Ş•^›Ü\JOË•\]TÛİ\˜ÙJ
NÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”˜Z\ÙRÜİ[œ]
]™RÜİ’Ù^QİÛ—‹Ù^Nˆ‘W‹[ÙYšY\œÎˆÛÛ›ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™Hİ›
ÑHÙ^Pš[™[™È^Xİ][ÛˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈY˜][Z][H\Üİ]™HÙ[ÛY]H˜[Y][ÛˆİXØÙYYYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYY˜][Z][H]™H›ÑÔHÔˆšY]ÜÜÈ\ÙHH[\ÚXØ[\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\ÛÛZ[œÊØÕ^‘^\›˜[ØØ[^˜][Û”›Ûİ‹‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\ÛÛZ[œÊØÕ^‘^\›˜[ØØ[^˜][Û•^‹‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TŞ\İ[PÛÛ[X[™ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[PÛÛ[X[™Ë“X^[Z^™UÚ[™İÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈŞ\İ[PÛÛ[X[™ÈÚİÈŞ\İ[HY[H›Ë[Üİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œÎœÚ[W˜Û‹[˜[Y\ÜXÙN”Ş\İ[K•Ú[™İÜË”Ú[Ø\ÜÙ[X›OT™\Ù[][Û‘œ˜[Y]ÛÜš×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œÎÜW˜Û‹[˜[Y\ÜXÙN”Ş\İ[K•Ú[™İÜÎØ\ÜÙ[X›OT™\Ù[][Û‘œ˜[Y]ÛÜš×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œÎœšX˜›ÛW˜Û‹[˜[Y\ÜXÙN”Ş\İ[K•Ú[™İÜËÛÛ›ÛË”šX˜›ÛØ\ÜÙ[X›OTŞ\İ[K•Ú[™İÜËÛÛ›ÛË”šX˜›Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšX˜›Û”šX˜›Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\ÚXš[]OWÛÛ\ÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[šX˜›Û]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TšX˜›ÛÛÛ›ÛÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšX˜›Ûˆ›İ]YÛÛ[X[™Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[•Ú[™İĞÚ›ÛYK•Ú[™İĞÚ›ÛYOˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÚ[•Ú[™İĞÚ›ÛYK’\Ò]\İš\ÚX›R[Ú›ÛYOW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÚ[•Ú[™İĞÚ›ÛYK”™\Ú^™QÜš\\™Xİ[ÛW›İÛTšYÚˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WŞ”İ]XÈÜ”Ş\İ[PÛÛ[X[™Ë“X^[Z^™UÚ[™İĞÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WŞ”İ]XÈÜ”Ş\İ[PÛÛ[X[™Ë“Z[š[Z^™UÚ[™İĞÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WŞ”İ]XÈÜ”Ş\İ[PÛÛ[X[™Ë”™\İÜ™UÚ[™İĞÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WŞ”İ]XÈÜ”Ş\İ[PÛÛ[X[™Ë”ÚİÔŞ\İ[SY[PÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[Ş\İ[PÛÛ[X[™^Xİ]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TŞ\İ[PÛÛ[X[™]ÛŠ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈSSÚ[™İĞÚ›ÛYH]XÚY˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈSSŞ\İ[PÛÛ[X[™ÈX^[Z^™H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İ\\W“Û‘^\›˜[\İ\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^]W“Û‘^\›˜[\^]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ\\™\Ûİ\˜ÙU^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü]Ú\”š[Üš]K“›Ü›X[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û”[[™Ú]İÛˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û‘^]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü]Ú\”š[Üš]K”™[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÜÚ[™ÏW“Û‘^\›˜[Ú[™İĞÛÜÚ[™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÜÙYW“Û‘^\›˜[Ú[™İĞÛÜÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û•Ú[™İÓY™][YJ\Ú[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØ[˜Ù[™^^\›˜[Ú[™İĞÛÜÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈÙXÛÛ™\HÚ[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][ÛÛÛZ[œÕÚ[™İÊ\ÙXÛÛ™\UÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Ûˆ^]Ûİ[™Y›Ü™HXZ[ˆÛÜÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈÚ^™K]ËXÛÛ[Ú[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ú^™UĞÛÛ[HÚ^™UĞÛÛ[•ÚY[™ZYÚ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÚ^™UĞÛÛ[ÛÛ[•ÚYHMM‹Œ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™HÚ^™K]ËXÛÛ[ÜX›HÜİÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™HÚ^™K]ËXÛÛ[ÜX›HÜİÚY›İ[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\™]ÙY[ŠMŒMŒÙ]ÜX›RÜİİX›JÚ^™UĞÛÛ[Ú[™İË•ÚYŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]ÜX›RÜİİX›JÚ^™UĞÛÛ[Ú[™İË•ÚYŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚ^™K]ËXÛÛ[ÜX›HÜİZYÚ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈİÛ™YÚ[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“İÛ™\ˆHÚ[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË“İÛ™YÚ[™İÜËÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİÛ™YÚ[™İÈÛÜÚ[™È][\YØ[˜Ù[İ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈ[Ù[X[ÙÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›[Ù[X[ÙË”ÚİÑX[ÙÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›[Ù[X[ÙË‘X[ÙÔ™\İ[HYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[Ù[X[ÙÈ™\İ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ú]İÛ“[ÙK“Û“XZ[•Ú[™İĞÛÜÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\‘^\›˜[İ\\]™[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\‘^\›˜[^]]™[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[•˜[Y]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\Ûİ\˜Ù\Ö×‘^\›˜[İ\\^—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\Ûİ\˜Ù\Ö×‘^\›˜[İ\\œ\Ú—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\“XZ[•Ú[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\•Ú[™İÜËÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ú]İÛ“[ÙK“Û“\İÚ[™İĞÛÜÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•ZYW‘^\›˜[ØØ[^™Y^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØØ[^˜][Û‹]šX]\ÏW‰ÛÛ[
™XYX›H[ÙYšXX›H^
Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØØ[^˜][Û‹ÛÛ[Y[ÏW‰ÛÛ[
^\›˜[ÑÈØØ[^˜][ÛˆÛÛ[Y[
Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØØ[^˜][Û\ÜÙ[X›S˜[YHH‘^\›˜[ØØ[^˜][Û\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\\™Q^\›˜[ØØ[^˜][Û”›Ú™Xİ
ÛÜšÔ›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØØ[^˜][Û‘\™Xİ]™\ÕÓØÑš[O[ÓØØ[^˜][Û‘\™Xİ]™\ÕÓØÑš[Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[ØØ[^˜][Û‘\™Xİ]™\ÊÛÜšÔ›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØØ[^™YšY]Ë›ØÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØØ[^˜][Û”›Ûİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØØ[^˜][Ûˆ^ÛÛ[Y[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[™\Ûİ\˜Ù\Ë[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\Ûİ\˜ÙQXİ[Û˜\HÛİ\˜ÙOW‘^\›˜[™\Ûİ\˜Ù\Ë[[ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ]XĞœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•YÏWŞ“[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[[š[œÚXÕ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ“[[š[œÚXÈYÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\œ˜^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[\œ˜^R][\×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\OWŞ•\HŞ\Î”İš[™ßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[\œ˜^R][\ĞÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\œ˜^H][\ÔÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\Û™[™\Ûİ\˜ÙRÙ^H\R[•\™Ù]\ÜÙ[X›O^Ş•\HØØ[“XZ[•Ú[™İßK™\Ûİ\˜ÙRYQ^\›˜[ÛÛ\Û™[XØÙ[œ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ\Û™[™\Ûİ\˜ÙU^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™™\Ûİ\˜ÙJ‘^\›˜[Yš[š][SZ\ÜÚ[™Ô™\Ûİ\˜ÙWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\Ûİ\˜ÙT™Y™\™[˜ÙRÙ^S›İ›İ[™^Ù\[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™™\Ûİ\˜ÙHZ\ÜÚ[™È™\Ûİ\˜ÙHÙ^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[˜[ZXĞœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T[[YT™\Ûİ\˜ÙT™Y™\™[˜ÙJÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù]™\Ûİ\˜ÙT™Y™\™[˜ÙJ^›ØÚË‘›Ü™YÜ›İ[™›Ü\K‘^\›˜[[˜[ZXĞœ\ÚŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[[YHÙ]™\Ûİ\˜ÙT™Y™\™[˜ÙH\]Y›Ü™YÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[[YSY\™ÙY™\Ûİ\˜ÙU^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[[YSY\™ÙY^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[[YSY\™ÙYœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T[[YS˜[YTØÛÜJÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™YÚ\İ\“˜[YJ‘^\›˜[[[YT™YÚ\İ\™Y]Û—‹™YÚ\İ\™Y]ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•[œ™YÚ\İ\“˜[YJ‘^\›˜[[[YT™YÚ\İ\™Y]Û—ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[[YH˜[Y\ØÛÜH\XØ]H™\Ù\™\ÈÜšYÚ[˜[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[[YH˜[Y\ØÛÜH™\XÙ[Y[Øš™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[ØYÛÛ\Û™[šY]Ë[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ\X[Û\ÜÈ^\›˜[ØYÛÛ\Û™[šY]Èˆ\Ù\ÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[X[X[ØYÛÛ\Û™[šY]Ë[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ\X[Û\ÜÈ^\›˜[X[X[ØYÛÛ\Û™[šY]Èˆ\Ù\ÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û“ØYÛÛ\Û™[

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹“ØYÛÛ\Û™[
™]È\šJ‹Ñ^\›˜[ÙÔÚ[ØÛÛ\Û™[Ñ^\›˜[ØYÛÛ\Û™[šY]Ë[[‹\šRÚ[™”™[]]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹“ØYÛÛ\Û™[
ˆX[X[SØYYšY]Ë‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‹Ñ^\›˜[ÙÔÚ[ØÛÛ\Û™[Ñ^\›˜[X[X[ØYÛÛ\Û™[šY]Ë[[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹“ØYÛÛ\Û™[[œİ[˜ÙHİ]XÈ™\Ûİ\˜ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹“ØYÛÛ\Û™[[œİ[˜ÙHXœ˜\Hš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØYÛÛ\Û™[^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹“ØYÛÛ\Û™[İ]XÈ™\Ûİ\˜ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØYÛÛ\Û™[[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[\XÚ]İ[T[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊİ[H\™Ù]\OWŞ•\H^›ØÚßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[YOW™^\›˜[[\XÚ]İ[HXİ]™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[YOWÑ[˜[ZXÔ™\Ûİ\˜ÙH^\›˜[İ]XĞœ\ÚWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[\XÚ]İ[Y^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[œ™Y^˜X›Pœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[œ™Y^˜X›QÜ˜YY[œ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[™X\‘Ü˜YY[œ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ü˜YY[İÜÛÛÜWˆÌ‘MˆÙ™œÙ]WŒˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ü˜YY[İÜÛÛÜWˆĞŒMQLĞ—ˆÙ™œÙ]WŒWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ]XÕ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Øš™Xİ]T›İšY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Øš™Xİ]T›İšY\‹“Y]Ù\˜[Y]\œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™\Ûİ\˜ÙQ˜XİÜH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[]T›İšY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Øš™Xİ›İšY\•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[›İšY\•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[X\šİ\^[œÚ[Û•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WÛØØ[‘^\›˜[^™Yš^Y^\›˜[˜[YO[X\šİ\Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈ^\›˜[^^[œÚ[ÛˆˆX\šİ\^[œÚ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ™\œšYHØš™Xİ›İšYU˜[YJTÙ\šXÙT›İšY\ˆÙ\šXÙT›İšY\ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’T›İšYU˜[YU\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SX\šİ\^[œÚ[ÛœÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\[YX\šİ\^[œÚ[Ûˆ›İšYY^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]Û•[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜\ÙY]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[šYÙÙ\™Y]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™[Ù]\]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ÙYÛWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[˜\ÙY]Û”İ[_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]™[Ù]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[™\W“Û‘^\›˜[İ[Q]™[]ÛÛXÚ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYÙÙ\ˆ›Ü\OW’\Ñ[˜X›Yˆ˜[YOW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜XÚÙÜ›İ[™WÕ[\]Pš[™[™È˜XÚÙÜ›İ[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[WÕ[\]Pš[™[™ÈÛÛ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[[\]Y\™[^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WĞš[™[™È™[]]™TÛİ\˜ÙO^Ô™[]]™TÛİ\˜ÙH[\]Y\™[K]PÛÛ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\İX[İ]SX[˜YÙ\‹•š\İX[İ]QÜ›İ\Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\İX[İ]QÜ›İ\“˜[YOW‘^\›˜[ÛÛ[[Û”İ]\×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\İX[İ]H“˜[YOW”™\ÜÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İÜX›Ø\™•\™Ù]›Ü\OW“ÜXÚ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ›Û[\]K•šYÙÙ\œÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYÙÙ\ˆ›Ü\OW•Y×ˆ˜[YOW[\]K]šYÙÙ\‹XXİ]™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ›Û[\]HšYÙÙ\ˆXİ[Ûˆ[\Xİ[ÛœÈ\™Ù]˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆÛÛ›Û[\]HšYÙÙ\ˆ[\Xİ[ÛœÈZ[•ÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K•Ú[™İÜË“YYXK[š[X][ÛÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[ØYYİÜX›Ø\™^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØYYW“Û‘^\›˜[ØYYİÜX›Ø\™^ØYYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]™[šYÙÙ\ˆ›İ]Y]™[W‘œ˜[Y]ÛÜšÑ[[Y[“ØYYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™YÚ[”İÜX›Ø\™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İÜX›Ø\™•\™Ù]˜[YOW‘^\›˜[ØYYİÜX›Ø\™^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[›Ü\UšYÙÙ\Xİ[Û•^İ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•šYÙÙ\‹‘[\Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•šYÙÙ\‹‘^]Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[›Ü\UšYÙÙ\Xİ[Û•^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[][UšYÙÙ\Xİ[Û•^İ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“][UšYÙÙ\‹‘[\Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“][UšYÙÙ\‹‘^]Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[][UšYÙÙ\Xİ[Û•^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[]UšYÙÙ\Xİ[Û•^İ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]UšYÙÙ\‹‘[\Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]UšYÙÙ\‹‘^]Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İÜX›Ø\™•\™Ù]›Ü\OW“ÜXÚ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[]UšYÙÙ\Xİ[Û•^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[][Q]UšYÙÙ\Xİ[Û•^İ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“][Q]UšYÙÙ\‹‘[\Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“][Q]UšYÙÙ\‹‘^]Xİ[ÛœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[][Q]UšYÙÙ\Xİ[Û•^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İ[OWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[šYÙÙ\™Y]Û”İ[_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[]™[Ù]\]Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İ[OWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[]™[Ù]\]Û”İ[_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ[Q]™[]ÛÛXÚĞÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[İ[Q]™[]ÛÛXÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØYYİÜX›Ø\™^ØYYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[ØYYİÜX›Ø\™^ØYY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Tİ[\Ğ[™[\]\ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØYYİÜX›Ø\™Y]Y]JÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØYYİÜX›Ø\™Y\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T›Ü\UšYÙÙ\Xİ[ÛœÓY]Y]JÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T›Ü\UšYÙÙ\Xİ[ÛœĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S][UšYÙÙ\Xİ[ÛœÓY]Y]JÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S][UšYÙÙ\Xİ[ÛœĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q]UšYÙÙ\Xİ[ÛœÓY]Y]JÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q]UšYÙÙ\Xİ[ÛœĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S][Q]UšYÙÙ\Xİ[ÛœÓY]Y]JÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S][Q]UšYÙÙ\Xİ[ÛœĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYÙÙ\™Yİ[K˜\ÙYÛˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYÙÙ\™Yİ[K•šYÙÙ\œËÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ù]\”İ[K”Ù]\œË“Ù•\O]™[Ù]\Š
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ù]\]Û‹”˜Z\ÙQ]™[
™]È›İ]Y]™[\™ÜÊ]Û˜\ÙKÛXÚÑ]™[]™[Ù]\]ÛŠJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™[Ù]\ˆ›İ]Y]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜]Û•[\]K‘š[™˜[YJ‘^\›˜[[\]T›Ûİ‹İ[Y]ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜]Û•[\]K‘š[™˜[YJ‘^\›˜[[\]Y\™[^‹İ[Y]ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\İX[İ]SX[˜YÙ\‹‘Ù]š\İX[İ]QÜ›İ\Ê[\]T›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İÜX›Ø\™‘Ù]\™Ù]›Ü\JİX›P[š[X][ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØYYİÜX›Ø\™\™Ù]›Ü\H]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆØYYİÜX›Ø\™ÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\šYÙÙ\Xİ[Û”İÜX›Ø\™
][UšYÙÙ\‹‘[\Xİ[ÛœÖÌKN™^\›˜[ÑÈ][HšYÙÙ\ˆXİ[Ûˆ[\Xİ[Ûœ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\šYÙÙ\Xİ[Û”İÜX›Ø\™
][UšYÙÙ\‹‘^]Xİ[ÛœÖÌK™^\›˜[ÑÈ][HšYÙÙ\ˆXİ[Ûˆ^]Xİ[Ûœ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\šYÙÙ\Xİ[Û”İÜX›Ø\™
]UšYÙÙ\‹‘[\Xİ[ÛœÖÌKŒÌK™^\›˜[ÑÈ]HšYÙÙ\ˆXİ[Ûˆ[\Xİ[Ûœ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\šYÙÙ\Xİ[Û”İÜX›Ø\™
]UšYÙÙ\‹‘^]Xİ[ÛœÖÌK‹™^\›˜[ÑÈ]HšYÙÙ\ˆXİ[Ûˆ^]Xİ[Ûœ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\šYÙÙ\Xİ[Û”İÜX›Ø\™
][Q]UšYÙÙ\‹‘[\Xİ[ÛœÖÌKŒ™^\›˜[ÑÈ][H]HšYÙÙ\ˆXİ[Ûˆ[\Xİ[Ûœ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\šYÙÙ\Xİ[Û”İÜX›Ø\™
][Q]UšYÙÙ\‹‘^]Xİ[ÛœÖÌKÍ‹™^\›˜[ÑÈ][H]HšYÙÙ\ˆXİ[Ûˆ^]Xİ[Ûœ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][HšYÙÙ\ˆXİ[Ûˆ\X[XÛÛ™][ÛˆÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][HšYÙÙ\ˆ[\Xİ[ÛœÈÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][HšYÙÙ\ˆ^]Xİ[ÛœÈÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][HšYÙÙ\ˆ™KY[\ˆÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ]HšYÙÙ\ˆ[\Xİ[ÛœÈÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ]HšYÙÙ\ˆ^]Xİ[ÛœÈÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][H]HšYÙÙ\ˆXİ[Ûˆ\X[XÛÛ™][ÛˆÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][H]HšYÙÙ\ˆ[\Xİ[ÛœÈÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆ][H]HšYÙÙ\ˆ^]Xİ[ÛœÈÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Uš\İX[İ]U˜[œÚ][ÛœÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\İX[İ]SX[˜YÙ\‹‘ÛÕÔİ]Jİ[Y]Û‹”™\ÜÙY‹˜[ÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆš\İX[İ]SX[˜YÙ\ˆ™\ÜÙYÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆš\İX[İ]SX[˜YÙ\ˆ›Ü›X[˜[œÚ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›Ü\HšYÙÙ\ˆ˜XÚÙÜ›İ[™Ù]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\]Pš[™[™ÈšYÙÙ\™Y˜XÚÙÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™TÛİ\˜ÙH[\]Y\™[ÛÛ[š[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[]]™TÛİ\˜ÙS[ÙK•[\]Y\™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][\Ô[™[[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][PÛÛZ[™\”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ü›İ\Y][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ü›İ\XY\•[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[œ˜[Y]ÛÜšÒ][U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™[™\š[™Ò][U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Y˜][][U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[\XÚ]][U[\]U^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WĞš[™[™È˜[YKİš[™Ñ›Ü›X]Q^\›˜[[\XÚ]Ì_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][U[\]TÙ[XİÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][[\]OWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[Y˜][][U[\]_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[œ˜[Y]ÛÜšÒ][PÛÛZ[™\”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Y˜][][PÛÛZ[™\”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][PÛÛZ[™\”İ[TÙ[XİÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’][PÛÛZ[™\”İ[TÙ[XİÜWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[][PÛÛZ[™\”İ[TÙ[XİÜŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ[TÙ[XİÜ’][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ[TÙ[XİÜ’][U^›ØÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™]š[İ\Ñ]R][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[]]™TÛİ\˜ÙO^Ô™[]]™TÛİ\˜ÙH™]š[İ\Ñ]_H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T™]š[İ\Ñ]Pš[™[™ÜĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]QÙ[™\˜]Y™]š[İ\Ñ]R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™]š[İ\Ñ]H\™™]š[İ\È^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[š[\™Y][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[\W“Û‘^\›˜[][\Ñš[\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™Qš[\™Y][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\Ó]™Qš[\š[™Ô™\]Y\İYW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[Û•šY]ÔÛİ\˜ÙK“]™Qš[\š[™Ô›Ü\Y\Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊŞ\Î”İš[™Ï’\ĞXİ]™OÜŞ\Î”İš[™Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™TÛÜY][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\Ó]™TÛÜ[™Ô™\]Y\İYW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[Û•šY]ÔÛİ\˜ÙK“]™TÛÜ[™Ô›Ü\Y\Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™Qš[\™Y][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™TÛÜY][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™QÜ›İ\Y][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\Ó]™QÜ›İ\[™Ô™\]Y\İYW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[Û•šY]ÔÛİ\˜ÙK“]™QÜ›İ\[™Ô›Ü\Y\Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊŞ\Î”İš[™Ï’Ú[™ÜŞ\Î”İš[™Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]™QÜ›İ\Y][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ\œ™[˜ŞR][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\ÔŞ[˜Ú›Ûš^™YÚ]İ\œ™[][OW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ\ÜÚ]R][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\ÜÚ]PÛÛXİ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[ÛÛÛZ[™\ˆÛÛXİ[ÛWŞ”İ]XÈØØ[‘^\›˜[ÛÛ\ÜÚ]T›İšY\‹’][\ßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ]XÈÛ\ÜÈ^\›˜[ÛÛ\ÜÚ]T›İšY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[Û•šY]ÔÛİ\˜ÙK”ÛÜ\ØÜš\[ÛœÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\Û™[[Ù[”ÛÜ\ØÜš\[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›Ü\S˜[YOW“˜[YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛXİ[Û•šY]ÔÛİ\˜ÙK‘Ü›İ\\ØÜš\[ÛœÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›Ü\QÜ›İ\\ØÜš\[Ûˆ›Ü\S˜[YOW’Ú[™ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ü›İ\Y][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\İ›Ş‘Ü›İ\İ[Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ü›İ\İ[HXY\•[\]OWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[Ü›İ\XY\•[\]_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^[İ]ÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØÚÔ[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ø[˜\ĞÚ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[šY›Ü›QÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÜšYÜ]\‘ÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÜšYÜ]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][\Ô[™[\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[\]TÙ[XİÜ”™\Ù[\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[[\]TÙ[XİÜWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[][U[\]TÙ[XİÜŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[\XÚ][\]T™\Ù[\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[\]TÙ[XİÜ’][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’][U[\]TÙ[XİÜWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[][U[\]TÙ[XİÜŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[\İšY]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\İšY]Ë•šY]Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘ÜšYšY]ĞÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü^SY[X™\š[™[™ÏWĞš[™[™È˜[Y_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü^SY[X™\š[™[™ÏWĞš[™[™ÈÚ[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]QÜšY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]]ÑÙ[™\˜]PÛÛ[[œÏW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØ[•\Ù\Y›İÜÏW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]QÜšY^ÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]QÜšYÚXÚĞ›ŞÛÛ[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÏWĞš[™[™È\ĞXİ]™_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ›ÛÛ\ĞXİ]™H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\ĞXİ]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S^[İ]Ğ[™][\ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘ÜšY‘Ù]ÛÛ[[”Ü[ŠÜšY˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘ØÚÔ[™[‘Ù]ØÚÊØÚÕÜ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØ[˜\Ë‘Ù]Y
Ø[˜\ĞÚ[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜšYÜ]\ˆ™\Ú^™H™Z]š[Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]QÜšYÜ]\‘˜YĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜšYÜ]\ˆ˜YÙÙYYÛÛ[[ˆÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜšYÜ]\ˆ˜YÙÙYšYÚÛÛ[[ˆÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš][\Ô[™[[\]K“ØYÛÛ[

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš][T[™[\İ’][PÛÛZ[™\”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H[™[\İÛÛXİ[ÛˆÛİ[Y\ˆ]]][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P[\›˜][ÛY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’][\ĞÛÛ›Û‘Ù][\›˜][Û’[™^
][PÛÛZ[™\ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\›˜][Ûˆ\™[™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H[\]HÙ[XİÜˆœ˜[Y]ÛÜšÈ[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ[[\]HÙ[XİÜˆÙ[XİY[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\[\]U^
œ˜[Y]ÛÜšÕ[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[™\š[™ÈÙ[XİY[\]H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H[\]HÙ[XİÜˆÛÛXİ[ÛˆÛİ[Y\ˆ]]][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H[\]HÙ[XİÜˆY˜][Ù[XİY[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\İ›Ş][PÛÛZ[™\”İ[TÙ[XİÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]R][PÛÛZ[™\”İ[TÙ[XİÜY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][PÛÛZ[™\”İ[TÙ[XİÜˆœ˜[Y]ÛÜšÈÙ[™\˜]YÛÛZ[™\ˆİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][PÛÛZ[™\”İ[TÙ[XİÜˆY˜][Ù[™\˜]Y^›ØÚÈ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™š\İX[\ØÙ[™[S˜[YJ][PÛÛZ[™\‹‘^\›˜[İ[TÙ[XİÜ’][U^›ØÚ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ›İ\YÛÛXİ[Û•šY]ÔÛİ\˜ÙHÛÜ›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ›İ\YÛÛXİ[Û•šY]ÔÛİ\˜ÙHÜ›İ\›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ›İ\Y\İ›Ş][\ÔÛİ\˜ÙHšY]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ›İ\YÛÛXİ[Û•šY]ÔÛİ\˜ÙHšY]ÈÜ›İ\Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ›İ\XY\ˆÙ[™\˜]Y^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[\™YÛÛXİ[Û•šY]ÔÛİ\˜ÙHš[\ˆ]™[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™š[\™Y][\Ë•šY]Ë”™Yœ™\Ú

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[\™Y\İ›Ş™Yœ™\ÚY][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ\œ™[˜ŞHİ\œ™[][Hœ›ÛHÙ[Xİ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\œ™[˜ŞR][\Ë•šY]Ë“[İ™Pİ\œ™[ÔÜÚ][ÛŠŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ\œ™[˜ŞH\İ›ŞÙ[XİY][HY\ˆİ\œ™[[İ™H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\ÜÚ]PÛÛXİ[ÛˆÛİ\˜ÙH\Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\ÜÚ]PÛÛXİ[Ûˆİ]XÈÛİ\˜ÙH][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\ÜÚ]PÛÛXİ[Ûˆ[š]X[›][™Y][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\ÜÚ]PÛÛXİ[ÛˆÛÛXİ[Û‹XÚ[™ÙH\[™YÛİ\˜ÙH][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\İšY]ÈÜšY]šY]ÈÛÛ[[ˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\İšY]È˜[YHš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]HÜšYÛÛ[[ˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]HÜšYXİ]™Hš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]HÜšYÙ[XİY][HY\ˆÚ[™ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›ÙU[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊY\˜\˜ÚXØ[]U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]U\OWŞ•\HØØ[‘^\›˜[›Ù_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’][\ÔÛİ\˜ÙOWĞš[™[™ÈÚ[™[ŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™YUšY]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^XÚ]™YUšY]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™YT›Ûİ][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^[™YW“Û‘^\›˜[™YR][Q^[™Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİYW“Û‘^\›˜[™YR][TÙ[XİYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØœÙ\˜X›PÛÛXİ[Û^\›˜[›ÙOˆ^\›˜[›Ù\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™YQ^[™YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™YTÙ[XİYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ››ÙU[\]K’][\ÔÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÙH[\]H][\ÔÛİ\˜ÙH]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™YHÙ[XİYÜšYÚ[˜[Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™YH[œÙ[XİY]™[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Y[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ[X[™Y[R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÚXÚØX›SY[R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ü\İÛ™\]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ[™[Û™TÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ^Y[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ^ÚXÚØX›SY[R][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÚXÚĞ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜Y[Ğ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÙÙÛP]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ˜\•˜^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛ˜\ÛÛ[X[™]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ]\Ğ˜\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[\ÜİÛÜ™›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ø[[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[]TXÚÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›ÙÜ™\ÜĞ˜\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™\X]]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØÜ›Û˜\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜YÕ[Xˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[Y[R][PÛXÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[ÛÛ^Y[R][PÚXÚÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[˜Y[Ğ]ÛÚXÚÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[\ÜİÛÜ™Ú[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[ÛY\•˜[YPÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[™\X]]ÛÛXÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[ØÜ›Û˜\”ØÜ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[[X‘˜YÔİ\Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[[X‘˜YÑ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[[X‘˜YĞÛÛ\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[X˜›Y[X‘˜YÑ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SY[\Ğ[™ÚÚXÙPÛÛ›ÛÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜ\Ü[š[™ĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆY[H][HÜ[™Y›İYÚÜX›HÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆÛÛX›È›ŞÜ[™Y›İYÚÜX›HÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆİ[™[Û™HÜ\Ü[™Y›İYÚÜX›HÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆÛÛ\Ü[™Y›İYÚÜX›HÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][Û‹”[ˆÛÛ^Y[HÜ[™Y›İYÚÜX›HÜ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]UÛÛ˜\”İ]\Ô˜[™ÙT\ÜİÛÜ™]PÛÛ›ÛÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]U[X‘˜YÓX[˜YÙ\ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY[HÛÛ[X[™^Xİ]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ^ÛÛ[X[™^Xİ]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\XÙ[Y[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚXÚÈ›Ş[˜ÚXÚÙY›İ]Y]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜Y[È[˜ÚXÚÙYÙ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙÙÛH[˜ÚXÚÙY›İ]Y]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ˜\ˆ›İ]YÛÛ[X[™Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ]\È˜\ˆÙ[XİY][Hš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\ÜİÛÜ™›ŞÙXİ\™H\ÜİÛÜ™[™İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØ[[™\ˆÙ[XİY]HÛÛXİ[Ûˆ][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]TXÚÙ\ˆÙ[XİY]H›Ü›X]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÙÜ™\ÜĞ˜\ˆ[[Y[˜[YHš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÙÜ™\ÜĞ˜\ˆ˜[YHY\ˆÛY\ˆ\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™\X]]Ûˆ›İ]Y]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØÜ›Û˜\ˆ[™QİÛˆÛÛ[X[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØÜ›Û˜\ˆØÜ›ÛĞ›İÛHÛÛ[X[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[Xˆ˜YÔİ\Y[™\ˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[Xˆ˜YÑ[HÜš^›Û[Ú[™ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[XˆX˜›Y˜YÑ[HÜšYÚ[˜[Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[Xˆ˜YĞÛÛ\]YØ[˜Ù[Yİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[YÜ›™\‘XÛÜ˜]Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[YÜ›™Y]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈ^\›˜[YÜ›™\ˆˆYÜ›™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]PYÜ›™\‘XÛÜ˜]ÜŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]PYÜ›™\“^Y\ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈYÜ›™\“^Y\ˆYYYÜ›™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÛX›Ğ›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY˜[YT]W’Ú[™ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY˜[YOWĞš[™[™ÈÙ[XİY^\›˜[Ú[™[ÙOUÛÕØ^_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[Xİ[ÛÚ[™ÙYW“Û‘^\›˜[Ù[Xİ[ÛÚ[™ÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\Õ^ÙX\˜Ú[˜X›YW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^ÙX\˜Ú•^]W“˜[YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][TÙ[Xİ][\Ó\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[Xİ[Û“[ÙOW“][\Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[Xİ[ÛÚ[™ÙYW“Û‘^\›˜[][TÙ[Xİ[ÛÚ[™ÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S][TÙ[Xİ[ÛY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][K\Ù[Xİ™[[İ™Y][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“\İ^\›˜[][TÙ[Xİ[ÛYYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[XÛÛ›Û‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ü›İ\›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^[™YW“Û‘^\›˜[^[™\‘^[™Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\ÙYW“Û‘^\›˜[^[™\ÛÛ\ÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ØÜ›ÛšY]Ù\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[XİY^\›˜[Ú[™ÈÙ]ÈÙ]ÈHH”™[™\š[™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ù[Xİ[ÛÚ[™ÙYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^[™\‘^[™YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÙ[XİÜœĞ[™ÛÛ[
Ú[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛX›Ğ›Ş‘Ù]š[™[™Ñ^™\ÜÚ[ÛŠÙ[XİÜ‹”Ù[XİY˜[YT›Ü\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛX›È›Ş^ÙX\˜Ú[˜X›Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛX›È›Ş^ÙX\˜Ú]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛX›È›ŞÛË]Ø^HÙ[XİY˜[YHÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÛÛ›Û”Ù[XİY[™^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈXˆÙ[Xİ[ÛˆÛİ\˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ›İ\›ŞÛÛ[š[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^[™\ˆ^[™Y]™[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØÜ›ÛšY]Ù\ˆ™\XØ[š\ÚXš[]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[šXÚ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘›İÑØİ[Y[YÙTY[™ÏWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Øİ[Y[[šÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ]U\šOWšÎ‹ËÙ^[\K\İÙ^\›˜[\Ù×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Y\İ˜]šYØ]OW“Û‘^\›˜[Øİ[Y[[šÔ™\]Y\İ˜]šYØ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›[™URPÛÛZ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØÚÕRPÛÛZ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊX›HÙ[ÜXÚ[™ÏWŒˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Øİ[Y[[šÔ™\]Y\İ˜]šYØ]PÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K•Ú[™İÜË‘Øİ[Y[ÎÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TšXÚØİ[Y[ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš\\›[šË‘ĞÛXÚÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\\›[šÈ™\]Y\İ˜]šYØ]H›İ]Y]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[\İX\šÙ\ˆİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[X›HÙ[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›İÑØİ[Y[ØÜ›ÛšY]Ù\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’\ÕÛÛ˜\•š\ÚX›OW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[ØÜ›ÛšY]Ù\ˆ\İX\šÙ\ˆİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›İÑØİ[Y[YÙUšY]Ù\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–›ÛÛOWŒLWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[YÙUšY]Ù\ˆ›ÛÛH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[YÙUšY]Ù\ˆ\İX\šÙ\ˆİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[YÙUšY]Ù\ˆ^˜[™ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›İÑØİ[Y[™XY\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•šY]Ú[™Ó[ÙOW”ØÜ›Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İÑØİ[Y[™XY\ˆšY]Ú[™È[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TšXÚ^Y][™ĞÛÛ[X[™ÊšXÚ^›Ş[›Ô\˜YÜ˜\Z[”[‹Øİ[Y[\İ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y][™ĞÛÛ[X[™Ë•ÙÙÛP›Û‘^Xİ]J[šXÚ^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšXÚ^›ŞÙÙÛP›Û\YYÙZYÚ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y][™ĞÛÛ[X[™Ë•ÙÙÛR][XË‘^Xİ]J[šXÚ^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšXÚ^›ŞÙÙÛR][XÈ\YYİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y][™ĞÛÛ[X[™Ë•ÙÙÛU[™\›[™K‘^Xİ]J[šXÚ^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšXÚ^›ŞÙÙÛU[™\›[™HXÛÜ˜][ÛˆØØ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y][™ĞÛÛ[X[™Ë[YÛ”šYÚ‘^Xİ]J[šXÚ^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšXÚ^›Ş[YÛÙ[\ˆ\˜YÜ˜\[YÛ›Y[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y][™ĞÛÛ[X[™Ë•ÙÙÛP[]Ë‘^Xİ]J[šXÚ^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšXÚ^›ŞÙÙÛS[X™\š[™ÈX\šÙ\ˆİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈšXÚ^›ŞÙ[Xİ[Ûˆ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È^˜[™ÙJØİ[Y[ÛÛ[İ\Øİ[Y[ÛÛ[[™
K•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Ü[ÚXÚÕ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ü[ÚXÚË’\Ñ[˜X›YW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜ[ÚXÚÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ[ÚXÚÈ›Ë[Ü™^Ü[[™È\œ›Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ[ÚXÚÈİ\İÛHXİ[Û˜\HYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ[ÚXÚÈ\ØX›Y[œİ[˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
TŞ\İ[K“Ü\˜][™ÔŞ\İ[K’\ÕÚ[™İÜÊ
JH‹Ü[\’[\›Ü˜\ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ™]È[Ü[\’[\›Ü

NÈ‹Ü[\’[\›Ü˜\ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÙX[YÛ\ÜÈ[Ü[\’[\›ÜˆÜ[\’[\›Ü˜\ÙH‹Ü[\’[\›Ü˜\ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ™\œšYH›ÛÛØ[”Ü[ÚXÚÊİ[\™R[™›Èİ[\™JH‹Ü[\’[\›Ü˜\ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ˜[ÙNÈ‹Ü[\’[\›Ü˜\ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[][U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]U\OWŞ•\HØØ[‘^\›˜[][_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\XÚ]^İ[H\™Ù]\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\XÚ]İ[Y^›Ü™YÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È]U[\]RÙ^J\[ÙŠ^\›˜[][JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\XÚ]][H]H[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\XÚ]][H[\]Hš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[\XÚ]][H[\]H™\ÛÛ™Y^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØœÙ\˜X›PÛÛXİ[Û^\›˜[][Oˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û”™\Ûİ\˜Ù\ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TXÚÔ™\Ûİ\˜Ù\Ê
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹‘Ù]™\Ûİ\˜ÙTİ™X[J™\Ûİ\˜ÙU\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹‘Ù]ÛÛ[İ™X[JÛÛ[\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹‘Ù]™[[İTİ™X[J™[[İU\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚÎ‹ËØ\XØ][Û‹Ğ\ÜÙ]ËÑ^\›˜[™\Ûİ\˜ÙK‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚÎ‹ËØ\XØ][Û‹Ğ\ÜÙ]ËÑ^\›˜[ÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚÎ‹ËÜÚ][Ù›ÜšYÚ[‹Ğ\ÜÙ]ËÑ^\›˜[ÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™H™\Ûİ\˜ÙHİ™X[H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈXœÛÛ]HXÚÈ™\Ûİ\˜ÙHİ™X[H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™HÛÛ[İ™X[H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈXœÛÛ]HXÚÈÛÛ[İ™X[H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™H™[[İHİ™X[H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈXœÛÛ]HXÚÈ™[[İHİ™X[H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈÛÛ\Û™[™\Ûİ\˜ÙRÙ^J\[ÙŠXZ[•Ú[™İÊK‘^\›˜[ÛÛ\Û™[XØÙ[œ\ÚŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\Û™[™\Ûİ\˜ÙRÙ^H\XØ][Ûˆœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\Û™[™\Ûİ\˜ÙRÙ^HÚ[™İÈÛÚİ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\Û™[™\Ûİ\˜ÙRÙ^H›Ü™YÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØØ[^˜][Û‹‘Ù]ÛÛ[Y[ÊØØ[^™Y^
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØØ[^˜][Û‹‘Ù]]šX]\ÊØØ[^™Y^
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ•ZY˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØØ[^˜][Û‹]šX]\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØØ[^˜][Ûˆ›Ûİ\™Xİ]™HZY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØØ[^˜][Ûˆ^\™Xİ]™HZY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØØ[^˜][Ûˆ[ÙYšXX›H]šX]Hİ]]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØØ[^˜][Ûˆ[›[ÙYšXX›H]šX]Hİ]]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Qœ™Y^˜X›T™\Ûİ\˜Ù\Ê
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈœ™Y^˜X›Hœ\ÚÛÛ™H]]X›HÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈœ™Y^˜X›Hœ\Úİ\œ™[]˜[YHÛÛ™HÜXÚ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈœ™Y^˜X›HÜ˜YY[İÜÛÛXİ[Ûˆœ›Ş™[ˆİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈœ™Y^˜X›HÜ˜YY[ÛÛ™H]]X›HİÜÙ™œÙ]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈœ™Y^˜X›HÜ˜YY[İ\œ™[]˜[YHÛÛ™HİÜÛÛXİ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SX[˜YÙYœ˜[Y]ÛÜšĞÛÛXİ[ÛœÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“TË’[\›˜[“\İÙ“Øš™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\İÙ“Øš™Xİ[œÙ\›ÜØ\™ÈÈS\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\İÙ“Øš™XİÛX\ˆ›ÜØ\™ÈÈS\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“TË’[\›˜[•ÙXZÑXİ[Û˜\Xˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙXZÑXİ[Û˜\HÙ^HÛÛXİ[ÛˆÛÛZ[œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙXZÑXİ[Û˜\H˜[YHÛÛXİ[ÛˆÛÛZ[œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙXZÑXİ[Û˜\HÙ^HÛÛXİ[ÛˆY™Z™XİY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙXZÑXİ[Û˜\H˜[YHÛÛXİ[ÛˆÛX\ˆ™Z™XİY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K•Ú[™İÜË“YYXK’[XYÚ[™ÎÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K“™]ØXÚNÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K“™]”ÛØÚÙ]ÎÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SX[˜YÙY[XYÚ[™ÓØš™XİÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš]X\Ûİ\˜ÙKÜ™X]J‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš]X\[]\Ë›XÚĞ[™Ú]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[]\Ë•ÙX”[]HÛÛÜˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[]Jš]X\Ûİ\˜ÙK
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[]Hœ›ÛH‘ÔHÛİ\˜ÙHš\œİÛÛÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[]J[™^Y[]TÛİ\˜ÙKÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[]Hœ›ÛH[™^YÛİ\˜ÙH\™ÛÛÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš]X\œ˜[YKÜ™X]Jš]X\Ûİ\˜ÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È›\š]X\[˜ÛÙ\Š
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜›\[˜ÛÙ\‹”Ø]™J›\İ™X[JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›\š]X\[˜ÛÙ\ˆ›İÛK[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš]X\XÛÙ\‹Ü™X]J‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È›\š]X\XÛÙ\Š‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H“TÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJ›\\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’H“TXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’H“TÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È™Ğš]X\XÛÙ\Š‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H‘ÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H‘ÈÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÙX[YÛ\ÜÈÛÜ˜XÚÒ[XYÙTÙ\™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È™\]Y\İØXÚTÛXŞJ™\]Y\İØXÚS]™[”™[ØY
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H‘ÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™Ğš]X\XÛÙ\ˆT’Hœ˜[YHÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙH‘ÈÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’H‘ÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJ™Õ\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’H‘ÈÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚÎ‹ËØ\XØ][Û‹Ğ\ÜÙ]ËÑ^\›˜[[XYÙKœ™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HXÚÈ‘ÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™Ğš]X\XÛÙ\ˆXÚÈT’Hœ˜[YHÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHXÚÈ‘ÈÜ[Y™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[[[™\Ûİ\˜ÙR[XYÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ûİ\˜ÙOW\ÜÙ]ËÑ^\›˜[[XYÙKœ™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[[[[XYÙPœ\Ú™Xİ[™ÛWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[XYÙPœ\Ú[XYÙTÛİ\˜ÙOWœXÚÎ‹ËØ\XØ][Û‹Ğ\ÜÙ]ËÑ^\›˜[[XYÙKœ™×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈSS™\Ûİ\˜ÙH[XYÙHÜ[Y™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈSS[XYÙPœ\ÚÜ\šYÚÜ™Y[ˆ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]PY[MÔ™Ø˜T™Ğ]\Ê^[Ë‹‹
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H[\›XÙY‘ÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H[\›XÙY‘È›İÛK\šYÚ™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’H[\›XÙY‘ÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJ[\›XÙY™Õ\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’H[\›XÙY‘È›İÛK\šYÚ™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]T™ÒXÛÛ]\Ê™Ğ]\Ë‹ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈXÛÛš]X\XÛÙ\Š‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HPÓÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HPÓÈÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’HPÓÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJXÛÛ•\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’HPÓÈÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]QX’XÛÛ]\Ê^[Ë‹‹
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HPˆPÓÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HPˆPÓÈX\ÚÙY[H]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’HPˆPÓÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’HPˆPÓÈX\ÚÙY[H]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]RœYĞ]\Ê
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈœYĞš]X\XÛÙ\Š‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H”QÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H”QÈ›Û˜›[šÈ‘Ğˆİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’H”QÈXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJœYÕ\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’H”QÈš\œİ[H]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]QÚY]\Ê
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈÚYš]X\XÛÙ\Š‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HÒQˆXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HÒQˆš\œİYœ˜[YH[^HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HÒQˆÙXÛÛ™Yœ˜[YH[^HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™š\œİÚY“Y]Y]KÛÛZ[œÔ]Y\J‹ÙÜ˜İ^Ñ[^WŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™š\œİÚY“Y]Y]K‘Ù]]Y\J‹Ú[YÙ\ØËÕÚYŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HÒQˆÙXÛÛ™Yœ˜[YHÜ™Y[ˆ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚYš]X\XÛÙ\ˆœ˜[YHÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚYš]X\XÛÙ\ˆÙXÛÛ™Yœ˜[YH[^HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚYš]X\XÛÙ\ˆT’HÙXÛÛ™Yœ˜[YHÜ™Y[ˆ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HÒQˆ›Û˜›[šÈ‘Ğˆİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’HÒQˆXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’HÒQˆÙXÛÛ™Yœ˜[YH[^HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚYš]X\XÛÙ\ˆT’Hš\œİYœ˜[YH[^HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJÚY•\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’HÒQˆš\œİ[H]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]UY™]\Ê^[Ë‹ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈY™š]X\XÛÙ\Š‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HQ‘ˆXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HQ‘ˆY]Y]H›Ü›X]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HQ‘ˆÜšY[][Ûˆ]Y\H™\Ù[˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HQ‘ˆÜšY[][ÛˆY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY™š]X\XÛÙ\ˆÜšY[][ÛˆY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HQ‘ˆ›İÛK\šYÚ™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’HQ‘ˆXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’HQ‘ˆÜšY[][ÛˆY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY™š]X\XÛÙ\ˆT’HÜšY[][ÛˆY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Èš]X\[XYÙJY™•\šJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’HQ‘ˆÜ[Y›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Üš]UY™”ÚÜ[JY™‹™Yˆ[SÙ™œÙ]ÍŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]S][Qœ˜[YUY™]\Ê^[ËÙXÛÛ™Y™”^[Ë‹ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H][KYœ˜[YHQ‘ˆœ˜[YHÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][KYœ˜[YHY™š]X\XÛÙ\ˆœ˜[YHÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’H][KYœ˜[YHQ‘ˆœ˜[YHÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]T[]UY™]\ÊÌK‹×K‹‹
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H[]HQ‘ˆXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HT’H[]HQ‘ˆXÛÙ\ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\[XYÙHT’H[]HQ‘ˆ›İÛK\šYÚ™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]T™Ø˜T™Ğ]\Ê^[Ë‹‹
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]T™Ø˜LM”™Ğ]\Ê^[Ë‹‹
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]R[™^Y™Ğ]\Ê‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]HM‹Xš]‘ĞH‘È›İÛK\šYÚ™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\XÛÙ\‹Ü™X]H[™^Y‘È›İÛK[Y[H]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”^[›Ü›X]Ë’[™^Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[™^Yš]X\XÛÙ\ˆ[]HÜ™Y[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[™^Yš]X\[XYÙHT’H›İÛK[Y[™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Üš]XX›Pš]X\
‹‹M‹ŒM‹Œ^[›Ü›X]Ë™Ü˜LÌˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\Ûİ\˜ÙHÛÜYY›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš]X\œ˜[YHÛÜYY™Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜš]XX›Pš]X\ÛÜYYÙXÛÛ™\›İÈ›YH]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[XYÙHÛİ\˜ÙHš]X\œ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[XYÙPœ\ÚÛİ\˜ÙHš]X\Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K•Ú[™İÜË“X\šİ\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SÛÜÙV[[™XY\•Üš]\Š
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[™XY\‹”\œÙJÛÜÙV[[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™Jœ\Ú
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÜÙPXØÙ[œ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÜÙU^İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆİ[Hİ]XÔ™\Ûİ\˜ÙHœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÛÜÙR[œ]ØÛÜU^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[[ÛÜÙKZ[œ]\ØÛÜH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]ØÛÜS˜[YU˜[YK‘[XZ[\Ù\“˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆ[œ]ØÛÜS˜[YH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆ[œ]ØÛÜT˜\ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YÜ˜YY[İÜ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\ÙXÛÛ™İÜÛÛÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Y[R][K”Ù\\˜]Ü”İ[RÙ^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™JŞ\İ[T™\Ûİ\˜ÙQXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YŞ\İ[H™\Ûİ\˜ÙHÙ^HY[X™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\Ş\İ[KZÙ^Hİ[H\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\˜\ÙP]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\]Û”İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™Jİ[QXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™Yİ[H˜\ÙYÛˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\İ[H˜\ÙYÛˆÙ]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆİ[Y]Ûˆ[š\š]YYÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\]Û•[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™J[\]QXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YÛÛ›Û[\]HšYÙÙ\œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\ÛÛ›Û[\]HšYÙÙ\ˆ›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ\YYÛÛ›Û[\]HÛÛ[™\Ù[\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\‘]U[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™J]U[\]QXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆ]U[\]H˜[YHš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™Y]U[\]HšYÙÙ\œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\]U[\]HšYÙÙ\ˆš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ]U[\]HšYÙÙ\ˆÙ]\ˆ\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\]U[\]HÚ[™^›ØÚÈ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\“›ÙU[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™JY\˜\˜ÚXØ[[\]QXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆY\˜\˜ÚXØ[]U[\]H][\ÔÛİ\˜ÙH]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆY\˜\˜ÚXØ[]U[\]H˜[YHš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YY\˜\˜ÚXØ[]U[\]HšYÙÙ\œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\Y\˜\˜ÚXØ[]U[\]HšYÙÙ\ˆš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆY\˜\˜ÚXØ[]U[\]HšYÙÙ\ˆÙ]\ˆ\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\Y\˜\˜ÚXØ[]U[\]HÚ[™^›ØÚÈ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\’][\Ô[™[[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™J][\Ô[™[[\]QXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™Y][\Ô[™[[\]H[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\][\Ô[™[[\]H[™[˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\][\Ô[™[[\]HÜšY[][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\][\Ô[™[[\]H][HÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\‘Ü›İ\İ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™JÜ›İ\İ[QXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[™XY\ˆÜ›İ\İ[HXY\ˆš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YÜ›İ\İ[HXY\•[\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YÜ›İ\İ[H[™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\Ü›İ\İ[HY\ÒY‘[\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\Ü›İ\İ[HXY\ˆ^›ØÚÈ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\Ü›İ\İ[H[™[ÜšY[][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\‘[[Y[›Ûİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™Jœ˜[Y]ÛÜšÑ[[Y[›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™Yœ˜[Y]ÛÜšÑ[[Y[]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\œ˜[Y]ÛÜšÑ[[Y[›Ûİ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\œ˜[Y]ÛÜšÑ[[Y[]Ûˆ˜XÚÙÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\œ˜[Y]ÛÜšÑ[[Y[^›Ş^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Üš]\”\˜YÜ˜\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™J›İÑØİ[Y[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™Y›İÑØİ[Y[›Ûİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\›İÑØİ[Y[\˜YÜ˜\˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\›İÑØİ[Y[X›HÙ[Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\›İÑØİ[Y[^˜[™ÙHÙXÛÛ™\İ][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]U[YU[\]V[[Üš]\”›İ[™š\
Ú[™İË[YYÛÛ›Û
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[[Üš]\‹”Ø]™J[YU[\]JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™Y[YYÛÛ›Û[\]H›Ûİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\[YYÛÛ›Û[\]H\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÜÙH[[Üš]\ˆ›İ[™]š\[YYÛÛ›Û[\]HÛÛ\Û™[™\Ûİ\˜ÙHœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q]T›İšY\œÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØš™Xİ]T›İšY\ˆ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØš™Xİ]T›İšY\ˆ›İ[™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØš™Xİ]T›İšY\ˆš[™[™ÈÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[]T›İšY\ˆ›İ[™^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[]T›İšY\ˆš[™[™È]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[\\ÛÛ™\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ[[X\PÛÛ™\\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈ^\›˜[][U[\]TÙ[XİÜˆˆ]U[\]TÙ[XİÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘œ˜[Y]ÛÜšÕ[\]HÈÙ]ÈÙ]ÈH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[™\š[™Õ[\]HÈÙ]ÈÙ]ÈH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][[\]HÈÙ]ÈÙ]ÈH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈ^\›˜[][PÛÛZ[™\”İ[TÙ[XİÜˆˆİ[TÙ[XİÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘œ˜[Y]ÛÜšÔİ[HÈÙ]ÈÙ]ÈH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Y˜][İ[HÈÙ]ÈÙ]ÈH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›Û‘[\U˜[Y][Û”[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[š[™[™ÑÜ›İ\˜[Y][Û”[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ™\\^Ôİ]XÔ™\Ûİ\˜ÙH^\›˜[\\ÛÛ™\\ŸH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ][Pš[™[™ÈÛÛ™\\WÔİ]XÔ™\Ûİ\˜ÙH^\›˜[İ[[X\PÛÛ™\\ŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[Üš]Pš[™[™Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[˜[˜XÚĞš[™[™Õ^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜[˜XÚÕ˜[YOQ^\›˜[˜[˜XÚÈ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[\™Ù][š[™[™Õ^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\™Ù][˜[YOQ^\›˜[[^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[[^YYš[™[™Õ^›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[^OLH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[^YYš[™[™È[^HY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[^YYš[™[™È[[YYX]HÛİ\˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[^YYš[™[™ÈÛİ\˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Û™U[YPš[™[™Õ^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[ÙOSÛ™U[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ™U[YHš[™[™È™]Z[™Y\™Ù]^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Û™UØ^UÔÛİ\˜ÙPš[™[™Õ^›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[ÙOSÛ™UØ^UÔÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ™UØ^UÔÛİ\˜ÙHš[™[™ÈÛİ\˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØœÙ\˜X›PÛÛXİ[Û^\›˜[][Oˆ^\›˜[[™^Y][\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[[™^Yš[™[™Õ^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[™^Y][\ÖÌWK“˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[™^Yš[™[™È\]Y\™Ù]^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[İš[™Ñ›Ü›X]š[™[™Õ^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İš[™Ñ›Ü›X]Q^\›˜[›Ü›X]YÌH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİš[™Ñ›Ü›X]š[™[™ÈY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[š[™[™Õ˜[œÙ™\•^›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ûİ\˜ÙU\]YW“Û‘^\›˜[š[™[™ÔÛİ\˜ÙU\]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\™Ù]\]YW“Û‘^\›˜[š[™[™Õ\™Ù]\]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[š[™[™Õ˜[œÙ™\•^[ÙOUÛÕØ^K\]TÛİ\˜ÙUšYÙÙ\Q^XÚ]›İYSÛ”Ûİ\˜ÙU\]YUYK›İYSÛ•\™Ù]\]YUYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Ù[š[™[™Õ^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WĞš[™[™È™[]]™TÛİ\˜ÙO^Ô™[]]™TÛİ\˜ÙHÙ[ŸK]UYßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[[˜Ù\İÜš[™[™Ğ›Ü™\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WĞš[™[™È™[]]™TÛİ\˜ÙO^Ô™[]]™TÛİ\˜ÙH[˜Ù\İÜ•\O^Ş•\H›Ü™\Ÿ_K]UYßWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØØ[‘^\›˜[›Û‘[\U˜[Y][Û”[HÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[š[™[™ÑÜ›İ\[™[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÑÜ›İ\˜[YOW‘^\›˜[š[™[™ÑÜ›İ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š\œİ›Ü\OWš[™[™ÑÜ›İ\š\œİ˜[YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÙXÛÛ™›Ü\OWš[™[™ÑÜ›İ\\İ˜[YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[š[™[™ÑÜ›İ\š\œİ›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WĞš[™[™Èš[™[™ÑÜ›İ\š\œİ˜[YK\]TÛİ\˜ÙUšYÙÙ\Q^XÚ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[š[™[™ÑÜ›İ\\İ›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^WĞš[™[™Èš[™[™ÑÜ›İ\\İ˜[YK\]TÛİ\˜ÙUšYÙÙ\Q^XÚ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[›İ]Y]™[ÛÛ›Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[X˜›OW“Û‘^\›˜[İ\İÛPX˜›Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[›™[W“Û‘^\›˜[İ\İÛU[›™[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™YÚ\İ\”›İ]Y]™[
‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›İ][™Ôİ˜]YŞKX˜›H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›İ][™Ôİ˜]YŞK•[›™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[\[™[˜ŞT›Ü\PÛÛ›Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØØ[‘^\›˜[\[™[˜ŞT›Ü\PÛÛ›Û’[š\š]YX™[W‘^\›˜[[š\š]YX™[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÙ\˜ÙY[X™\WŒLŒˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜XÚÙY^W˜ÛÛ\[Y˜XÚÙY^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘œ˜[Y]ÛÜšÔ›Ü\SY]Y]SÜ[ÛœË’[š\š]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÙ\˜ÙS[X™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^Ú[™ÙYW“Û‘^\›˜[˜[Y][Û•^Ú[™ÙYˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y][Û‹‘\œ›ÜW“Û‘^\›˜[˜[Y][Û‘\œ›Ü—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“›İYSÛ•˜[Y][Û‘\œ›ÜW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[š[™[™Õ˜[œÙ™\•^ÈÙ]ÈÙ]ÈHH™^\›˜[˜[œÙ™\ˆ[š]X[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[š[™[™Õ^ÈÙ]ÈHH[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[š[™[™ÔÛİ\˜ÙU\]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[š[™[™Õ\™Ù]\]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[š[™[™ÔÛİ\˜ÙU\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[š[™[™Õ\™Ù]\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ][™İXYÙSX[˜YÙ\‹’[œ][™İXYÙOW™[‹UT×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]Y]Ù”™Y™\œ™Y[YTİ]OW“Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]Y]Ù”™Y™\œ™Y[YPÛÛ™\œÚ[Û“[ÙOW“˜]]™K[Ú\Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]Y]Ù”™Y™\œ™Y[YTÙ[[˜ÙS[ÙOW]]ÛX]X×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™Yİ[\‘^™\ÜÚ[ÛW–ĞKVŒNWJ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ü™ÜÓX\šİ\W™^\›˜[\ÙËZ[œ]\ØÛÜWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œ]ØÛÜS˜[YO‘[XZ[Û]Y™\ÜÏÒ[œ]ØÛÜS˜[YOˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œ]ØÛÜT˜\ÙO™^\›˜[XÚØYÙH˜\ÙOÒ[œ]ØÛÜT˜\ÙOˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y][Û•^ÈÙ]ÈÙ]ÈHH˜[Y^\›˜[^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“XZ[•Ú[™İÈˆÚ[™İËS›İYT›Ü\PÚ[™ÙYQ]Q\œ›Ü’[™›ËS›İYQ]Q\œ›Ü’[™›È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[]Q\œ›Ü•˜[Y][Û•^›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘]Q\œ›Ü•^[ÙOUÛÕØ^K\]TÛİ\˜ÙUšYÙÙ\Q^XÚ]˜[Y]\ÓÛ‘]Q\œ›ÜœÏUYK›İYSÛ•˜[Y][Û‘\œ›ÜUYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİš[™È]Q\œ›Ü•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Q]Q\œ›Ü’[™›È™\]Z\™\È]Nˆ™Yš^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[›İYQ]Q\œ›Ü•˜[Y][Û•^›Şˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“›İYQ]Q\œ›Ü•^[ÙOUÛÕØ^K\]TÛİ\˜ÙUšYÙÙ\Q^XÚ]˜[Y]\ÓÛ“›İYQ]Q\œ›ÜœÏUYK›İYSÛ•˜[Y][Û‘\œ›ÜUYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİš[™È›İYQ]Q\œ›Ü•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ]™[]™[[™\]Q\œ›ÜœĞÚ[™ÙY]™[\™ÜÏÈ\œ›ÜœĞÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[S›İYQ]Q\œ›Ü’[™›È™\]Z\™\È›İYNˆ™Yš^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÑÜ›İ\š\œİ˜[YHÈÙ]ÈÙ]ÈHH™Ü›İ\ˆYWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÑÜ›İ\\İ˜[YHÈÙ]ÈÙ]ÈHH™Ü›İ\ˆİ™[XÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜[Y][Û•^Ú[™ÙYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[˜[Y][Û•^Ú[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜[Y][Û‘\œ›ÜYYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜[Y][Û‘\œ›Ü”™[[İ™YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[˜[Y][Û‘\œ›Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Pš[™[™ÜÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]R[œ]X[˜YÙ\œÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[İÑ›ÜW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™]šY]Ñ˜YÑ[\W“Û‘^\›˜[™]šY]Ñ˜YÑ[\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜YÑ[\W“Û‘^\›˜[˜YÑ[\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™]šY]Ñ˜YÓİ™\W“Û‘^\›˜[™]šY]Ñ˜YÓİ™\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜YÓİ™\W“Û‘^\›˜[˜YÓİ™\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™]šY]Ñ˜YÓX]™OW“Û‘^\›˜[™]šY]Ñ˜YÓX]™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜YÓX]™OW“Û‘^\›˜[˜YÓX]™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™]šY]Ñ›ÜW“Û‘^\›˜[™]šY]Ñ›Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘›ÜW“Û‘^\›˜[›Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›Q˜YÑ›Ü
Ú[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”ÜX›UÚ[™İĞXİ]˜][Û”Ù\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”›ØÙ\ÜÑ˜YÑ›Ü]™[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”›ØÙ\ÜÑ˜YÑ›Üˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™]šY]Ñ˜YÑ[\Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜YÓİ™\Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[˜YÓX]™PÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™]šY]Ñ›ÜÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[›ÜÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H˜YËY[\ˆXØÙ\YY™™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H˜YË[İ™\ˆXØÙ\YY™™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H˜YË[X]™H˜[˜XÚÈY™™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H˜YËÙ›ÜXØÙ\YY™™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H˜YËÙ›ÜÜ˜\\ˆXØÙ\YY™™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H˜YËÙ›Üš\œİš[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Pš[™[™ÑÜ›İ\
Ú[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T›İ]Y]™[ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q\[™[˜ŞT›Ü\Y\ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÓÜ\˜][ÛœË‘Ù]][Pš[™[™Ñ^™\ÜÚ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[™[™ÓÜ\˜][ÛœË‘Ù]š[Üš]Pš[™[™Ñ^™\ÜÚ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙ[š[™[™Ñ^™\ÜÚ[Û‹”\™[š[™[™Ë”™[]]™TÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[]]™TÛİ\˜ÙS[ÙK”Ù[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™TÛİ\˜ÙHÙ[ˆš[™[™È˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[˜Ù\İÜš[™[™Ñ^™\ÜÚ[Û‹”\™[š[™[™Ë”™[]]™TÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[]]™TÛİ\˜ÙS[ÙK‘š[™[˜Ù\İÜˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™TÛİ\˜ÙH[˜Ù\İÜˆš[™[™È˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[˜Ù\İÜ“]™[š[™[™Õ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[˜Ù\İÜ“]™[Lˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™TÛİ\˜ÙH[˜Ù\İÜ‹[]™[š[™[™È˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™[]]™TÛİ\˜ÙH[˜Ù\İÜ‹[]™[˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y][Û‹‘Ù]\Ñ\œ›ÜŠ˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y][Û‹‘Ù]\œ›ÜœÊ˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[˜[Y][Û‘\œ›Ü•[\]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYÜ›™Y[[Y[XÙZÛ\ˆ“˜[YOW‘^\›˜[˜[Y][Û‘\œ›Ü”XÙZÛ\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y][Û‹‘\œ›Ü•[\]OWÔİ]XÔ™\Ûİ\˜ÙH^\›˜[˜[Y][Û‘\œ›Ü•[\]_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][Ûˆ\œ›ÜˆYYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][Ûˆ\œ›ÜˆYY›İ]Y]™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][Ûˆ\œ›Üˆ™[[İ™YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][Ûˆ\œ›Üˆ™[[İ™YÙ[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]U˜[Y][Û‘\œ›Ü•[\]PY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYÜ›™\“^Y\‹‘Ù]YÜ›™\“^Y\Š˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][Ûˆ\œ›Ü•[\]HYÜ›™\ˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][Ûˆ\œ›Ü•[\]H™XÛİ™\Hİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈQ]Q\œ›Ü’[™›Èš[™[™È˜[Y]\ÓÛ‘]Q\œ›ÜœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈQ]Q\œ›Ü’[™›È˜[Y][Ûˆ\œ›ÜˆÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈQ]Q\œ›Ü’[™›È˜[Y][Ûˆ™XÛİ™\™YÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈS›İYQ]Q\œ›Ü’[™›Èš[™[™È˜[Y]\ÓÛ“›İYQ]Q\œ›ÜœÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈS›İYQ]Q\œ›Ü’[™›È˜[Y][Ûˆ\œ›ÜˆÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈS›İYQ]Q\œ›Ü’[™›È˜[Y][Ûˆ™XÛİ™\™YÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]Y]Ù‘Ù][œ]ØÛÜJ˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\[Y[œ]ØÛÜS˜[YH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ\[Y[œ]ØÛÜT˜\ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ][™İXYÙSX[˜YÙ\‹İ\œ™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ][™İXYÙSX[˜YÙ\‹‘Ù][œ][™İXYÙJ˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[œ][™İXYÙSX[˜YÙ\ˆÙ]İ\œ™[[™İXYÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]Y]Ù‘Ù]™Y™\œ™Y[YTİ]J˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[œ]Y]ÙÙ]ÛÛ™\œÚ[Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™È˜[œÙ™\ˆ›İYSÛ”Ûİ\˜ÙU\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™È˜[œÙ™\ˆ›İYSÛ•\™Ù]\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™È˜[˜XÚÈ˜[YHY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™È\™Ù][˜[YHY]Y]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™ÈÛİ\˜ÙU\]Y›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™È\™Ù]\]Y›İ]Y]™[˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜[Y][ÛˆÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^Ù\[Û•˜[Y][Û•^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ^Ù\[Û•˜[Y][Û”[HÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^Ù\[Û‘š[\•^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\]TÛİ\˜ÙQ^Ù\[Û‘š[\W“Û‘^\›˜[\]TÛİ\˜ÙQ^Ù\[Û‘š[\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİš[™È^Ù\[Û•˜[Y][Û•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİš[™È^Ù\[Û‘š[\•^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^Ù\[Ûˆ˜[Y][Ûˆ™Z™XİY˜[YKˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[^Ù\[Ûˆš[\ˆ™Z™XİY˜[YKˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^Ù\[Û•˜[Y][Û”[HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^Ù\[Ûˆ˜[Y][Ûˆ™Z™XİYÛİ\˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^Ù\[Ûˆ˜[Y][Ûˆ™XÛİ™\™YÛİ\˜ÙH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\]TÛİ\˜ÙQ^Ù\[Û‘š[\ˆØ[˜XÚÈÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\]TÛİ\˜ÙQ^Ù\[Û‘š[\ˆ˜[Y][Ûˆ\œ›ÜˆÛÛ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“\İ^\›˜[\]TÛİ\˜ÙQ^Ù\[Û‘š[\”]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜š[™[™ÑÜ›İ\•˜[Y]UÚ]İ]\]J
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜š[™[™ÑÜ›İ\ÛÛ[Z]Y]

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™ÑÜ›İ\™Z™XİYÛÛ[Z]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[™[™ÑÜ›İ\XØÙ\Yš\œİÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ›Û”˜Z\ÙQ^\›˜[X˜›J
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ\İÛHX˜›HY[™\ˆ[™[Ù[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ›Û”˜Z\ÙQ^\›˜[[›™[

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ\İÛH[›™[Y[™\ˆ[™[Ù[™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\[™[˜ŞT›Ü\R[\‹‘Ù]˜[YTÛİ\˜ÙJ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[š\š]Y]XÚY›Ü\H˜[YHÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÙ\˜ÙY\[™[˜ŞH›Ü\HÛİ\˜ÙH›YÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\[™[˜ŞH›Ü\HÚ[™ÙYØ[˜XÚÈ™]È˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û•^›Ş”Ù[Xİ
KÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û•^›Ş”Ù[XİY^HœÙ[Xİ[Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û•^›Ş\[™^
ˆ\[™YŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^›ŞÙ[XİY^™\XÙ[Y[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^›ŞY][™ÈÛİ\˜ÙH\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û•^›Ş•[™Ó[Z]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û•^›Ş•[™Ê
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û•^›Ş”™YÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^›Ş[\H[™È^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ^›Ş[\H™YÈ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İËÛÛ[X[™š[™[™ÜÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË’[œ]š[™[™ÜÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WŞ”İ]XÈØØ[“XZ[•Ú[™İË‘^\›˜[ÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊØ[‘^Xİ]OW“Û‘^\›˜[ÛÛ[X[™Ø[‘^Xİ]Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^Xİ]YW“Û‘^\›˜[ÛÛ[X[™^Xİ]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù\İ\™OWİ›
ÑWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[İ\ÙPš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù\İ\™OW“YİX›PÛXÚ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[İ\ÙPÛÛ[X[™\˜[Y]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘›Øİ\ÓX[˜YÙ\‹‘›Øİ\ÙY[[Y[WĞš[™[™È[[Y[˜[YOQ^\›˜[ÛÛ[X[™]ÛŸWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^X›Ø\™˜]šYØ][Û‹•X“˜]šYØ][ÛWŞXÛWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[XØÙ\ÜÓX™[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•\™Ù]WĞš[™[™È[[Y[˜[YOQ^\›˜[˜[Y][Û•^›ŞWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]]ÛX][Û”›Ü\Y\Ë]]ÛX][Û’YW‘^\›˜[˜[Y][Û•^›Ş]]ÛX][Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]]ÛX][Û”›Ü\Y\Ë“X™[YOWĞš[™[™È[[Y[˜[YOQ^\›˜[XØÙ\ÜÓX™[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]]ÛX][Û”›Ü\Y\Ë‘Ù]]]ÛX][Û’Y
˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]]ÛX][Û”›Ü\Y\Ë‘Ù]X™[YJ˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•RQ[[Y[]]ÛX][Û”Y\‹Ü™X]TY\‘›Ü‘[[Y[
˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•RQ[[Y[]]ÛX][Û”Y\‹Ü™X]TY\‘›Ü‘[[Y[
XØÙ\ÜÓX™[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û”Y\‹‘Ù]X™[YJ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\Ú[™ÈŞ\İ[K•Ú[™İÜË]]ÛX][Û‹”›İšY\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P]]ÛX][Û”]\›”›İšY\œÊÚ[™İËÛÛ[X[™]Û‹˜[Y][Û•^›Ş
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ[X[™]Û”Y\‹‘Ù]]\›Š]\›’[\™˜XÙK’[›ÚÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]]ÛX][Ûˆ[›ÚÙHÛÛ[X[™Ûİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Û”Y\‹‘Ù]]\›Š]\›’[\™˜XÙK•˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]]ÛX][Ûˆ˜[YH›İšY\ˆ^›Ş^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÚXÚĞ›ŞY\‹‘Ù]]\›Š]\›’[\™˜XÙK•ÙÙÛJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]]ÛX][ÛˆÙÙÛHÚXÚÙYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛY\”Y\‹‘Ù]]\›Š]\›’[\™˜XÙK”˜[™ÙU˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]]ÛX][Ûˆ˜[™ÙH›ÙÜ™\ÜÈ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[İ[™[Û™PXØÙ\ÜÕ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•^W—Ñ^\›˜[İ[™[Û™HXØÙ\Ü×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Ù^X›Ø\™˜]šYØ][Û”[™[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Ù^X›Ø\™˜]šYØ][Û‘š\œİ]Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Ù^X›Ø\™˜]šYØ][Û”ÙXÛÛ™]Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^X›Ø\™˜]šYØ][Û‹‘Ù]X“˜]šYØ][ÛŠÙ^X›Ø\™˜]šYØ][Û”[™[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]RÙ^X›Ø\™˜]šYØ][ÛY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[İ™Q›Øİ\Ê™]È˜]™\œØ[™\]Y\İ
›Øİ\Ó˜]šYØ][Û‘\™Xİ[Û‹“™^
JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙ^X›Ø\™˜]šYØ][ÛˆŞXÛY™]š[İ\È]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\Ù[][Û”Ûİ\˜ÙK‘œ›ÛUš\İX[
Ú[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXØÙ\ÜÒÙ^SX[˜YÙ\‹’\ÒÙ^T™YÚ\İ\™Y
™\Ù[][Û”Ûİ\˜ÙK‘WŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXØÙ\ÜÒÙ^SX[˜YÙ\‹”›ØÙ\ÜÒÙ^J™\Ù[][Û”Ûİ\˜ÙK‘W‹˜[ÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈXØÙ\ÜËZÙ^HX[˜YÙ\ˆ›Øİ\ÙYX™[\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛXÚÏW“Û‘^\›˜[ÛÛ[X[™]ÛÛXÚ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[™\]Y\PÛÛ[X[™]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WĞš[™[™È^\›˜[™\]Y\PÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™X[˜YÙ\‹”™\]Y\TİYÙÙ\İY
ÏH˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™X[˜YÙ\‹’[˜[Y]T™\]Y\TİYÙÙ\İY

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™\]Y\HÛÛ[X[™[˜X›Yİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ™\]Y\HÛÛ[X[™^Xİ]HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Û\ÜĞÛÛ[X[™\™Ù]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[Û\ÜĞÛÛ[X[™]Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™WŞ”İ]XÈØØ[‘^\›˜[Û\ÜĞÛÛ[X[™^›Ş‘^\›˜[Û\ÜĞÛÛ[X[™Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™\˜[Y]\W‘^\›˜[Û\ÜĞÛÛ[X[™\˜[Y]\—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈ^\›˜[Û\ÜĞÛÛ[X[™^›Şˆ^›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™X[˜YÙ\‹”™YÚ\İ\Û\ÜĞÛÛ[X[™š[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™X[˜YÙ\‹”™YÚ\İ\Û\ÜÒ[œ]š[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[Û\ÜÒ[œ]\˜[Y]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\ÜÈÛÛ[X[™^Xİ]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]PÛ\ÜÒ[œ]š[™[™ĞY\”[ŠÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]TÜX›R[œ]]™[
’Ù^QİÛ—‹Ù^Nˆ‘‹[ÙYšY\œÓ˜[YNˆÛÛ›ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[™TÜX›R[œ]
Ú[™İËÙ^QİÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\ÜÈ[œ]š[™[™ÈÙ^H]™[[™Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\ÜÈ[œ]š[™[™ÈÛÛ[X[™\˜[Y]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\ÜÈ[œ]š[™[™ÈYÛ›Ü™\ÈÙ^H\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]PÛÛ[X[™Ğ[™›Øİ\ÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İËÛÛ[X[™š[™[™ÜËÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË’[œ]š[™[™ÜËÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™U\O[İ\ÙPš[™[™Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“[İ\ÙPXİ[Û‹“YİX›PÛXÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[İ\ÙHš[™[™ÈÛÛ[X[™^Xİ]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^X›Ø\™˜]šYØ][Û‹‘Ù]\™Xİ[Û˜[˜]šYØ][ÛŠ›Øİ\Ô[™[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈX™[XØÙ\ÜËZÙ^H\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ[™[Û™HXØÙ\ÜÈ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“XZ[•Ú[™İË‘^\›˜[ÛÛ[X[™‘^Xİ]J‘\™XİÛÛ[X[™\˜[Y]\—‹ÛÛ[X[™]ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ[X[™]Û‹”˜Z\ÙQ]™[
™]È›İ]Y]™[\™ÜÊ]Û˜\ÙKÛXÚÑ]™[ÛÛ[X[™]ÛŠJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ[X[™]Û‹ÛÛ[X[™\˜[Y]\‹ÛÛ[X[™]Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]ÛˆÛÛ[X[™\˜[Y]\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\™\Ûİ\˜Ù\Ë“Y\™ÙYXİ[Û˜\šY\ËÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[œÚ\™Yœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ú\™YW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[œÚ\™Yœ\Ú^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[[œÚ\™Yœ\Ú^ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ”Ú\™YY˜[ÙHİ]XÔ™\Ûİ\˜ÙHÛÛœİ[Y\œÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ”Ú\™YY˜[ÙHXİ[Û˜\HÛÚİ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\™\Ûİ\˜Ù\Ö×‘^\›˜[[˜[ZXĞœ\Ú—HH™]ÈÛÛYÛÛÜœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\]Y[˜[ZXÈ™\Ûİ\˜ÙH›Ü™YÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\™\Ûİ\˜Ù\Ë“Y\™ÙYXİ[Û˜\šY\ËY
[[YSY\™ÙYXİ[Û˜\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[[YHY\™ÙY[˜[ZXÈ™\Ûİ\˜ÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\]Y[[YHY\™ÙY[˜[ZXÈ™\Ûİ\˜ÙH›Ü™YÜ›İ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TŞ\İ[T\˜[Y]\œÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]UÚ[™İĞÚ›ÛYJÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ú[™İĞÚ›ÛYK”Ù]Ú[™İĞÚ›ÛYJÚ[™İËÚ›ÛYJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÚ[™İĞÚ›ÛYH]XÚY˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œË‘›Øİ\Ğ›Ü™\•ÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œË”š[X\TØÜ™Y[•ÚYÙ^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œË•ÛÜšĞ\™XH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œË“Y[TÚİÑ[^H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œËÛY[\™XP[š[X][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œË‘›Ü™YÜ›İ[™›\ÚÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[T\˜[Y]\œË•ÚY[ØÜ›Û[™\È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈŞ\İ[T\˜[Y]\œËÜ›Ü\S˜[Y_H™\Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]S][˜Ú\Š
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”ÜX›S][˜Ú\”Ù\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H][˜Ú\ˆÙ\šXÙH[˜X›Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H][˜Ú\ˆ[™Y™\]Y\İ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H][˜Ú\ˆ™\]Y\İT’H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H][˜Ú\ˆ\™Ù]œ˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SY\ÜØYÙP›Ş
Ú[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”ÜX›SY\ÜØYÙP›ŞÙ\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›HY\ÜØYÙP›ŞÙ\šXÙH[˜X›Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™YÚ\İ\‘]\›Z[š\İXÓY\ÜØYÙP›Ş
Ù\šXÙU\JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÚİÑ]\›Z[š\İXÓY\ÜØYÙP›Ş‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY\ÜØYÙP›Ş›Ë[İÛ™\ˆY˜][™\İ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY\ÜØYÙP›ŞİÛ™\ˆ˜[˜XÚÈ™\İ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Qš[QX[ÙÜÊÚ[™İÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ZXÜ›ÜÛÙ•Ú[ŒÌ‹”ÜX›Qš[QX[ÙÔÙ\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›Hš[HX[ÙÈÙ\šXÙH[˜X›Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ[‘š[QX[ÙÈš[S˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİÛ™\ˆØ]™Qš[QX[ÙÈš[S˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİÛ™\ˆÜ[‘›Û\‘X[ÙÈ›Û\“˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš[HX[ÙÈ™\]Y\İÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]PÛ\›Ø\™

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\›Ø\™”Ù]^
™^\›˜[ÑÈÛ\›Ø\™^ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\›Ø\™]HØš™Xİ[šXÛÙH^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\›Ø\™İ\œ™[]HØš™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\›Ø\™”Ù]]SØš™Xİ
İ\İÛQ]SØš™XİÛÜNˆYJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÙĞİ\İÛQ›Ü›X]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\œ™[]SØš™Xİ•QÙ]]J‘^\›˜[ÙĞİ\İÛQ›Ü›X]ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\›Ø\™\Yİ\İÛH]H™]šY]˜[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\›Ø\™”Ù]š[Q›Ü\İ
š[Q›Ü\İ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\›Ø\™‘Ù]š[Q›Ü\İ

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\›Ø\™š[KY›ÜÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\›Ø\™ÛX\™Yš[KY›Üİ]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛ\›Ø\™ÛX\™Y^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[]UšYÙÙ\™Y^İ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ]UšYÙÙ\ˆš[™[™ÏWĞš[™[™È\Ñ^\›˜[]UšYÙÙ\Xİ]™_Wˆ˜[YOW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^OW‘^\›˜[][Q]UšYÙÙ\™Y^İ[Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ][Q]UšYÙÙ\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ™][Ûˆš[™[™ÏWĞš[™[™È\Ñ^\›˜[][UšYÙÙ\”™XY_Wˆ˜[YOW•YWˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ\X[Û\ÜÈXZ[•Ú[™İÈˆÚ[™İËS›İYT›Ü\PÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\Ñ^\›˜[]UšYÙÙ\Xİ]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\Ñ^\›˜[][UšYÙÙ\”™XYJJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\Ñ^\›˜[]UšYÙÙ\Xİ[ÛXİ]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\Ñ^\›˜[][Q]UšYÙÙ\Xİ[Û”™XYJJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\Ñ^\›˜[][Q]UšYÙÙ\Xİ[Û\›YY
JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈ^\›˜[][HˆS›İYT›Ü\PÚ[™ÙY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ\ĞXİ]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ü\PÚ[™ÙY
˜[Y[ÙŠ˜[YJJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]HšYÙÙ\ˆXİ]™H^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H]HšYÙÙ\ˆÛ™KXÛÛ™][Ûˆ^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H]HšYÙÙ\ˆXİ]™HYÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H]HšYÙÙ\ˆ^]^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[\]K“ØYÛÛ[

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ][H[\]H˜[YHš[™[™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË‘^\›˜[][\ËY
™]È^\›˜[][J‘Ø[[XW‹‘]WŠJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›İ[™][\ÈÛİ[Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™Hš[\™YÛÛXİ[Û•šY]ÔÛİ\˜ÙH]™H\]H][HÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË‘^\›˜[]™R][\ÖÌWK’\ĞXİ]™HHYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™HÛÜYÛÛXİ[Û•šY]ÔÛİ\˜ÙH]™H™\ÛÜš\œİ][H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË‘^\›˜[]™R][\ÖÌ—K“˜[YHH“]™HX\›Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™HÜ›İ\YÛÛXİ[Û•šY]ÔÛİ\˜ÙH]™H™YÜ›İ\œ˜[Y]ÛÜšÈÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË‘^\›˜[]™R][\ÖÌ—K’Ú[™H‘œ˜[Y]ÛÜš×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ]™HÜ›İ\YÛÛXİ[Û•šY]ÔÛİ\˜ÙH]™H™YÜ›İ\™[[İ™Y]HÜ›İ\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[š\X[^š[™Ò][\Ó\İˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\X[^š[™Ô[™[’\Õš\X[^š[™ÏW•YWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\X[^š[™Ô[™[•š\X[^˜][Û“[ÙOW”™XŞXÛ[™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•š\X[^š[™ÔİXÚÔ[™[ÜšY[][ÛW•™\XØ[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš\X[^š[™Ô[™[\Õš\X[^š[™È]XÚY˜[YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈš\X[^š[™Ô[™[š\X[^˜][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈXZ[•Ú[™İÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË‘š[™˜[YJ‘^\›˜[[YYÛÛ›ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYÛÛ›Û•[\]K‘š[™˜[YJ•[YT›Ûİ‹[YYÛÛ›Û
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYÛÛ›Û•[\]K‘š[™˜[YJ•[YU^‹[YYÛÛ›Û
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[YYÛÛ›Û[\]Pš[™[™È^‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[YYÛÛ›ÛÛÛ\Û™[™\Ûİ\˜ÙHœ\Ú‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈXœ˜\HÙ[™\šXË[[Ûİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[YÙK[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[ÙXÛÛ™YÙK[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[YÙQ[˜İ[Û‹[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J\›Ûİ‘^\›˜[˜]šYØ][Û•Ú[™İË[[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\ÜÏW‘^\›˜[ÙĞ\‘^\›˜[YÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\ÜÏW‘^\›˜[ÙĞ\‘^\›˜[ÙXÛÛ™YÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\ÜÏW‘^\›˜[ÙĞ\‘^\›˜[YÙQ[˜İ[Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ\X[Û\ÜÈ^\›˜[YÙQ[˜İ[ÛˆˆYÙQ[˜İ[Ûİš[™Ïˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÛÛœİİš[™ÈY˜][™\İ[H‘^\›˜[YÙQ[˜İ[Ûˆ™]\›—È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”™]\›Š™]È™]\›‘]™[\™ÜÏİš[™ÏŠ™\İ[ÏÈY˜][™\İ[
JNÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛ\ÜÏW‘^\›˜[ÙĞ\‘^\›˜[˜]šYØ][Û•Ú[™İ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ\X[Û\ÜÈ^\›˜[˜]šYØ][Û•Ú[™İÈˆ˜]šYØ][Û•Ú[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÚİÜÓ˜]šYØ][Û•ROW‘˜[ÙWˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[˜]šYØ][Û•Ú[™İÓ˜]šYØ][™È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[˜]šYØ][Û•Ú[™İÓ˜]šYØ]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û‘^\›˜[˜]šYØ][Û•Ú[™İÓØYÛÛ\]Y‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[˜]šYØ][Û˜XÚĞ]Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™W“˜]šYØ][ÛÛÛ[X[™Ëœ›İÜÙP˜XÚ×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜[YOW‘^\›˜[˜]šYØ][Û‘›ÜØ\™]Û—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™W“˜]šYØ][ÛÛÛ[X[™Ëœ›İÜÙQ›ÜØ\™ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ[X[™\™Ù]WĞš[™[™È[[Y[˜[YOQ^\›˜[œ˜[Y_Wˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ûİ\˜ÙOW‘^\›˜[YÙK[[ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][™ÏW“Û‘^\›˜[œ˜[YS˜]šYØ][™×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ]YW“Û‘^\›˜[œ˜[YS˜]šYØ]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØYÛÛ\]YW“Û‘^\›˜[œ˜[YSØYÛÛ\]Yˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘˜Z[‘\Ü]Ú\Š
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘\Ü]Ú\”š[Üš]K\XØ][Û’YH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•[YTÜ[‹‘œ›ÛSZ[\ÙXÛÛ™ÊL
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X\šÙ\“Ü\˜][Û‹X›Ü

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YK“˜]šYØ]J™]È\šJ‘^\›˜[ÙXÛÛ™YÙK[[‹\šRÚ[™”™[]]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YK‘ÛĞ˜XÚÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YK‘ÛÑ›ÜØ\™

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YKØ[‘ÛÑ›ÜØ\™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][ÛÛÛ[X[™Ëœ›İÜÙP˜XÚËØ[‘^Xİ]J[œ˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][ÛÛÛ[X[™Ëœ›İÜÙP˜XÚË‘^Xİ]J[œ˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][ÛÛÛ[X[™Ëœ›İÜÙQ›ÜØ\™Ø[‘^Xİ]J[œ˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][ÛÛÛ[X[™Ëœ›İÜÙQ›ÜØ\™‘^Xİ]J[œ˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆœ˜[YS˜]šYØ][Û”Ù\šXÙHHœ˜[YK“˜]šYØ][Û”Ù\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][Û”Ù\šXÙK‘Ù]˜]šYØ][Û”Ù\šXÙJœ˜[YS˜]šYØ][Û”Ù\šXÙTÙXÛÛ™YÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YS˜]šYØ][Û”Ù\šXÙK‘ÛĞ˜XÚÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YS˜]šYØ][Û”Ù\šXÙK‘ÛÑ›ÜØ\™

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆ˜]šYØ][Û•Ú[™İÈH™]È^\›˜[˜]šYØ][Û•Ú[™İÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]šYØ][Û•Ú[™İË”ÚİÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]šYØ][Û•Ú[™İË“˜]šYØ]J™]È\šJ‘^\›˜[ÙXÛÛ™YÙK[[‹\šRÚ[™”™[]]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆ˜]šYØ][Û•Ú[™İÔÙ\šXÙHH˜]šYØ][Û•Ú[™İË“˜]šYØ][Û”Ù\šXÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“˜]šYØ][Û”Ù\šXÙK‘Ù]˜]šYØ][Û”Ù\šXÙJ˜]šYØ][Û•Ú[™İÒ[š]X[YÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]šYØ][Û•Ú[™İÔÙ\šXÙK‘ÛĞ˜XÚÊ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]šYØ][Û•Ú[™İÔÙ\šXÙK‘ÛÑ›ÜØ\™

H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›˜]šYØ][Û•Ú[™İËÛÜÙJ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[œ˜[YS˜]šYØ][ÛØ[˜Ù[YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™KØ[˜Ù[HYH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YK“˜]šYØ]J™]È\šJ‘^\›˜[›ØÚÙYYÙK[[‹\šRÚ[™”™[]]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[YK“˜]šYØ]J™]È\šJ‘^\›˜[YÙQ[˜İ[Û‹[[‹\šRÚ[™”™[]]™JJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
KÛÛ[\È^\›˜[YÙQ[˜İ[ÛˆYÙQ[˜İ[ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœYÙQ[˜İ[Û‹”™]\›ˆ
ÏHÛ‘^\›˜[YÙQ[˜İ[Û”™]\›ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛ‘^\›˜[YÙQ[˜İ[Û”™]\›ŠØš™XİÙ[™\‹™]\›‘]™[\™ÜÏİš[™ÏˆJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[YÙQ[˜İ[Û”™]\›Ûİ[
ÊÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“\İ^\›˜[YÙQ[˜İ[Û”™\İ[HK”™\İ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]Y]Ù
—ÓÛ‘š[š\Úˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È™]\›‘]™[\™ÜÏİš[™ÏŠ‘^\›˜[YÙQ[˜İ[Ûˆ[[YH™\İ[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØ[˜Ù[Yœ˜[YH˜]šYØ]YÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØ[˜Ù[Y˜]šYØ][Ûˆ™]Z[™YYÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ[š]X[œ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÙXÛÛ™œ˜[YHÛÛ[\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜XÚÈœ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÜØ\™œ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ[X[™˜XÚÈœ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÛÛ[X[™›ÜØ\™œ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û”Ù\šXÙH˜XÚÈœ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û”Ù\šXÙH›ÜØ\™œ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈ[š]X[˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈÙXÛÛ™˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈ˜]šYØ][Û”Ù\šXÙHØ[ˆÛÈ˜XÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈ˜]šYØ][Û”Ù\šXÙHØ[ˆÛÈ›ÜØ\™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈ˜XÚÈ˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈ›ÜØ\™˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ˜]šYØ][Û•Ú[™İÈÛÜÙYÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈØ[˜Ù[Yœ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈYÙQ[˜İ[Ûˆœ˜[YH˜]šYØ][Ûˆ[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈYÙQ[˜İ[Ûˆ™]\›ˆ™\İ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\Y\™ÙY™\Ûİ\˜ÙHXİ[Û˜\HÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\ÛÛ\[YYÙHÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“QÙ]˜ÛÛ™šYÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÙÔXÚØYÙS^[İ]
XÚØYÙQ™YY
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\XÚØYÙY›ÑÜQ\™XİÜQ[š\›Û›Y[˜\šXX›HH”“ÑÔWÕÔ—Ô‘TPÒĞQÑQÔ“ÑÔWÑT—ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J™\XÚØYÙY›ÑÜQ\™XİÜQ[š\›Û›Y[˜\šXX›JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØØ[›ÑÜTXÚØYÙT›İ™[˜[˜ÙJˆ™\Ô›ÛİˆXÚØYÙQ™YYˆ™\XÚØYÙY›ÑÜQ\™XİÜJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØØ[XÚØYÙSX]Ú\Ô™\XÚØYÙYÛİ\˜ÙJ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Z\™Q\™XİÜJ™\XÚØYÙY›ÑÜQ\™XİÜK™^Xİ™\XÚØYÙY›ÑÔHXÚØYÙHÛİ\˜ÙWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\]Qš[TÚLMŠ™\XÚØYÙYÛİ\˜ÙT]
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØØ[ÜXÚØYÙRYHXÚØYÙHX]Ú\È^Xİ™\XÚØYÙYÛİ\˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊˆ\İš[™Ë‘\]X[Ê\ÜÙ[X›S˜[YK”›ÑÔK•Ü—‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]SØØ[Ü”XÚØYÙSX]Ú\Ğ]˜Z[X›T™\ÜÚ]ÜPZ[Ê™\Ô›ÛİXÚØYÙQ™YY
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİš[™Èİ™]]H™\ÛÛ™Qİ™]Üİ
™\Ô›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™J™\Ô›Ûİ‹™İ™]‹İ™]š[S˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J‘Õ‘UÔ“ÓÕŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ™İ™]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”]ÛÛXš[™J™\Ô›Ûİ‹™İ™]‹™İ™]ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\ÜÚ]ÜT›ÑÜP\ÜÙ[X›T]
™\Ô›Ûİ\ÜÙ[X›S˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\ÜÚ]ÜUÜ\ÜÙ[X›T]
™\Ô›Ûİ\ÜÙ[X›S˜[YJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØØ[ÜXÚØYÙRYHXÚØYÙHX]Ú\ÈÙ^XİY\ÜÙ[X›Q\ØÜš\[ÛŸH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™\ÜÚ]ÜHÔˆ˜[œÜÜØ\ÜÙ[X›S˜[Y_K™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TXÚØYÙP\ÜÙ[X›RY[]Y\ÊXÚØYÙQ™YY
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Q^\›˜[İ]]
İ]]›ÛİXÚØYÙQ™YY
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Ü™XXÚ
İš[™È\ÜÙ[X›S˜[YH[ˆ×Ü™\]Z\™Y›ÑÜT[[YP\ÜÙ[X›Y\ÊH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]XÚØYÙRY›Ü”[[YP\ÜÙ[X›J\ÜÙ[X›S˜[YJKˆ\ÜÙ[X›S˜[YKˆ›™]LŒŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Sİ]]\ÜÙ[X›SX]Ú\ÓØØ[XÚØYÙJˆİ]]›ÛİˆXÚØYÙQ™YYˆ“Xœ™UÔ‹•˜[œÜÜ‹ˆ\ÜÙ[X›S˜[YKˆ›™]LŒŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ]]Ø\ÜÙ[X›TÚ[\S˜[Y_K™X]Ú\ÈØØ[ÜXÚØYÙRYHXÚØYÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\]Tİ™X[TÚLMŠXÚØYÙTİ™X[JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Xœ™UÔ‹”ÙËÔÙÕ™\œÚ[ÛŸK›\ÙÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Ê“Xœ™UÔ‹•˜[œÜÜ‹”™\Ù[][ÛÛÜ™W‹›™]LŒ‹•Ô—ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Ê“Xœ™UÔ‹•˜[œÜÜ‹”Ş\İ[K•Ú[™İÜËÛÛ›ÛË”šX˜›Û—‹›™]LŒ‹‘XÛXWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Ê“Xœ™UÔ‹”›ÑÔW‹”›ÑÔK•Ü—‹›™]LŒ‹”›ÑÔWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Ê“Xœ™UÔ‹’[\›Ü‹”›ÑÔK•Ü‹’[\›Ü‹›™]LŒ‹”›ÑÔWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Ê”›ÑÔK‘\™Xİ‹”›ÑÔK‘\™Xİ‹›™]LŒ‹”›ÑÔWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]Ê”›ÑÔK”ØÙ[™W‹”›ÑÔK”ØÙ[™W‹›™]LŒ‹”›ÑÔWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]^XİYXÚØYÙP\ÜÙ[X›U™\œÚ[ÛŠ^Xİ][ÛŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İš[™ĞÛÛ\\™\‹“Ü™[˜[‘\]X[Ê^Xİ][Û‹”X›XÒÙ^UÚÙ[‘Ü›İ\”›ÑÔWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ™]È™\œÚ[ÛŠK
NÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ™]È™\œÚ[ÛŠLK
NÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ[X›S˜[YK‘Ù]\ÜÙ[X›S˜[YJ[\]
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^XİYÙ\ØÜš\[ÛŸH\ÜÙ[X›HÈ]™HHX›XÈÙ^HÚÙ[‹ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\]X[
^XİY\ÜÙ[X›U™\œÚ[Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXÚØYÙU\H˜[YOW“TĞZ[Ù×ˆÏˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™XYXÚØYÙQ[JXÚØYÙK”ÙËÔÙËœ›Ü×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™XYXÚØYÙQ[JXÚØYÙK\™Ù]ËÔ›ÑÔK•Ü‹”ÙË”ÜX›P›Ûİİ˜\˜Ü×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\›ÔXÚØYÙQ[T™Yš^
XÚØYÙK›X‹×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\›ÔXÚØYÙQ[T™Yš^
XÚØYÙKœ™Y‹×ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—Ô›ÑÜUÜ”ÙĞÛÜS˜]]™T[[YP\ÜÙ]È‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—“Ü[‘›ÛÚ\œˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜ[‘›ÛÚ\œ™\œÚ[ÛˆY˜][‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜ[‘›ÛÚ\œXÚØYÙH™Y™\™[˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÔHÜ[‘›ÛÚ\œXÚØYÙH™Y™\™[˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜ[‘›ÛÚ\œXÚØYÙH\[™[˜ŞH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈY˜][Z][HÜ[‘›ÛÚ\œXÚØYÙH\[™[˜ŞH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”İ’[XYÙTÚ\œˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈİ’[XYÙTÚ\œXÚØYÙH™Y™\™[˜ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈİ’[XYÙTÚ\œXÚØYÙH\[™[˜ŞH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘^\›˜[ÑÈÛ[ÚÙH]\İ›İ™[HÛˆÙ[™\˜]Y\™XİÜKZ[œ›ÜÈÜˆ\™XİÜKZ[\™Ù]Èš[\Ëˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™ÛØ˜[XÚØYÙ\Ñ›Û\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”]ÛÛXš[™JÛÜšÔ›Ûİ‹œXÚØYÙ\×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÜUÜ“X[˜YÙY™Y™\™[˜ÙT›Ûİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Xœ™UÔ‹•˜[œÜÜ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK‘\™Xİ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹’[\›Ü‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔKÛÛ\]H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•˜[œÜ[\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]˜]]™P\ÜÙ]Ø[™Y]\ÊÙÜWŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]˜]]™P\ÜÙ]Ø[™Y]\Ê™Û×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T›ÑÜRQT™[™\”İ\™˜XÙJİ]]›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİÙÚXØ[ÚY›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİÚ[™İÈ›Ü™\ˆ›Ü\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİÚ[™İÈ›Ü™\ˆY]Ù\˜[Y]\ˆÛİ[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË”™\Ú^™S[ÙHH™\Ú^™S[ÙKØ[”™\Ú^™UÚ]Üš\‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İË•Ú[™İÔİ[HHÚ[™İÔİ[K“›Û™H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][ÛˆXZ[ˆÚ[™İÈ\]Y™\Ú^™H[ÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ\XØ][ÛˆXZ[ˆÚ[™İÈ\]YÚ[™İÈİ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈÜX›H™\Ù[][ÛˆÛİ\˜ÙHÛY[\Ú^™H™]\›ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÛÛ\ÜÚ][Ûˆ™[™\ˆÙÚXØ[Ü\ÚXØ[İ\™˜XÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİ™\Ù[šY]ÜÜ™[™\ˆİ™\›ØY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİÜX›HÛİ\˜ÙHÙÚXØ[\Ú^™HŞ[˜Ú›Ûš^˜][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİ\YÙÚXØ[\Ú^™HØXÚH™]\›ˆ\H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK˜XÚÙ[™‘\Ü^TØØ[T™\ÛÛ™\ˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\Ü^TØØ[T™\ÛÛ™\Š\Ü^TØØ[T™\ÛÛ™\•\K™^\›˜[Ñ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔH˜XÚÙ[™˜]]™H\Ü^K\ØØ[H˜[˜XÚÈ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÜİ[YØ]\È\Ü^K\ØØ[H˜[˜XÚÈÈ›ÑÔH˜XÚÙ[™‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÔˆÛÛ\ÜÚ][Ûˆ™[™\ˆ[YØ]\ÈÈšY]ÜÜ™[™\ˆİ\™˜XÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ™[™\ˆÙÚXØ[Ü\ÚXØ[İ\™˜XÙH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÛÛ\ÜÚ]ÜˆØ[˜\È^[ÚY^XÚ]™[™\ˆ\™Ù]‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ\ÚXØ[™[™\ˆ[YØ]\ÈÈH™]XX›H™[™\ˆÛÜ™H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ™[™\ˆ\ÜÈšY]ÜÜ\XØ][Ûˆ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™^\›˜[ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ˜XÚÙ[™Z[™\[™[\ÚXØ[™[™\ˆ\™Ù]šY]ÜÜ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[™\”\ÜÑ[˜ÛÙ\”Ù]šY]ÜÜ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\™]Z[™YÜ“^Y\•\Ù\ÓÙÚXØ[›İ[™Ğ[™Y[]TØØ[J›ÑÜUÜ‹›ÑÜTØÙ[™K™^\›˜[Ñ×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙYYÚT™]Z[™YÜ”^[Ñš[\ÚXØ[\™Ù]
‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙYØš™Xİ™[™\‘]T™Xİ[™ÛQš[Ô\ÚXØ[\™Ù]
‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙY™][˜Tİ\\™\Ú^™RÙY\ÓÙÚXØ[İ\™˜XÙJ‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙY™][˜Hİ\\ÙÚXØ[ÜİÚY‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙY™]Z[™YÔˆQH\\‹[Y^[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙY™]Z[™YÔˆQHİÙ\‹\šYÚ^[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙYØš™Xİ™[™\‹Y]HÔˆQHİÙ\‹\šYÚ^[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙSØš™Xİ˜]Ô™Xİ[™ÛJ˜]Ú[™ĞÛÛ^™Yœ\Ú[™Xİ[™ÛJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”\Úİ\œ™[\™XİÜJ˜]]™P\ÜÙ]›Ûİ
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜTØÙ[™K”›ÑÔK”ØÙ[™K‘˜]Ú[™Õš\İX[ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜP˜XÚÙ[™”›ÑÔK˜XÚÙ[™‘ÜU^\™Q[Y[œÚ[Û—ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[[K”\œÙJÜU^\™Q[Y[œÚ[Û•\K‘[Y[œÚ[ÛŒ‘ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙJ^\™K”™XY^[×‹JH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜUÜ‹”Ş\İ[K•Ú[™İÜË“YYXK”›ÑÔKÛÛ\ÜÚ][Û‹”›ÑÜT™]Z[™YÛÛ\ÜÚ][ÛÛÛ[X[™Ú[š×ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J™\Ù[][Û‘œ˜[Y]ÛÜšË”Ş\İ[K•Ú[™İÜËÛÛ›ÛË›Ü™\—ŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J™\Ù[][ÛÛÜ™K”Ş\İ[K•Ú[™İÜË“YYXK”ÛÛYÛÛÜœ\ÚŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\JÚ[™İÜĞ˜\ÙK”Ş\İ[K•Ú[™İÜË”™XİŠH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]T™YÜ•š\İX[
™\Ù[][ÛÛÜ™K™\Ù[][Û‘œ˜[Y]ÛÜšËÚ[™İÜĞ˜\ÙJH‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙJ\™Ù]”™\^Uš\İX[İX™YW‹Ü•š\İX[Ú[šË[[
H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔH™]Z[™YÔˆ^Y\ˆÙÚXØ[Ú^™H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔH™]Z[™YÔˆ^Y\ˆY[]HØØ[H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\›Ü\QÙ]\”™Y™\™[˜Ù\ÑšY[‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Y]ÙØ[ÓY]Ù‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Y]ÙØ[ÔÜXÚYšXÓY]Ù‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–×œ›Ûİ‹›ÙÚXØ[ÚY‹›ÙÚXØ[ZYÚ‹œ™[™\•\™Ù]ÚY‹œ™[™\•\™Ù]ZYÚ‹™TØØ[W‹\™Ù]šY]×—H‹^\›˜[ÙÒ\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]T›ÑÜRQT™[™\”İ\™˜XÙJ[œ]ÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİÙÚXØ[ÚY›Ü\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİÚ[™İÈ›Ü™\ˆ›Ü\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİÚ[™İÈ›Ü™\ˆY]Ù\˜[Y]\ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈXİ[ÛØš™XİØš™XİØš™XİŠ™XÛÜ™\‹”Ù]Ú[™İĞ›Ü™\ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]È[˜ÏØš™Xİ[Š™XÛÜ™\‹‘Ù][™JH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[ˆÙ][™JØš™XİXİ]˜][ÛŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]›Ü\J\YXİ]˜][Û‹”™\Ù[][Û”Ûİ\˜ÙK’[™WŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ]˜]YÑÈÚ[™İÈ]™H™\Ú^™H[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ]˜]YÑÈÚ[™İÈ]™HÚ[™İÈİ[H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜX›HXİ]˜][Ûˆ™XÛÜ™\ˆÚ[™İÈ›Ü™\ˆ\™Ù]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÜX›H™\Ù[][ÛˆÛİ\˜ÙHÛY[\Ú^™H™]\›ˆ\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÛÛ\ÜÚ][Ûˆ™[™\ˆÙÚXØ[Ü\ÚXØ[İ\™˜XÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİ™\Ù[šY]ÜÜ™[™\ˆİ™\›ØY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİÜX›HÛİ\˜ÙHÙÚXØ[\Ú^™HŞ[˜Ú›Ûš^˜][Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİ\YÙÚXØ[\Ú^™HØXÚH™]\›ˆ\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK˜XÚÙ[™‘\Ü^TØØ[T™\ÛÛ™\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\Ü^TØØ[T™\ÛÛ™\Š\Ü^TØØ[T™\ÛÛ™\•\K”Ñ×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔH˜XÚÙ[™˜]]™H\Ü^K\ØØ[H˜[˜XÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÜİ[YØ]\È\Ü^K\ØØ[H˜[˜XÚÈÈ›ÑÔH˜XÚÙ[™‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÔˆÛÛ\ÜÚ][Ûˆ™[™\ˆ[YØ]\ÈÈšY]ÜÜ™[™\ˆİ\™˜XÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ™[™\ˆÙÚXØ[Ü\ÚXØ[İ\™˜XÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÛÛ\ÜÚ]ÜˆØ[˜\È^[ÚY^XÚ]™[™\ˆ\™Ù]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ\ÚXØ[™[™\ˆ[YØ]\ÈÈH™]XX›H™[™\ˆÛÜ™H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ™[™\ˆ\ÜÈšY]ÜÜ\XØ][Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›ÑÔHÛÛ\ÜÚ]Üˆ˜XÚÙ[™Z[™\[™[\ÚXØ[™[™\ˆ\™Ù]šY]ÜÜ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[™\”\ÜÑ[˜ÛÙ\”Ù]šY]ÜÜ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\™]Z[™YÜ“^Y\•\Ù\ÓÙÚXØ[›İ[™Ğ[™Y[]TØØ[J›ÑÜUÜ‹›ÑÜTØÙ[™K”Ñ×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙYYÚT™]Z[™YÜ”^[Ñš[\ÚXØ[\™Ù]
‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙYØš™Xİ™[™\‘]T™Xİ[™ÛQš[Ô\ÚXØ[\™Ù]
‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙYYØXŞT™[™\“İ™\›ØYš[Ô\ÚXØ[\™Ù]
‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\XÚØYÙY™][˜Tİ\\™\Ú^™RÙY\ÓÙÚXØ[İ\™˜XÙJ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙY™][˜Hİ\\ÙÚXØ[ÜİÚY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙY™]Z[™YÔˆQH\\‹[Y^[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙY™]Z[™YÔˆQHİÙ\‹\šYÚ^[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙYØš™Xİ™[™\‹Y]HÔˆQHİÙ\‹\šYÚ^[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœXÚØYÙYYØXŞHÔˆQHİÙ\‹\šYÚ^[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙSØš™Xİ˜]Ô™Xİ[™ÛJ˜]Ú[™ĞÛÛ^™Yœ\Ú[™Xİ[™ÛJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”\Úİ\œ™[\™XİÜJ˜]]™P\ÜÙ]›Ûİ
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜTØÙ[™K”›ÑÔK”ØÙ[™K‘˜]Ú[™Õš\İX[ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜP˜XÚÙ[™”›ÑÔK˜XÚÙ[™‘ÜU^\™Q[Y[œÚ[Û—ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[[K”\œÙJÜU^\™Q[Y[œÚ[Û•\K‘[Y[œÚ[ÛŒ‘ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙJ^\™K”™XY^[×‹JH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜU™XİÜ‹”›ÑÔK•™XİÜ‹”ÛÛYÛÛÜœ\ÚŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J›ÑÜUÜ‹”Ş\İ[K•Ú[™İÜË“YYXK”›ÑÔKÛÛ\ÜÚ][Û‹”›ÑÜT™]Z[™YÛÛ\ÜÚ][ÛÛÛ[X[™Ú[š×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J™\Ù[][Û‘œ˜[Y]ÛÜšË”Ş\İ[K•Ú[™İÜËÛÛ›ÛË›Ü™\—ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\J™\Ù[][ÛÛÜ™K”Ş\İ[K•Ú[™İÜË“YYXK”ÛÛYÛÛÜœ\ÚŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]™\]Z\™Y\JÚ[™İÜĞ˜\ÙK”Ş\İ[K•Ú[™İÜË”™XİŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÜ™X]T™YÜ•š\İX[
™\Ù[][ÛÛÜ™K™\Ù[][Û‘œ˜[Y]ÛÜšËÚ[™İÜĞ˜\ÙJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙJ\™Ù]”™\^Uš\İX[İX™YW‹Ü•š\İX[Ú[šË[[
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]›Ü\JÙ]›Ü\J\™Ù]”›Ûİš\İX[ŠKÛÛ^ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔH™]Z[™YÔˆ^Y\ˆÙÚXØ[Ú^™H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔH™]Z[™YÔˆ^Y\ˆY[]HØØ[H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\›Ü\QÙ]\”™Y™\™[˜Ù\ÑšY[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Y]ÙØ[ÓY]Ù‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Y]ÙØ[ÔÜXÚYšXÓY]Ù‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–×›ÙÚXØ[ÚY‹›ÙÚXØ[ZYÚ‹œ^[ÚY‹œ^[ZYÚ‹™TØØ[W‹\™Ù]šY]×—H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›PÛ\›Ø\™Ù\šXÙU\S˜[YHH”Ş\İ[K•Ú[™İÜË”ÜX›PÛ\›Ø\™Ù\šXÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›Qš[QX[ÙÔÙ\šXÙU\S˜[YHH“ZXÜ›ÜÛÙ•Ú[ŒÌ‹”ÜX›Qš[QX[ÙÔÙ\šXÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›SY\ÜØYÙP›ŞÙ\šXÙU\S˜[YHH”Ş\İ[K•Ú[™İÜË”ÜX›SY\ÜØYÙP›ŞÙ\šXÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›TŞ\İ[T\˜[Y]\œÊ™\Ù[][Û‘œ˜[Y]ÛÜšË\
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›UÚ[™İĞÚ›ÛYJ™\Ù[][Û‘œ˜[Y]ÛÜšËÚ[™İÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”Ú[•Ú[™İĞÚ›ÛYH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÑÈÚ[™İĞÚ›ÛYH]XÚY˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”Ş\İ[T\˜[Y]\œÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÑÈŞ\İ[T\˜[Y]\œËÜ›Ü\S˜[Y_H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÑÈŞ\İ[T\˜[Y]\œËÜ›Ü\S˜[Y_H™\Ûİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”š[X\TØÜ™Y[•ÚY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\ÜX›TŞ\İ[T\˜[Y]\”™Xİ
Ş\İ[T\˜[Y]\œÕ\K™\Ûİ\˜ÙSİÛ™\‹•ÛÜšĞ\™XWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—“Y[TÚİÑ[^W‹‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÛY[\™XP[š[X][Û—‹˜[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—‘›Ü™YÜ›İ[™›\ÚÛİ[‹È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•ÚY[ØÜ›Û[™\È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\ÛÛ™TŞ\İ[T\˜[Y]\”™\Ûİ\˜ÙJŞ\İ[T\˜[Y]\œÕ\K™\Ûİ\˜ÙSİÛ™\‹›Ü\S˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›PÛ\›Ø\™
™\Ù[][ÛÛÜ™JH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›Qš[QX[ÙÜÊ™\Ù[][Û‘œ˜[Y]ÛÜšÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™YÚ\İ\”ÜX›SY\ÜØYÙP›Ş
™\Ù[][Û‘œ˜[Y]ÛÜšÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›SY\ÜØYÙP›Ş
™\Ù[][Û‘œ˜[Y]ÛÜšËÚ[™İÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÛ\›Ø\™ÑÈ]HØš™Xİ[šXÛÙH^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›İÛ™\”™Yš^HİÛ™\ˆ\È[È››Ë[İÛ™\—ˆˆ›İÛ™\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÑÈÛİÛ™\”™Yš^HØ]™Qš[QX[ÙÈš[S˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HÑÈÛİÛ™\”™Yš^HÜ[‘›Û\‘X[ÙÈ›Û\“˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HY\ÜØYÙP›ŞÑÈ›Ë[İÛ™\ˆY˜][™\İ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÜX›HY\ÜØYÙP›ŞÑÈİÛ™\ˆ˜[˜XÚÈ™\İ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛX\”ÜX›TÙ\šXÙJ™\Ù[][Û‘œ˜[Y]ÛÜšËÜX›SY\ÜØYÙP›ŞÙ\šXÙU\S˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛX\”ÜX›TÙ\šXÙJ™\Ù[][Û‘œ˜[Y]ÛÜšËÜX›Qš[QX[ÙÔÙ\šXÙU\S˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛX\”ÜX›TÙ\šXÙJ™\Ù[][ÛÛÜ™KÜX›PÛ\›Ø\™Ù\šXÙU\S˜[YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\]X[
“XZ[•Ú[™İË[[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™™\Ûİ\˜ÙW‹”Û[ÚÙPXØÙ[œ\Úˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™™\Ûİ\˜ÙW‹“Y\™ÙYXØÙ[œ\Úˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™™\Ûİ\˜ÙW‹•[œÚ\™YXØÙ[œ\Úˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆ”Ú\™YY˜[ÙH™\Ûİ\˜ÙH[œİ[˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™™\Ûİ\˜ÙW‹”Û[ÚÙT[™[X\™Ú[—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Qš[™™\Ûİ\˜ÙW‹”›İšY\‘Ü™Y][™×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][ÛˆØš™Xİ]H›İšY\ˆ™\İ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Qœ™Y^˜X›Pœ\Ú™\Ûİ\˜ÙJ\
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]Qœ™Y^˜X›QÜ˜YY[œ\Ú™\Ûİ\˜ÙJ\
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈœ™Y^˜X›Hœ\ÚÛÛ™H]]X›HÜXÚ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈœ™Y^˜X›HÜ˜YY[ÛÛ™H]]X›HİÜÙ™œÙ]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈœ™Y^˜X›HÜ˜YY[İ\œ™[]˜[YHÛÛ™HİÜÛÛXİ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÙÓÛÜÙV[[™XY\•Üš]\Š™\Ù[][Û‘œ˜[Y]ÛÜšË™\Ù[][ÛÛÜ™JH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË“X\šİ\–[[™XY\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË“X\šİ\–[[Üš]\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØY\XØ][ÛÛÛ\Û™[
Øš™XİÛÛ^Øš™Xİİš[™ÈÛÛ\Û™[\šJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹“ØYÛÛ\Û™[ÑÈXœ˜\H[™[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\XØ][Û‹“ØYÛÛ\Û™[ÑÈXœ˜\HYÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\Hœ˜[YHÛİ\˜ÙHÛÛ\Û™[]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\Hœ˜[YHYÙH™\Ûİ\˜ÙH›Ü™YÜ›İ[™ÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÛÜÙH[[™XY\ˆİ[Hİ]XÔ™\Ûİ\˜ÙHœ\Ú‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÛÜÙH[[™XY\ˆš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÙÓÛÜÙR[œ]ØÛÜU^›Ş‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œ]ØÛÜS˜[YO‘[XZ[\Ù\“˜[YOÒ[œ]ØÛÜS˜[YOˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œ]ØÛÜT˜\ÙOœÙÈÛÜÙH˜\ÙOÒ[œ]ØÛÜT˜\ÙOˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—œÙË[ÛÜÙKZ[œ]\ØÛÜWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”ÑÈÛÜÙH[[™XY\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]ØÛÜS˜[YH˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]ØÛÜT˜\ÙH^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈÛÜÙH[[Üš]\ˆÙ\šX[^™YÜ˜YY[İÜ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÙÓÛÜÙQÜ˜YY[İÜ
Ù]ÛÛXİ[Û’][J›İ[™š\YİÜËJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û”[“Y™][YJ\
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][ÛˆÑÈİ]]İX\™ÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û’[š]X[Y™][YTİ]J\
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û”Ú]İÛ“Y™][YTİ]J\
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹İ\œ™[™Y›Ü™H[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹”Ú]İÛ“[ÙH™Y›Ü™H[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹•Ú[™İÜÈ™Y›Ü™H[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹“XZ[•Ú[™İÈ™Y›Ü™H[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹İ\œ™[\š[™È[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹•Ú[™İÜÈİ\\Ú[™İÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹İ\œ™[Y\ˆÚ]İÛˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ\XØ][Û‹•Ú[™İÜÈY\ˆÚ]İÛˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆİ\\]™[[š]X[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆİ\\]™[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆİ\\\™ÜÈ[™İ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆ^]]™[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆ^]ÛÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆİ\\[š™XİYœ\Ú‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆİ\\[š™XİY^™\Ûİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\]X[
ˆÑ‘ŒÍM‘QWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØYYİÜX›Ø\™^›ØÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØYYİÜX›Ø\™]™[šYÙÙ\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØYYİÜX›Ø\™İX›P[š[X][Ûˆ\™Ù]˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ØYYİÜX›Ø\™ÜİSØYYÜXÚ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈØYYİÜX›Ø\™[™\ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]P\XØ][Û‘[˜[ZXÔ™\Ûİ\˜ÙR[˜[Y][Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙ]Ò][W‹”Û[ÚÙPXØÙ[œ\Úˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙ]Ò][W‹“Y\™ÙYXØÙ[œ\Úˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Y\ÜØYÙH[˜[ZXÈ™\Ûİ\˜ÙH\]YÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[Ûˆ]Ûˆ[˜[ZXÈ™\Ûİ\˜ÙH\]YÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\ÜÚYÛ˜X›UÊÚ[™İË”Ş\İ[K•Ú[™İÜË•Ú[™İ×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“Y\ÜØYÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”›Ûİ[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”İ\\™\Ûİ\˜ÙU^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİ\\™\Ûİ\˜ÙH›Ü™YÜ›İ[™ÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹Xİ[Û]Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛÛ[X[™]Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ú[™İËÛÛ[X[™š[™[™ÜÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]›Ü\JÚ[™İË’[œ]š[™[™Ü×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ[œ]š[™[™ÈÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈÙ^Hš[™[™ÈÛÛ[X[™\˜[Y]\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ[İ\ÙHš[™[™ÈÛÛ[X[™\˜[Y]\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ[İ\ÙHš[™[™ÈÙ\İ\™HXİ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ[İ\ÙHš[™[™ÈÛÛ[X[™^Xİ]Y\˜[Y]\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ[X[™]Ûˆ›İ]YÛÛ[X[™Ø[‘^Xİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ›İ]YÛÛ[X[™^Xİ][ÛˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”™\]Y\PÛÛ[X[™]Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ™\]Y\HÛÛ[X[™\ØX›Yİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ™\]Y\HÛÛ[X[™[˜X›Yİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ™\]Y\HÛÛ[X[™^Xİ]HÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘]™[Ù]\]Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ù]\ˆ]Ûˆİ[H\™Ù]\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË‘]™[Ù]\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ù]\ˆ]Ûˆ›İ]Y]™[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ù]\ˆÛXÚÈÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ù]\ˆ›İ]Y]™[˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[Ûˆ]ÛˆÛÛ\‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[ÛˆÛÛ\ÛÛ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[ÛˆÛÛ\XÙ[Y[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[Ûˆ]ÛˆÛÛ^Y[H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[ÛˆÛÛ^Y[HÜ[™Y›İYÚÜX›HÜ\‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[ÛˆÛÛ^Y[HÛÜÙY›İYÚÜX›HÜ\‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[ÛˆÛÛ^Y[H][HÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ^ÛÛ[X[™Y[H][H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ^Y[HÛÛ[X[™^Xİ][ÛˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ^Y[HÛÛ[X[™^[ØYØœÙ\™Y‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[ÛˆÛÛ^Y[HÙ\\˜]Üˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ^ÚXÚØX›HY[H][HÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ^ÚXÚØX›HY[H][H[˜ÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙSY[Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH›ÛİY[H][HÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHY[HÙ\\˜]Üˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛÛ[X[™Y[R][Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ[X[™Y[H›İ]YÛÛ[X[™Ø[‘^Xİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ›İ]YÛÛ[X[™Y[H\˜[Y]\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“Y[Tİ]\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛXÚÓY[R][Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”˜Z\ÙT›İ]Y]™[
ÛXÚÓY[R][KÛXÚÑ]™[ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈY[HÛXÚÈÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÚXÚØX›SY[R][Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù]›Ü\JÚXÚØX›SY[R][K’\ĞÚXÚÙY‹˜[ÙJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÚXÚØX›HY[H][HÙÙÛY[˜ÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈY[H[˜ÚXÚÙYÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù]›Ü\JÚXÚØX›SY[R][K’\ĞÚXÚÙY‹YJH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÚXÚØX›HY[H][HÙÙÛYÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈY[HÚXÚÙYÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÚXÚĞÚÚXÙT[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“X[˜YÙYÚXÚĞ›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙYÚXÚÈ›Ş[˜ÚXÚÙYHÛXÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙYÚXÚÈ›ŞÚXÚÙYHÛXÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“X[˜YÙY˜Y[Ğ[Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“X[˜YÙY˜Y[Ğ™]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙY˜Y[È[H[˜ÚXÚÙYY\ˆ™]HÛXÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙY˜Y[È™]HÚXÚÙYHÛXÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙY˜Y[È™]H\İÚXÚÙY˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙY˜Y[È[H™XÚXÚÙYHÛXÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙY˜Y[È™]H[˜ÚXÚÙYY\ˆ[HÛXÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›X[˜YÙY˜Y[È[H\İÚXÚÙY˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÚY˜Z\ÙT›İ]Y]™[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[Ûˆ]ÛˆÛÛ›Û[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Xİ[Ûˆ]Ûˆš\İX[İ]HÜ›İ\Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ›Ü\HšYÙÙ\ˆ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HšYÙÙ\ˆ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›][HšYÙÙ\ˆ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›][H]HšYÙÙ\ˆ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹˜\ÙYÛ”™\Ûİ\˜ÙU^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜˜\ÙY[Ûˆ™\Ûİ\˜ÙH[š\š]Y›ÛÙZYÚ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”›İšY\‘Ü™Y][™Õ^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ›İšY\ˆÜ™Y][™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™™\Ûİ\˜ÙW‹–[Û[ÚÙQ]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈS]H›İšY\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹–[›İšY\•^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–S›İšY\ˆ]š[™[™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•[œÚ\™Yœ\Ú›Ü™\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[œÚ\™Y›Ü™\ˆœ\ÚÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’[œ]›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—–ÌNXK^—J×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—œÙËZ[œ]\ØÛÜWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—”ÑÈÛÛ\[YSSˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹XØÙ\ÜÒÙ^Q›Øİ\Ô[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜XØÙ\ÜÈÙ^H›Øİ\ÈØÛÜH›YÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜XØÙ\ÜÈÙ^H›Øİ\È[š]X[›Øİ\ÙY[[Y[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË’[œ]’Ù^X›Ø\™˜]šYØ][Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜XØÙ\ÜÈÙ^HXˆ˜]šYØ][Ûˆ[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜XØÙ\ÜÈÙ^HÛÛ›ÛXˆ˜]šYØ][Ûˆ[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜XØÙ\ÜÈÙ^H\™Xİ[Û˜[˜]šYØ][Ûˆ[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’[œ]XØÙ\ÜÓX™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[œ]XØÙ\ÜÈX™[\™Ù]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”İ[™[Û™PXØÙ\ÜÕ^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİ[™[Û™HXØÙ\ÜÈ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÙÑ›Øİ\Ğ[™XØÙ\ÜÒÙ^PY\”[ŠÜ™\Ù[][ÛÛÜ™K\YXİ]˜][Û‹•Ú[™İÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ^›ŞÙ^X›Ø\™‘›Øİ\È™]\›ˆ˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈ›Øİ\ÓX[˜YÙ\ˆ]™HÙÚXØ[›Øİ\È\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈXØÙ\ÜËZÙ^HX[˜YÙ\ˆ™YÚ\İ\™YX™[Ù^H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈXØÙ\ÜËZÙ^HX[˜YÙ\ˆ›Øİ\ÙYX™[\™Ù]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹[˜Ù\İÜš[™[™Ğ›Ü™\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[˜Ù\İÜˆš[™[™È›Ü™\ˆYÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹[˜Ù\İÜš[™[™Õ^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[˜Ù\İÜˆš[™[™È™[]]™K\Ûİ\˜ÙH[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[˜Ù\İÜˆš[™[™È™\ÛÛ™Y^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]\ÜÙ[X›Qœ›ÛPÛÛ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“]]X›Tİ]\Õ^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›]]X›Hİ]\È[š]X[š[™[™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“]]X›Tİ]\×‹\]Yš[™[™Èİ]\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›]]X›Hİ]\È›Ü\HÚ[™ÙYš[™[™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•˜[Y]Y[œ]›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y]Y[œ]›Ş[š]X[^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜËÛÛ›ÛË•˜[Y][Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y]Y[œ][\H˜[Y][Ûˆİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y][Ûˆİ]\È[\H^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y]Y[œ]™Z™XİYÛİ\˜ÙH\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y]Y[œ]ÛÜœ™XİY˜[Y][Ûˆİ]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜[Y]Y[œ]ÛÜœ™XİYÛİ\˜ÙH\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹Ü™Y[X[›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ü™Y[X[\ÜİÛÜ™›ŞÙXİ\™H\ÜİÛÜ™[™İ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ü™Y[X[\ÜİÛÜ™›ŞÚ[™ÙYÙ[™\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ü™Y[X[\ÜİÛÜ™›Ş›İ]Y]™[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ü™Y[X[\ÜİÛÜ™›ŞÛX\™YÙXİ\™H\ÜİÛÜ™[™İ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹Ø[[™\”Û[ÚÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø[[™\ˆÛ[ÚÙHÙ[XİY]HÛÛXİ[Ûˆ][H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø[[™\ˆÛ[ÚÙH\]YÙ[XİY]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø[[™\ˆÛ[ÚÙHÙ[Xİ[ÛˆÚ[™ÙYÙ[™\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘]TXÚÙ\”Û[ÚÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HXÚÙ\ˆÛ[ÚÙHÙ[XİY]H›Ü›X]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HXÚÙ\ˆÛ[ÚÙH\]YÙ[XİY]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HXÚÙ\ˆÛ[ÚÙHÙ[Xİ[ÛˆÚ[™ÙYÙ[™\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ›ÚY\ÜÙ\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”›İ]Y]™[Ûİ\˜ÙWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”˜Z\ÙTÛ[ÚÙPX˜›Y‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\İÛH›İ]Y]™[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\İÛH›İ]Y]™[X˜›YÙ[™\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\İÛH›İ]Y]™[ÜšYÚ[˜[Ûİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\İÛH›İ]Y]™[İ]\È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’][\Ó\İˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙTİ]\Ğ˜\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHİ]\È˜\ˆ][HÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”İ]\Ô™XYR][Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİ]\È™XYH][HÛÛ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”İ]\Õ^›ØÚ×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœİ]\ÈÙ[XİY][H^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’][\ĞÛİ[^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[š]X[][\ÈÛİ[š[™[™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”[™[][\ĞÛÛ›Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ[™[][\È[\›˜][ÛˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ[™[][\Èİš[™È›Ü›X]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ[™[][\ÈÛÛZ[™\ˆİ[HÙ]\ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ[™[][\È[™[[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ[™[][\È[™[ÜšY[][Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙS\İšY]×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH\İšY]ÈÜšYšY]È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH\İšY]È˜[YHš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH\İšY]È˜[YHš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“\İšY]Ôİ]\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›\İšY]È[š]X[Ù[XİY^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH\İšY]ÈÚ[™ÙYÙ[XİY][H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›\İšY]ÈÚ[™ÙYÙ[XİY^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“][Pš[™[™Ôİ[[X\U^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›][Hš[™[™ÈÛÛ™\\ˆ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”š[Üš]Pš[™[™Õ^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš[Üš]Hš[™[™È˜[˜XÚÈ^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]š[Üš]Pš[™[™Ñ^™\ÜÚ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš[Üš]Hš[™[™È^™\ÜÚ[ÛˆÚ[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš[Üš]Hš[™[™ÈXİ]™H]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Ù[XİY][T™\Ù[\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË‘]U[\]RÙ^H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\XÚ]][H]H[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\XÚ]][H[\]Hš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’[\XÚ]][T™\Ù[\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\XÚ]][H[\]H™\ÛÛ™Y^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’[\XÚ]İ[T[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’[\XÚ]İ[Y^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\XÚ]^İ[H\™Ù]\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\XÚ]İ[Y^›Ü™YÜ›İ[™ÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“^[İ]ÜšYˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›^[İ]ÜšY›İÈYš[š][ÛˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“^[İ]X™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›^[İ]X™[ÜšYÛÛ[[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛÛ™\YÙ[XİY][U^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ™\YÙ[XİY][H^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘›Ü›X]Y[œ]^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™›Ü›X]Y[œ]š[™[™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]ÛÛ[[”Ü[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘ØÚÓ^[İ][™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™ØÚÈ^[İ]\İÚ[š[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™ØÚÈ^[İ]ÜØÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™ØÚÈ^[İ]YØÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™ØÚÈš[š[™[™È^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹Ø[˜\Ó^[İ][™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø[˜\ÈÜÚ][Û™YY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø[˜\ÈÜÚ][Û™YÜ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•[šY›Ü›S^[İ][™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[šY›Ü›H^[İ]›İÜÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[šY›Ü›H^[İ]ÛÛ[[œÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[šY›Ü›HÙ[™YH^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™™\Ûİ\˜ÙW‹‘Ü›İ\Y][\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ü›İ\Y][\ÈÜ›İ\\ØÜš\[ÛˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ü›İ\Y][\ÈšY]ÈÜ›İ\Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘Ü›İ\Y][\ĞÛÛ›Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ü›İ\Y][\ÈÜ›İ\XY\ˆ[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Ù[XİÜ’][\ĞÛÛ›Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH][H[\]HÙ[XİÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™™\Ûİ\˜ÙW‹”Û[ÚÙQœ˜[Y]ÛÜšÒ][U[\]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™™\Ûİ\˜ÙW‹”Û[ÚÙT™[™\š[™Ò][U[\]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™œ˜[Y]ÛÜšÈ][HÙ[XİY[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™[™\š[™È][HÙ[XİY[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙPÛÛX›Ğ›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHÛÛX›È›Ş[š]X[Ù[XİY˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHÛÛX›È›ŞÚ[™ÙYÙ[XİY˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙ[XİÜˆÙ[Xİ[ÛˆÚ[™ÙYÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙ[XİÜˆİ]\ÈY\ˆÛÛX›ÈÙ[Xİ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙUXœ×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHXˆ[š]X[Ù[XİY[™^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘œ˜[Y]ÛÜšÕX—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”™[™\š[™ÕX—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHXˆÚ[™ÙYÙ[XİY][H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXˆÙ[Xİ[ÛˆÚ[™ÙYÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊXˆİ]\ÈY\ˆXˆÙ[Xİ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙUÛÛ˜\•˜^Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHÛÛ˜\ˆ˜^HÛÛ˜\ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙUÛÛ˜\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHÛÛ˜\ˆXY\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•ÛÛ˜\ÛÛ[X[™]Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ˜\ˆÛÛ[X[™^Xİ][ÛˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ˜\ˆÛÛ[X[™^[ØYØœÙ\™Y‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•ÛÛ˜\”Ù\\˜]Ü—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•ÛÛ˜\•ÙÙÛWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ˜\ˆÙÙÛHÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ˜\ˆÙÙÛH[˜ÚXÚÙY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙQÜ›İ\›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHÜ›İ\›ŞXY\ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙQ^[™\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH^[™\ˆÛÛ\ÙYÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH^[™\ˆ^[™YÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙTØÜ›ÛšY]Ù\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHØÜ›ÛšY]Ù\ˆ™\XØ[š\ÚXš[]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”ØÜ›ÛÛÛ[[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœØÜ›ÛÛÛ[Ú[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙTÛY\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙHÛY\ˆÚ[™ÙY˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙT›ÙÜ™\ÜĞ˜\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH›ÙÜ™\ÜÈÚ[™ÙY›İ[™˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ˜[™ÙH˜[YHÚ[™ÙYÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ˜[™ÙHİ]\ÈY\ˆÛY\ˆ˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙQ]QÜšYˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH]HÜšYÛÛ[[ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH]HÜšY˜[YHš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH]HÜšYØ]YÛÜHš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH]HÜšYXİ]™Hš[™[™È]‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘]QÜšYİ]\×ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HÜšY[š]X[Ù[XİY^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛ[ÚÙH]HÜšYÚ[™ÙYÙ[XİY][H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HÜšYÚ[™ÙYÙ[XİY^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]HÜšY][\ÈÛİ[Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš][\È\İÛİ[Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ[™[][\ÈÛİ[Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›\İšY]ÈÛİ[Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÙ[XİÜˆ][\ÈÛİ[Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš][\ÈÛİ[š[™[™È^Y\ˆÛÛXİ[ÛˆÚ[™ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™[˜[ZXÈœ˜[Y]ÛÜšÈ][HÙ[XİY[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹’Y\˜\˜ÚU™YWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšY\˜\˜ÚH™YH›Ûİ][HÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË’Y\˜\˜ÚXØ[]U[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšY\˜\˜ÚH][HÛİ\˜ÙHš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšY\˜\˜ÚHš\œİ][HÚ[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšY\˜\˜ÚHš\œİÚ[˜[YH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛÛ\[YÛ[ÚÙT[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Y\Ù\ˆÛÛ›Û\[™[˜ŞH›Ü\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Y\Ù\ˆÛÛ›Û›İ[™\[™[˜ŞH›Ü\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”[™[Ø\[Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Y\Ù\ˆÛÛ›Û[[Y[[˜[YHš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”[™[™[]]™PØ\[Û—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Y\Ù\ˆÛÛ›Û™[]]™K\Ûİ\˜ÙHš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”[™[ÛÛ[™\Ù[\—ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Y\Ù\ˆÛÛ›ÛÛÛ[š[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛÛ\[YXœ˜\T[™[ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙÔİÚ]ÚXœ˜\K“Xœ˜\T[™[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H\Ù\ˆÛÛ›Û\[™[˜ŞH›Ü\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[[Y[[˜[YH]Hš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[[Y[[˜[YHYÈš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\HSS^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H™\Ûİ\˜ÙHœ\ÚÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆ™Y™\™[˜ÙYXœ˜\HY\™ÙYXØÙ[œ\ÚÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆ™Y™\™[˜ÙYXœ˜\HY\™ÙYİš[™È™\Ûİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\XØ][Ûˆ™Y™\™[˜ÙYXœ˜\HY\™ÙYY[™ÈY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“Xœ˜\SY\™ÙY™\Ûİ\˜ÙU^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Y™\™[˜ÙYXœ˜\HY\™ÙY›Ü™YÜ›İ[™ÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™Y™\™[˜ÙYXœ˜\HY\™ÙYY[™È›İÛH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹ÛÛ\[YXœ˜\U[YYÛÛ›Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙÔİÚ]ÚXœ˜\K“Xœ˜\U[YYÛÛ›Û‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[YYÛÛ›ÛY˜][[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“Xœ˜\U[YU^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[YYÛÛ›Û[\]Hš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹“Xœ˜\U[YT›Ûİˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[YYÛÛ›Û˜XÚÙÜ›İ[™ÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[YYÛÛ›ÛÛÛ\Û™[™\Ûİ\˜ÙHÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÑÈXœ˜\H[YYÛÛ›Û›Ü™\ˆXÚÛ™\ÜÈY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•[YYÛ[ÚÙPÛÛ›Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYİ\İÛHÛÛ›ÛY˜][[\]H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•[YU^ˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYİ\İÛHÛÛ›Û[\]Hš[™[™È‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹•[YT›Ûİˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYİ\İÛHÛÛ›Û˜XÚÙÜ›İ[™ÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYİ\İÛHÛÛ›ÛÛÛ\Û™[™\Ûİ\˜ÙHÛÛÜˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YYİ\İÛHÛÛ›Û›Ü™\ˆXÚÛ™\ÜÈY‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”Û[ÚÙQœ˜[YWˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YYÙHœ˜[YHÛİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙÔİÚ]ÚÛ[ÚÙK”Û[ÚÙTYÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH˜]šYØ][™ÈÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH˜]šYØ]YÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHØYÛÛ\]YÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH˜]šYØ][Ûˆ[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH˜]šYØ]YÛÛ[\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Û[ÚÙTÙXÛÛ™YÙK[[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHÙXÛÛ™YÙH˜]šYØ]H™\İ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÙXÛÛ™œ˜[YHYÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”ÙXÛÛ™YÙU]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÙXÛÛ™YÙH]H^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”ÙXÛÛ™YÙTİX]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YÙXÛÛ™YÙHİX]H^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHÙXÛÛ™YÙHÛÛ[\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH›İ\›˜[Ø[ˆÛÈ˜XÚÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙU›ÚY
Û[ÚÙQœ˜[YK‘ÛĞ˜XÚ×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH˜XÚÈ˜]šYØ][Ûˆ[ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YH˜XÚÈÛÛ[\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÑÔK•Ü‹”ÙÔİÚ]ÚÛ[ÚÙK”Û[ÚÙTYÙQ[˜İ[Ûˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHYÙQ[˜İ[Ûˆ˜]šYØ]H™\İ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHYÙQ[˜İ[ÛˆÛÛ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË“˜]šYØ][Û‹”™]\›‘]™[\™ÜØH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]Y]Ù
—ÓÛ‘š[š\Úˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHYÙQ[˜İ[Ûˆ™]\›ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[Yœ˜[YHYÙQ[˜İ[Ûˆ™]\›ˆ™\İ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”YÙU]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YYÙH[˜[ZXÈ™\Ûİ\˜ÙH›Ü™YÜ›İ[™‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹”YÙTİX]Wˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜ÛÛ\[YYÙHİX]H^‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘š[™˜[YW‹‘Øİ[Y[›Şˆ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^\\›[šÈT’H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙU›ÚY
Øİ[Y[\\›[šË‘ĞÛXÚ×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈšXÚ^\\›[šÈ™\]Y\İ˜]šYØ]HÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈšXÚ^\\›[šÈ™\]Y\İ˜]šYØ]HT’H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÑÈšXÚ^\\›[šÈ™\]Y\İ˜]šYØ]H›İ]Y]™[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^[›[™HRH]ÛˆÛÛ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^\İX\šÙ\ˆİ[H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^X›HÛÛ[[ˆÛİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^X›HÙ[Ûİ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœšXÚ^›ØÚÈRH^ÛÛ[‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈØš™Xİš[™š\œİU\H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[›ÚÙU›ÚY
Xİ[Û]Û‹“ÛÛXÚ×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”[\XØ][Û”[”Û[ÚÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›UÚ[™İĞXİ]˜][Û”Ù\šXÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›T™\Ù[][Û”Ûİ\˜ÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›SYYXPÛÛ^™[™\”Ù\šXÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›SY\ÜØYÙP›Ş
Ü™\Ù[][Û‘œ˜[Y]ÛÜšË\YXİ]˜][Û‹•Ú[™İÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•˜[Y]TÜX›Qš[QX[ÙÜÊÜ™\Ù[][Û‘œ˜[Y]ÛÜšË\YXİ]˜][Û‹•Ú[™İÊH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Øš™Xİ^]ÛÙHH[›ÚÙJ\”[—ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\\]X[
^]ÛÙH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ\ÜÙ\Ø[YJ\YXİ]˜][Û‹•Ú[™İËÙ]›Ü\JØ\XØ][Û‹“XZ[•Ú[™İ×ŠH‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘›\Ú\Ü]Ú\“Ü\˜][ÛœÈ‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š‹”ÚİÊ
H‹[[YR\›™\ÜÔ›ÙÜ˜[Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆB‚ˆÑ˜XİBˆX›XÈ›ÚY™\Ù[][Û•ZU\Ù\ÓX[˜YÙYš[[™Ô™Y™\™[˜ÙQ›Ü“›Û•Ú[™İÜĞœš[™İ\

BˆÂˆ˜\ˆ™\Ù[][Û•ZT›Ú™Xİ]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][Û•RH‹ˆ”™\Ù[][Û•RK˜ÜÜ›ÚˆŠNÂˆ˜\ˆ™\Ù[][Û•ZT›Ú™XİHØİ[Y[“ØY
™\Ù[][Û•ZT›Ú™Xİ]
NÂ‚ˆ˜\ˆ˜]]™Tš[[™Ô™Y™\™[˜ÙHH\ÜÙ\›Ú™Xİ™Y™\™[˜ÙJˆ™\Ù[][Û•ZT›Ú™Xİˆ”Ş\İ[K”š[[™×Ş\İ[K”š[[™Ë˜Ş›ÚˆŠNÂˆ\ÜÙ\‘\]X[
‰É
ÔÊIÈOH	ÕÚ[™İÜ×Ó•	È‹˜]]™Tš[[™Ô™Y™\™[˜ÙK]šX]JÛÛ™][ÛˆŠOË•˜[YJNÂˆ\ÜÙ\‘\]X[
•\™Ù]œ˜[Y]ÛÜšÎÕ\™Ù]œ˜[Y]ÛÜšÜÈ‹˜]]™Tš[[™Ô™Y™\™[˜ÙK‘[[Y[
•[™Yš[™T›Ü\Y\ÈŠOË•˜[YJNÂ‚ˆ˜\ˆX[˜YÙYš[[™Ô™Y™\™[˜ÙHH\ÜÙ\›Ú™Xİ™Y™\™[˜ÙJˆ™\Ù[][Û•ZT›Ú™Xİˆ”Ş\İ[K”š[[™×™Y—Ş\İ[K”š[[™Ë\™Y‹˜ÜÜ›ÚˆŠNÂˆ\ÜÙ\‘\]X[
‰É
ÔÊIÈOH	ÕÚ[™İÜ×Ó•	È‹X[˜YÙYš[[™Ô™Y™\™[˜ÙK]šX]JÛÛ™][ÛˆŠOË•˜[YJNÂˆB‚ˆÑ˜XİBˆX›XÈ›ÚYÛÛ\ÜÚ][Û•\™Ù]İ\ÜÔÜX›S›Û‘XÙT›ÛİİÛ™\œÚ\

BˆÂˆ˜\ˆÛÛ\ÜÚ][Û•\™Ù]]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ“YYXH‹ˆÛÛ\ÜÚ][Û•\™Ù]˜ÜÈŠNÂˆ˜\ˆÜX›U\™Ù]]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ“YYXH‹ˆ”ÜX›PÛÛ\ÜÚ][Û•\™Ù]˜ÜÈŠNÂˆ˜\ˆÜX›TÛİ\˜ÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ”ÜX›T™\Ù[][Û”Ûİ\˜ÙK˜ÜÈŠNÂˆ˜\ˆ[İ\ÙQ]šXÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ’[œ]‹ˆ“[İ\ÙQ]šXÙK˜ÜÈŠNÂˆ˜\ˆÜX›TÛİ\˜ÙRÜİ]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ’TÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ˜ÜÈŠNÂˆ˜\ˆ™\Ù[][Û”Ûİ\˜ÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ”™\Ù[][Û”Ûİ\˜ÙK˜ÜÈŠNÂˆ˜\ˆÛ™Ûİ\˜ÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ’[\“Ü‹ˆ’Û™Ûİ\˜ÙK˜ÜÈŠNÂˆ˜\ˆÛ™\™Ù]]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ’[\“Ü‹ˆ’Û™\™Ù]˜ÜÈŠNÂˆ˜\ˆ›Ú™Xİ]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆ”™\Ù[][ÛÛÜ™K˜ÜÜ›ÚˆŠNÂˆ˜\ˆ™\Ù[][ÛÛÜ™T™Y”]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][ÛÛÜ™H‹ˆœ™Yˆ‹ˆ”™\Ù[][ÛÛÜ™K˜ÜÈŠNÂˆ˜\ˆÜX›TÛİ\˜ÙPœšYÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ”›ÑÔK•Üˆ‹ˆ•Ü”ÜX›T™\Ù[][Û”Ûİ\˜ÙPœšYÙK˜ÜÈŠNÂˆ˜\ˆ›ÑÜRÜİ]Hš[™™\Ô]
ˆœÜ˜È‹ˆ”›ÑÔK•Üˆ‹ˆ”›ÑÜUÜ•Ú[™İÒÜİ˜ÜÈŠNÂˆ˜\ˆ›ÑÜPÛÛ\ÜÚ][Û•\™Ù]]Hš[™™\Ô]
ˆœÜ˜È‹ˆ”›ÑÔK•Üˆ‹ˆ”›ÑÜUÜÛÛ\ÜÚ][Û•\™Ù]˜ÜÈŠNÂˆ˜\ˆÜX›QÙ[ÛY]R]\İØ[™Y]T]Hš[™™\Ô]
ˆ™^\›˜[‹ˆ”›ÑÔH‹ˆœÜ˜È‹ˆ”›ÑÔK•Ü‹’[\›Ü‹ˆ”ÜX›QÙ[ÛY]R]\İØ[™Y]K˜ÜÈŠNÂ‚ˆ˜\ˆÛÛ\ÜÚ][Û•\™Ù]Hš[K”™XY[^
ÛÛ\ÜÚ][Û•\™Ù]]
NÂˆ˜\ˆÜX›U\™Ù]Hš[K”™XY[^
ÜX›U\™Ù]]
NÂˆ˜\ˆÜX›TÛİ\˜ÙHHš[K”™XY[^
ÜX›TÛİ\˜ÙT]
NÂˆ˜\ˆ[İ\ÙQ]šXÙHHš[K”™XY[^
[İ\ÙQ]šXÙT]
NÂˆ˜\ˆÜX›TÛİ\˜ÙRÜİHš[K”™XY[^
ÜX›TÛİ\˜ÙRÜİ]
NÂˆ˜\ˆ™\Ù[][Û”Ûİ\˜ÙHHš[K”™XY[^
™\Ù[][Û”Ûİ\˜ÙT]
NÂˆ˜\ˆÛ™Ûİ\˜ÙHHš[K”™XY[^
Û™Ûİ\˜ÙT]
NÂˆ˜\ˆÛ™\™Ù]Hš[K”™XY[^
Û™\™Ù]]
NÂˆ˜\ˆ›Ú™XİHš[K”™XY[^
›Ú™Xİ]
NÂˆ˜\ˆ™\Ù[][ÛÛÜ™T™YˆHš[K”™XY[^
™\Ù[][ÛÛÜ™T™Y”]
NÂˆ˜\ˆÜX›TÛİ\˜ÙPœšYÙHHš[K”™XY[^
ÜX›TÛİ\˜ÙPœšYÙT]
NÂˆ˜\ˆ›ÑÜRÜİHš[K”™XY[^
›ÑÜRÜİ]
NÂˆ˜\ˆ›ÑÜPÛÛ\ÜÚ][Û•\™Ù]Hš[K”™XY[^
›ÑÜPÛÛ\ÜÚ][Û•\™Ù]]
NÂˆ˜\ˆÜX›QÙ[ÛY]R]\İØ[™Y]HHš[K”™XY[^
ÜX›QÙ[ÛY]R]\İØ[™Y]T]
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊš[\›˜[š\X[›ÛÛ\Ù\ÑXÙPÛÛ\ÜÚ][Ûˆ‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[š\X[›ÚYÛ”›Ûİš\İX[Ú[™ÙY‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
\Ù\ÑXÙPÛÛ\ÜÚ][ÛŠH‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘PÑKÚ[›™[Ú[›™[HYYXPÛÛ^Ú[›™[‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ú[›™[OH[‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Û”›Ûİš\İX[Ú[™ÙY
Û›Ûİš\İX[Ü›Ûİš\İX[
H‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š“YYXPÛÛ^‘œ›ÛJ\Ü]Ú\ŠK‘Ù]Ú[›™[Ê
H‹ÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ˜\ˆÙ]›Ûİš\İX[[™^HÛÛ\ÜÚ][Û•\™Ù]’[™^ÙŠœš]˜]H›ÚYÙ]›Ûİš\İX[‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\•YJˆÛÛ\ÜÚ][Û•\™Ù]’[™^ÙŠšYˆ
\Ù\ÑXÙPÛÛ\ÜÚ][ÛŠH‹Ù]›Ûİš\İX[[™^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
BˆÛÛ\ÜÚ][Û•\™Ù]’[™^ÙŠ—ØÛÛ[›Ûİ’\ÓÛÚ[›™[
Ú[›™[
H‹Ù]›Ûİš\İX[[™^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
Kˆ‘PÑHÛÛ[\›ÛİÚXÚÜÈ]\İİ^H™Z[™HPÑKXÛÛ\ÜÚ][Ûˆœ˜[˜ÚˆŠNÂ‚ˆ\ÜÙ\ÛÛZ[œÊš[\›˜[ÙX[YÛ\ÜÈÜX›PÛÛ\ÜÚ][Û•\™Ù]ˆÛÛ\ÜÚ][Û•\™Ù]‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ™\œšYH›ÛÛ\Ù\ÑXÙPÛÛ\ÜÚ][Ûˆ‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù]È™]\›ˆ˜[ÙNÈH‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ™\œšYH›ÚYÜ™X]UPÑT™\Ûİ\˜Ù\È‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ™\œšYH›ÚY™[X\ÙUPÑT™\Ûİ\˜Ù\È‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ™\œšYHX]š^˜[œÙ›Ü›UÑ]šXÙH‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÙ]]šXÙTØØ[PÛÜ™H‹ÜX›U\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOHˆ”Ş\İ[WÚ[™İÜ×YYXWÜX›PÛÛ\ÜÚ][Û•\™Ù]˜ÜÈˆˆÏˆ‹›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[\™˜XÙHTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[YØ]H›ÛÛÜX›R]\İ[Y™™\“İ™\œšYJİX›HİX›HKÜ[Øš™Xİˆ™\İ[Ëİ][™\İ[Ûİ[
H‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[YØ]H›ÛÛÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYJ‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[]™[[™\ˆ™[™\”™\]Y\İY‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Øš™Xİ›Ûİš\İX[ÈÙ]ÈÙ]ÈH‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[˜ÏİX›KİX›KØš™Xİˆ]\İİ™\œšYH‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›R]\İ[Y™™\“İ™\œšYH]\İ[Y™™\“İ™\œšYH‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYH]\İ›İ[™ĞY™™\“İ™\œšYH‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYH]\İ[\ÙP›İ[™ĞY™™\“İ™\œšYH‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ÚYÙ]ÛY[ÜšYÚ[ŠİX›HİX›HJH‹ÜX›TÛİ\˜ÙRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[YØ]H›ÛÛÜX›R]\İ[Y™™\“İ™\œšYJİX›HİX›HKŞ\İ[K”Ü[Øš™Xİˆ™\İ[Ëİ][™\İ[Ûİ[
H‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[YØ]H›ÛÛÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYJİX›HZ[–İX›HZ[–KİX›HX^İX›HX^KŞ\İ[K”Ü[Øš™Xİˆ™\İ[Ëİ][™\İ[Ûİ[
H‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ\X[[\™˜XÙHTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİˆŞ\İ[K’Q\ÜÜØX›H‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K‘[˜ÏİX›KİX›KØš™Xİˆ]\İİ™\œšYH‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”ÜX›R]\İ[Y™™\“İ™\œšYH]\İ[Y™™\“İ™\œšYH‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”ÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYH]\İ›İ[™ĞY™™\“İ™\œšYH‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ş\İ[K•Ú[™İÜË”ÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYH]\İ[\ÙP›İ[™ĞY™™\“İ™\œšYH‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[Ş\İ[K‘]™[[™\ˆ™[™\”™\]Y\İY‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ÚYÙ]ÛY[ÜšYÚ[ŠİX›HİX›HJHÈH‹™\Ù[][ÛÛÜ™T™Y‹İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[ÙX[YÛ\ÜÈÜX›T™\Ù[][Û”Ûİ\˜ÙHˆ™\Ù[][Û”Ûİ\˜ÙKTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİQ\ÜÜØX›H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›HÜX›PÛÛ\ÜÚ][Û•\™Ù]ØÛÛ\ÜÚ][Û•\™Ù]‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›HÜX›RÙ^X›Ø\™[œ]›İšY\ˆÚÙ^X›Ø\™[œ]›İšY\ˆ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›HÜX›S[İ\ÙR[œ]›İšY\ˆÛ[İ\ÙR[œ]›İšY\ˆ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H™XYÛ›HÛ™Ûİ\˜ÙHÜÜX›RÛ™Ûİ\˜ÙH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[[ˆ[™H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[Û™Ûİ\˜ÙHÛ™Ûİ\˜ÙH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Û™Ûİ\˜ÙKÜ™X]TÜX›J\ËÚ[™KTØØ[VTØØ[VJH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÜX›RÛ™Ûİ\˜ÙK‘\ÜÜÙJ
NÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊYÛİ\˜ÙJ
NÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[[İ™TÛİ\˜ÙJ
NÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”›ÛİÚ[™ÙY
Û›Ûİš\İX[Ü›Ûİš\İX[
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÚÙ^X›Ø\™[œ]›İšY\‹“Û”›ÛİÚ[™ÙY
Û›Ûİš\İX[Ü›Ûİš\İX[
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[]™[]™[[™\ˆ™[™\”™\]Y\İY‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[]™[]™[[™\ˆİ\œÛÜ”™\]Y\İY‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[]™[[™\ˆTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ”™[™\”™\]Y\İY‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[]™[[™\ˆTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİİ\œÛÜ”™\]Y\İY‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Øš™XİTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ”›Ûİš\İX[‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›Øš™XİTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİÛÛ\ÜÚ][Û•\™Ù]‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù]È™]\›ˆÚ\Ñ\ÜÜÙYÈ[ˆØÛÛ\ÜÚ][Û•\™Ù]ÈH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[˜ÏİX›KİX›KØš™XİˆTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ’]\İİ™\œšYH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›R]\İ[Y™™\“İ™\œšYHTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ’]\İ[Y™™\“İ™\œšYH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYHTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ’]\İ›İ[™ĞY™™\“İ™\œšYH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›QÙ[ÛY]R]\İY™™\“İ™\œšYHTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ’]\İ[\ÙP›İ[™ĞY™™\“İ™\œšYH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›ÚYTÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ”Ù]ÛY[ÜšYÚ[ŠİX›HİX›HJH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[Ú[ÛY[ÜšYÚ[ˆ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜Ø[™Y]H\È›İÜX›QÙ[ÛY]R]\İØ[™Y]HÜX›PØ[™Y]H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•Ò[\œÙXİ[Û‘]Z[
ÜX›PØ[™Y]K’[\œÙXİ[Û‘]Z[
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š˜Ø[™Y]H\ÈÙ[ÛY]R]\İ™\İ[‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š˜Ø[™Y]H\Èš\İX[š\İX[‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š\Ú[™ÈŞ\İ[K”™Y›Xİ[ÛÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Šš[™[™Ñ›YÜÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š‘Ù]›Ü\J•š\İX[]ˆ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š‘Ù]›Ü\J’[\œÙXİ[Û‘]Z[ˆ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈÙX[YÛ\ÜÈÜX›QÙ[ÛY]R]\İØ[™Y]H‹ÜX›QÙ[ÛY]R]\İØ[™Y]Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈØš™Xİš\İX[]ÈÙ]ÈH‹ÜX›QÙ[ÛY]R]\İØ[™Y]Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈZ[[\œÙXİ[Û‘]Z[ÈÙ]ÈH‹ÜX›QÙ[ÛY]R]\İØ[™Y]Kİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈÜX›QÙ[ÛY]R]\İØ[™Y]J‹›ÑÜPÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”›ÑÜUÜ‘Ù[ÛY]R]\İØ[™Y]H‹›ÑÜPÛÛ\ÜÚ][Û•\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ\œÛÜˆ™\]Y\İYİ\œÛÜˆÈÙ]Èš]˜]HÙ]ÈH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[›ÚYÙ]]šXÙTØØ[JİX›HTØØ[VİX›HTØØ[VJH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[›ÚYÙ]ÛY[Ú^™JİX›HÚYİX›HZYÚ
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÛÛ™\]Y\İİ\œÛÜŠİ\œÛÜˆİ\œÛÜŠH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™\]Y\İYİ\œÛÜˆHİ\œÛÜˆÏÈİ\œÛÜœË“›Û™NÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊİ\œÛÜ”™\]Y\İYË’[›ÚÙJ\Ë]™[\™ÜË‘[\JNÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚY\T›Ûİš\İX[^[İ]

H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ›ÛİRQ[[Y[“YX\İ\™JØÛY[Ú^™JNÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ›ÛİRQ[[Y[\œ˜[™ÙJ™]È™Xİ
™]ÈÚ[

KØÛY[Ú^™JJNÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ›İXİYİ™\œšYHÛÛ\ÜÚ][Û•\™Ù]Ù]ÛÛ\ÜÚ][Û•\™Ù]ÛÜ™J
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÚ\Ñ\ÜÜÙYÈ[ˆØÛÛ\ÜÚ][Û•\™Ù]È‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ™\œšYHR[œ]›İšY\ˆÙ][œ]›İšY\Š\H[œ]]šXÙJH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[œ]]šXÙHOH\[ÙŠ[İ\ÙQ]šXÙJH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[œ]]šXÙHOH\[ÙŠÙ^X›Ø\™]šXÙJH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÙX[YÛ\ÜÈÜX›RÙ^X›Ø\™[œ]›İšY\ˆˆRÙ^X›Ø\™[œ]›İšY\‹Q\ÜÜØX›H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÙX[YÛ\ÜÈÜX›S[İ\ÙR[œ]›İšY\ˆˆS[İ\ÙR[œ]›İšY\‹Q\ÜÜØX›H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’[œ]X[˜YÙ\‹İ\œ™[”™YÚ\İ\’[œ]›İšY\Š\ÊH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Ù^X›Ø\™‘›Øİ\Ê[
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÚ[™Ú[™ÈHXİ]™H™\Ù[][ÛˆÛİ\˜ÙH\È›İHØ[YH\È‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™[X\ÙY^XÚ]HHÔˆÜˆÚ[ˆ\È›İšY\ˆ\È\ÜÜÙY‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š›ÚYR[œ]›İšY\‹“›İYQXXİ]˜]J
Wˆ×ˆ™[X\ÙS[İ\ÙPØ\\™J™\Ü[œ]ˆYJN×ˆH‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜÛİ\˜ÙK”™\]Y\İİ\œÛÜŠİ\œÛÜŠNÈ‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”˜]Ó[İ\ÙPXİ[ÛœËØ[˜Ù[Ø\\™K‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”˜]Ó[İ\ÙPXİ[ÛœËXİ]˜]H˜]Ó[İ\ÙPXİ[ÛœËØ[˜Ù[Ø\\™H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÚ]K”™\Ü[œ]
™\Ü
H‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š“›İYPØ\\™T™[X\ÙY‹ÜX›TÛİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÛÛ\ĞXİ]™TÛİ\˜ÙSÜØ\\™Y›İšY\Ø[˜Ù[
˜]Ó[İ\ÙR[œ]™\Ü˜]Ó[İ\ÙR[œ]™\Ü
H‹[İ\ÙQ]šXÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ˜]Ó[İ\ÙR[œ]™\ÜXİ[ÛœÈOH˜]Ó[İ\ÙPXİ[ÛœËØ[˜Ù[Ø\\™H‹[İ\ÙQ]šXÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ˜]Ó[İ\ÙR[œ]™\Ü’[œ]Ûİ\˜ÙHOH[‹[İ\ÙQ]šXÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ˜]Ó[İ\ÙR[œ]™\Ü’[œ]Ûİ\˜ÙK‘Ù][œ]›İšY\Š\[ÙŠ[İ\ÙQ]šXÙJJH\ÈS[İ\ÙR[œ]›İšY\ˆ‹[İ\ÙQ]šXÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™Y™\™[˜ÙQ\]X[Ê[œ]›İšY\‹Ü›İšY\Ø\\™JH‹[İ\ÙQ]šXÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘\]X[
ˆ‹ˆ[İ\ÙQ]šXÙK”Ü]
ˆ’\ĞXİ]™TÛİ\˜ÙSÜØ\\™Y›İšY\Ø[˜Ù[
˜]Ó[İ\ÙR[œ]™\Ü
H‹ˆİš[™ÔÜ]Ü[ÛœË“›Û™JK“[™İHJNÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOHˆ”Ş\İ[WÚ[™İÜ×TÜX›T™\Ù[][Û”Ûİ\˜ÙRÜİ˜ÜÈˆˆÏˆ‹›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊÛÛ\[H[˜ÛYOHˆ”Ş\İ[WÚ[™İÜ×ÜX›T™\Ù[][Û”Ûİ\˜ÙK˜ÜÈˆˆÏˆ‹›Ú™Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÔX›XÔ™\Ù[][Û”Ûİ\˜ÙJÜš]XØ[œ›ÛUš\İX[
š\İX[
JNÈ‹™\Ù[][Û”Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÔX›XÔ™\Ù[][Û”Ûİ\˜ÙJÜš]XØ[œ›ÛUš\İX[
\[™[˜ŞSØš™Xİ
JNÈ‹™\Ù[][Û”Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]Hİ]XÈ™\Ù[][Û”Ûİ\˜ÙHÔX›XÔ™\Ù[][Û”Ûİ\˜ÙJ™\Ù[][Û”Ûİ\˜ÙHÛİ\˜ÙJH‹™\Ù[][Û”Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
Ûİ\˜ÙH\ÈÜX›T™\Ù[][Û”Ûİ\˜ÙHÜX›TÛİ\˜ÙJH‹™\Ù[][Û”Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜX›TÛİ\˜ÙK’Û™Ûİ\˜ÙNÈ‹™\Ù[][Û”Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ™Ûİ\˜ÙHÜ™X]TÜX›JÜX›T™\Ù[][Û”Ûİ\˜ÙHİÛ™\‹[ˆ[™KİX›HTØØ[VİX›HTØØ[VJH‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛ™Ûİ\˜ÙJÜX›T™\Ù[][Û”Ûİ\˜ÙHÜX›SİÛ™\‹[ˆÜX›R[™KİX›HTØØ[VİX›HTØØ[VJH‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÜX›SİÛ™\ˆHÜX›SİÛ™\È‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÜX›R[™HHÜX›R[™NÈ‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ’Û™\™Ù]Ü™X]TÜX›JÜX›R[™KTØØ[VTØØ[VJH‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜÜX›SİÛ™\‹‘Ù][œ]›İšY\Š[œ]]šXÙJNÈ‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜÜX›SİÛ™\‹”›Ûİš\İX[È‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÜX›SİÛ™\‹”›Ûİš\İX[H˜[YNÈ‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[›ÛÛ\ÔÜX›H‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Šİ\œÛÜ”™\]Y\İY]™[˜[YH‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”™\]Y\İYİ\œÛÜ”›Ü\S˜[YH‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•TİXœØÜšX™UĞİ\œÛÜ”™\]Y\İY‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”Ş\İ[K”™Y›Xİ[Û‹\ÜÙ[X›H‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Šœ™\Ù[][ÛÛÜ™P\ÜÙ[X›H‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š•Ü”ÜX›T™\Ù[][Û”Ûİ\˜ÙPœšYÙK•PÜ™X]Jˆ\Ëˆ™\Ù[][ÛÛÜ™P\ÜÙ[X›H‹›ÑÜRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÜÛİ\˜ÙKİ\œÛÜ”™\]Y\İY
ÏHÛ”Ûİ\˜ÙPİ\œÛÜ”™\]Y\İYÈ‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H›ÚYÛ”Ûİ\˜ÙPİ\œÛÜ”™\]Y\İY
Øš™XİÈÙ[™\‹]™[\™ÜÈJH‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—ÚÜİ\TÜX›Pİ\œÛÜŠÕÜİ\œÛÜŠÜÛİ\˜ÙK”™\]Y\İYİ\œÛÜ“˜[YHÏÈÜÛİ\˜ÙK”™\]Y\İYİ\œÛÜË•Ôİš[™Ê
JJNÈ‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—’[™ˆOˆÜİ\œÛÜ‹’[™‹ÜX›TÛİ\˜ÙPœšYÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[Üİ\œÛÜÈ\İÜX›Pİ\œÛÜˆÈÙ]Èš]˜]HÙ]ÈH‹›ÑÜRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[›ÛÛ\TÜX›Pİ\œÛÜŠÜİ\œÛÜˆİ\œÛÜŠH‹›ÑÜRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“\İÜX›Pİ\œÛÜˆHİ\œÛÜÈ‹›ÑÜRÜİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù]È™]\›ˆÜÜX›SİÛ™\ˆOH[ÈH‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊš[\›˜[™\Ù[][Û”Ûİ\˜ÙHÜX›SİÛ™\ˆ‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™Ù]È™]\›ˆÜÜX›SİÛ™\ÈH‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYŠÚÛÚÜÈOH[	‰ˆÚÛ™Ü˜\\ˆOH[
H‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÜÜX›R[™NÈ‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ[™HOH[‹–™\›È‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÜX›T™\Ù[][Û”Ûİ\˜ÙHÜÜX›SİÛ™\ˆ‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]H[ˆÜÜX›R[™H‹Û™Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊš[\›˜[İ]XÈÛ™\™Ù]Ü™X]TÜX›J[ˆÛ™İX›HTØØ[VİX›HTØØ[VJH‹Û™\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛ™\™Ù]
[ˆÛ™İX›HTØØ[VİX›HTØØ[VJH‹Û™\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ—Ú\ÔÜX›HHYNÈ‹Û™\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊİ\œ™[TØØ[HHTØØ[L‹‘œ›ÛT^[Ô\’[˜Ú‹Û™\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšYˆ
Ú\ÔÜX›JWˆ×ˆ™]\›È‹Û™\™Ù]İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆB‚ˆÑ˜XİBˆX›XÈ›ÚYÚ[Ó™]Ú[™İÑXÛÜ˜][Û”Ù\šXÙU\Ù\Ó˜]]™Q˜YÓ[İ™P›İ[™\J
BˆÂˆ˜\ˆÛİ\˜ÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ”›ÑÔK•Üˆ‹ˆ”]›Ü›H‹ˆ”Ú[Ó™]Ü•Ú[™İÑXÛÜ˜][Û”Ù\šXÙK˜ÜÈŠNÂˆ˜\ˆÛİ\˜ÙHHš[K”™XY[^
Ûİ\˜ÙT]
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊÚ[™İÈ\È›İUšY]ÈšY]È‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšY]Ë’[™HOH[‹–™\›È‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Ü\˜][™ÔŞ\İ[K’\ÕÚ[™İÜÊ
H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Ü\˜][™ÔŞ\İ[K’\ÓXXÓÔÊ
H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“Ü\˜][™ÔŞ\İ[K’\Ó[^

H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšY]È\È›İS˜]]™UÚ[™İÔÛİ\˜ÙH˜]]™UÚ[™İÔÛİ\˜ÙH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆ˜]]™UÚ[™İÔÛİ\˜ÙK“˜]]™H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘Ù]˜]]™UÚ[™İÊšY]ÊH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÚ[ŒÌˆH˜]]™UÚ[™İË•Ú[ŒÌˆ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÚ[ŒÌ‹’\Õ˜[YHÈÚ[ŒÌ‹•˜[YK’][Lˆˆ[‹–™\›È‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆÛØÛØHH˜]]™UÚ[™İËÛØÛØH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆÛØÛØK‘Ù]˜[YSÜ‘Y˜][

H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜\ˆLHH˜]]™UÚ[™İË–LH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ›™]ÈLUÚ[™İÒ[™JLK•˜[YK’][LKLK•˜[YK’][LŠH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•P™YÚ[•Ú[ŒÌ‘˜YÓ[İ™JÙ]Ú[ŒÌ’Û™
šY]ÊJH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•P™YÚ[ÛØÛØQ˜YÓ[İ™JÙ]ÛØÛØUÚ[™İÊšY]ÊJH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆP™YÚ[–LQ˜YÓ[İ™JLK‘\Ü^KLK•Ú[™İÊH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊšÛ™OH[‹–™\›È‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™[X\ÙPØ\\™J
NÈ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[™Y\ÜØYÙJÛ™ÓWÔÖTĞÓÓSPS‘
[ŠTĞ×ÓSÕTÑSSÕ‘K[‹–™\›ÊH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[™Y\ÜØYÙJÛ™ÓWÓ•UÓ•T[‹–™\›Ë[‹–™\›ÊH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈØšÓXœ˜\HH‹İ\Ü‹ÛX‹ÛX›Øš˜ËK™[X—ˆ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–Ôİ\ÜYÔÔ]›Ü›J›XXÛÜ×ŠWH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØšÑÙ]Û\ÜÊ“”Ğ\XØ][Û—ŠH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[™YÚ\İ\“˜[YJœÚ\™Y\XØ][Û—ŠH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[™YÚ\İ\“˜[YJ˜İ\œ™[]™[ŠH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”Ù[™YÚ\İ\“˜[YJœ\™›Ü›UÚ[™İÑ˜YÕÚ]]™[—ŠH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜İ\œ™[]™[OH[‹–™\›È‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ“ØšÓ\ÙÔÙ[™
œÕÚ[™İË\™›Ü›Q˜YÔÙ[XİÜ‹İ\œ™[]™[
H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ‘[TÚ[H›Øš˜×Û\ÙÔÙ[™ˆ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİİš[™ÈLSXœ˜\HH›X–LKœÛË—ˆ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœš]˜]HÛÛœİ[™]ÛS[İ™\™\Ú^™S[İ™HH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–Ôİ\ÜYÔÔ]›Ü›J›[^ŠWH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–Y˜][›ÛİÚ[™İÊ\Ü^JH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–]Y\TÚ[\Š‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[\›]ÛJ\Ü^K—Ó‘UÕÓWÓSÕ‘T‘TÒV‘W‹Û›RY‘^\İÎˆ˜[ÙJH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–[™Ü˜X”Ú[\Š\Ü^KR[‹–™\›ÊH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–Ù[™]™[
‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”İXœİXİ\™T™Y\™XİX\ÚÈİXœİXİ\™S›İYSX\ÚÈ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ–›\Ú
\Ü^JH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[ŠˆœX›XÈ›ÛÛP™YÚ[‘˜YÓ[İ™JØš™XİÚ[™İÊWˆ×ˆ™]\›ˆ˜[ÙN×ˆH‹ˆÛİ\˜ÙKˆİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆB‚ˆÑ˜XİBˆX›XÈ›ÚY›Y[Ş\İ[U[YU\Ù\Õ\YÜX›Tİ]P™Y›Ü™T]›Ü›Q˜[˜XÚÊ
BˆÂˆ˜\ˆÛİ\˜ÙT]Hš[™™\Ô]
ˆœÜ˜È‹ˆ“ZXÜ›ÜÛÙ‘İ™]•Üˆ‹ˆœÜ˜È‹ˆ”™\Ù[][Û‘œ˜[Y]ÛÜšÈ‹ˆ”Ş\İ[H‹ˆ•Ú[™İÜÈ‹ˆ•[YSX[˜YÙ\‹˜ÜÈŠNÂˆ˜\ˆÛÛ˜Xİ]Hš[™™\Ô]
ˆ™^\›˜[‹ˆ”›ÑÔH‹ˆœÜ˜È‹ˆ”›ÑÔK•Ü‹’[\›Ü‹ˆ”ÜX›TŞ\İ[U[YK˜ÜÈŠNÂˆ˜\ˆ™YÚ\İT]Hš[™™\Ô]
ˆ™^\›˜[‹ˆ”›ÑÔH‹ˆœÜ˜È‹ˆ”›ÑÔK•Ü‹’[\›Ü‹ˆ”ÜX›UÜ”Ù\šXÙT™YÚ\İK˜ÜÈŠNÂˆ˜\ˆÛİ\˜ÙHHš[K”™XY[^
Ûİ\˜ÙT]
NÂˆ˜\ˆÛÛ˜XİHš[K”™XY[^
ÛÛ˜Xİ]
NÂˆ˜\ˆ™YÚ\İHHš[K”™XY[^
™YÚ\İT]
NÂ‚ˆ\ÜÙ\İX\™™Y›Ü™JˆÛİ\˜ÙKˆšYˆ
SÜ\˜][™ÔŞ\İ[K’\ÕÚ[™İÜÊ
JH‹ˆ”™YÚ\İK‘Ù]˜[YJ™YÔ\œÛÛ˜[^™RÙ^T]ŠNÂˆ\ÜÙ\İX\™™Y›Ü™JˆÛİ\˜ÙKˆ”ÜX›UÜ”Ù\šXÙT™YÚ\İK•QÙ]Ş\İ[U[YTÛİ\˜ÙJ‹ˆšYˆ
SÜ\˜][™ÔŞ\İ[K’\ÕÚ[™İÜÊ
JHŠNÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›UÜ”Ù\šXÙRÙ^K”™\Ù[][Û‘œ˜[Y]ÛÜšÈ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœÛİ\˜ÙK•QÙ]Ş\İ[U[YJİ]ÜX›TŞ\İ[U[YH[YJH‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YHOHÜX›TŞ\İ[U[YK“YÚ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ[YHOHÜX›TŞ\İ[U[YK‘\šÈ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”ÜX›UÜ”Ù\šXÙT™YÚ\İK”Ş\İ[U[YPÚ[™ÙY
ÏHÛ”ÜX›TŞ\İ[U[YPÚ[™ÙY‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\Ü]Ú\‹ÚXÚĞXØÙ\ÜÊ
H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\Ü]Ú\‹’\ÔÚ]İÛ”İ\Y\Ü]Ú\‹’\ÔÚ]İÛ‘š[š\ÚY‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™\Ü]Ú\‹™YÚ[’[›ÚÙJ\Ü]Ú\”š[Üš]K“›Ü›X[
Xİ[ÛŠSÛ”Ş\İ[U[YPÚ[™ÙY
H‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”Ş\İ[K”™Y›Xİ[Ûˆ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœ™]\›ˆYNÈ‹Ûİ\˜ÙKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[[HÜX›TŞ\İ[U[YH‹ÛÛ˜Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈ[\™˜XÙHTÜX›TŞ\İ[U[YTÛİ\˜ÙH‹ÛÛ˜Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ˜›ÛÛQÙ]Ş\İ[U[YJİ]ÜX›TŞ\İ[U[YH[YJH‹ÛÛ˜Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ™]™[]™[[™\ÈŞ\İ[U[YPÚ[™ÙY‹ÛÛ˜Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊœX›XÈİ]XÈ]™[]™[[™\ÈŞ\İ[U[YPÚ[™ÙY‹™YÚ\İKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ”™YÚ\İ\”Ş\İ[U[YTÛİ\˜ÙJTÜX›TŞ\İ[U[YTÛİ\˜ÙHÛİ\˜ÙJH‹™YÚ\İKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\ÛÛZ[œÊ•QÙ]Ş\İ[U[YTÛİ\˜ÙJ‹™YÚ\İKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ\ÜÙ\‘Ù\Ó›İÛÛZ[Š”Ş\İ[K”™Y›Xİ[Ûˆ‹ÛÛ˜Xİİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆB‚ˆš]˜]Hİ]XÈ[[Y[\ÜÙ\›Ú™Xİ™Y™\™[˜ÙJØİ[Y[›Ú™Xİİš[™È[˜ÛYTİY™š^
BˆÂˆ™]\›ˆ\ÜÙ\”Ú[™ÛJˆ›Ú™Xİ‘\ØÙ[™[Ê”›Ú™Xİ™Y™\™[˜ÙHŠKˆ][HOˆ[˜ÛYQ[™ÕÚ]
][K’[˜ÛYH‹[˜ÛYTİY™š^
JNÂˆB‚ˆš]˜]Hİ]XÈ[[Y[\ÜÙ\XÚØYÙT™Y™\™[˜ÙJØİ[Y[›Ú™Xİİš[™È[˜ÛYJBˆÂˆ™]\›ˆ\ÜÙ\”Ú[™ÛJˆ›Ú™Xİ‘\ØÙ[™[Ê”XÚØYÙT™Y™\™[˜ÙHŠKˆ][HOˆİš[™Ë‘\]X[Ê][K]šX]J’[˜ÛYHŠOË•˜[YK[˜ÛYKİš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
JNÂˆB‚ˆš]˜]Hİ]XÈ›ÚY\ÜÙ\İX\™™Y›Ü™Jİš[™ÈÛİ\˜ÙKİš[™ÈİX\™İš[™ÈİX\™YØ[
BˆÂˆ˜\ˆİX\™[™^HÛİ\˜ÙK’[™^ÙŠİX\™İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂˆ˜\ˆİX\™YØ[[™^HÛİ\˜ÙK’[™^ÙŠİX\™YØ[İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[
NÂ‚ˆ\ÜÙ\•YJİX\™[™^H	‘^XİYİX\™	ŞÙİX\™IÈÈ^\İˆŠNÂˆ\ÜÙ\•YJİX\™YØ[[™^H	‘^XİYİX\™YØ[	ŞÙİX\™YØ[IÈÈ^\İˆŠNÂˆ\ÜÙ\•YJˆİX\™[™^İX\™YØ[[™^ˆ	‘^XİYİX\™	ŞÙİX\™IÈÈ\X\ˆ™Y›Ü™H	ŞÙİX\™YØ[IËˆŠNÂˆB‚ˆš]˜]Hİ]XÈ›ÚY\ÜÙ\ÛÛ\[R[˜ÛYJØİ[Y[›Ú™Xİİš[™È[˜ÛYTİY™š^›ÛÛ[šÈH˜[ÙJBˆÂˆ\ÜÙ\ÛÛZ[œÊˆ›Ú™Xİ‘\ØÙ[™[ÊÛÛ\[HŠKˆ][HOˆ[˜ÛYQ[™ÕÚ]
][K[šÈÈ“[šÈˆˆ’[˜ÛYH‹[˜ÛYTİY™š^
JNÂˆB‚ˆš]˜]Hİ]XÈ›ÛÛ[˜ÛYQ[™ÕÚ]
[[Y[[[Y[İš[™È]šX]S˜[YKİš[™È[˜ÛYTİY™š^
BˆÂˆ˜\ˆ[˜ÛYHH]šX]S˜[YHOH“[šÈ‚ˆÈ[[Y[‘[[Y[
“[šÈŠOË•˜[YK”™\XÙJ	ËÉË	×	ÊBˆˆ[[Y[]šX]J]šX]S˜[YJOË•˜[YK”™\XÙJ	ËÉË	×	ÊNÂˆ™]\›ˆ[˜ÛYOË‘[™ÕÚ]
[˜ÛYTİY™š^İš[™ĞÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJHOHYNÂˆB‚ˆš]˜]Hİ]XÈİš[™ÏÈÙ]][SY]Y]J[[Y[[[Y[İš[™ÈY]Y]S˜[YJBˆÂˆ™]\›ˆ[[Y[]šX]JY]Y]S˜[YJOË•˜[YHÏÈ[[Y[‘[[Y[
Y]Y]S˜[YJOË•˜[YNÂˆB‚ˆš]˜]Hİ]XÈ›ÚY\ÜÙ\Ûİ\˜ÙQš[Q^\İÊ\˜[\Èİš[™Ö×H]ÙYÛY[ÊBˆÂˆ\ÜÙ\•YJš[K‘^\İÊš[™™\Ô]
]ÙYÛY[ÊJK	‘^XİYÛİ\˜ÙHš[H	ŞÔ]ÛÛXš[™J]ÙYÛY[Ê_IÈÈ^\İˆŠNÂˆB‚ˆš]˜]Hİ]XÈİš[™Èš[™™\Ô]
\˜[\Èİš[™Ö×H]ÙYÛY[ÊBˆÂˆ˜\ˆ\™XİÜHH™]È\™XİÜR[™›Ê\ÛÛ^˜\ÙQ\™XİÜJNÂ‚ˆÚ[H
\™XİÜHOH[
BˆÂˆ˜\ˆØ[™Y]HH]ÛÛXš[™J™]Ö×HÈ\™XİÜK‘[˜[YHKÛÛ˜Ø]
]ÙYÛY[ÊK•Ğ\œ˜^J
JNÂ‚ˆYˆ
š[K‘^\İÊØ[™Y]JJBˆÂˆ™]\›ˆØ[™Y]NÂˆB‚ˆ\™XİÜHH\™XİÜK”\™[ÂˆB‚ˆ›İÈ™]Èš[S›İ›İ[™^Ù\[ÛŠ	Ûİ[›İØØ]H™\Èš[H	ŞÔ]ÛÛXš[™J]ÙYÛY[Ê_IÈœ›ÛHH\İİ]]\™XİÜKˆŠNÂˆBŸB