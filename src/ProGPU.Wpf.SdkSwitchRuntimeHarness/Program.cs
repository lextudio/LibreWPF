using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    private const string SmokeAssemblyName = "ProGPU.Wpf.SdkSwitchSmoke";
    private const string AppTypeName = "ProGPU.Wpf.SdkSwitchSmoke.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.SdkSwitchSmoke.MainWindow";
    private const string PortableMediaContextRenderServiceTypeName = "System.Windows.Media.PortableMediaContextRenderService";
    private const string PortablePresentationSourceTypeName = "System.Windows.PortablePresentationSource";
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private static readonly string[] RequiredWpfRuntimeAssemblies =
    [
        "WindowsBase",
        "System.Xaml",
        "PresentationCore",
        "PresentationFramework",
        "PresentationUI",
        "ReachFramework",
        "UIAutomationTypes",
        "UIAutomationProvider",
        "System.Windows.Input.Manipulations",
        "System.Windows.Primitives",
        "PresentationFramework.Aero2",
        "PresentationFramework.Fluent"
    ];
    private static readonly string[] ProGpuRuntimeAssemblies =
    [
        "ProGPU.Wpf",
        "ProGPU.Backend",
        "ProGPU.Scene",
        "ProGPU.Vector",
        "ProGPU.Text"
    ];
    private static readonly string[] SilkNetRuntimeAssemblies =
    [
        "Silk.NET.Core",
        "Silk.NET.GLFW",
        "Silk.NET.Input.Common",
        "Silk.NET.Input.Glfw",
        "Silk.NET.Maths",
        "Silk.NET.WebGPU",
        "Silk.NET.Windowing.Common",
        "Silk.NET.Windowing.Glfw"
    ];
    private static readonly string[] SupportPackageRuntimeAssemblies =
    [
        "System.Configuration.ConfigurationManager",
        "System.Diagnostics.EventLog",
        "System.Formats.Nrbf",
        "System.IO.Packaging",
        "System.Security.Cryptography.ProtectedData",
        "System.Windows.Extensions"
    ];

    [STAThread]
    private static int Main()
    {
        try
        {
            SmokeInputs inputs = ResolveSmokeInputs();
            RunObjectGraphSmoke(inputs);
            RunSdkPortableBootstrapSmoke(inputs);
            RunApplicationRunSmoke(inputs);

            Console.WriteLine("ProGPU WPF SDK switch runtime smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static SmokeInputs ResolveSmokeInputs()
    {
        string repoRoot = FindRepoRoot();
        string appOutputRoot = Path.Combine(
            repoRoot,
            "artifacts",
            "bin",
            SmokeAssemblyName,
            "Debug",
            "net11.0");
        string smokeAssemblyPath = Path.Combine(appOutputRoot, SmokeAssemblyName + ".dll");
        string packagedWpfRoot = Path.Combine(
            repoRoot,
            "artifacts",
            "packaging",
            "Debug",
            "Microsoft.DotNet.Wpf.GitHub.Debug",
            "lib",
            "net11.0");
        string wpfRoot = Directory.Exists(packagedWpfRoot)
            ? packagedWpfRoot
            : Path.Combine(repoRoot, "artifacts", "progpu-wpf-sdk-smoke", "wpf");
        string proGpuRoot = Path.Combine(repoRoot, "artifacts", "progpu-wpf-sdk-smoke", "progpu");

        RequireFile(smokeAssemblyPath, "SDK switch smoke assembly");
        RequireOutputRuntimeAssets(appOutputRoot);
        RequireDirectory(wpfRoot, "ported WPF artifact root");
        RequireDirectory(proGpuRoot, "ProGPU artifact root");

        return new SmokeInputs(repoRoot, appOutputRoot, smokeAssemblyPath, wpfRoot, proGpuRoot);
    }

    private static void RequireOutputRuntimeAssets(string appOutputRoot)
    {
        foreach (string assemblyName in RequiredWpfRuntimeAssemblies.Concat(ProGpuRuntimeAssemblies).Concat(SilkNetRuntimeAssemblies).Concat(SupportPackageRuntimeAssemblies))
        {
            RequireFile(
                Path.Combine(appOutputRoot, assemblyName + ".dll"),
                $"SDK switch output runtime asset '{assemblyName}.dll'");
        }

        RequireAnyFile(
            appOutputRoot,
            GetNativeAssetCandidates("wgpu"),
            "SDK switch output native WebGPU runtime asset");
        RequireAnyFile(
            appOutputRoot,
            GetNativeAssetCandidates("glfw"),
            "SDK switch output native GLFW runtime asset");
    }

    private static string[] GetNativeAssetCandidates(string assetName)
    {
        return assetName switch
        {
            "wgpu" when OperatingSystem.IsWindows() => ["wgpu_native.dll"],
            "wgpu" when OperatingSystem.IsMacOS() => ["libwgpu_native.dylib"],
            "wgpu" => ["libwgpu_native.so"],
            "glfw" when OperatingSystem.IsWindows() => ["glfw3.dll"],
            "glfw" when OperatingSystem.IsMacOS() => ["libglfw.3.dylib"],
            "glfw" => ["libglfw.so.3"],
            _ => throw new ArgumentOutOfRangeException(nameof(assetName), assetName, null)
        };
    }

    private static void RunObjectGraphSmoke(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(inputs.SmokeAssemblyPath);

        object app = Create(smokeAssembly, AppTypeName);
        try
        {
            InvokeVoid(app, "InitializeComponent");
            ValidateApp(app);

            object window = Create(smokeAssembly, MainWindowTypeName);
            ValidateWindow(window, validateFrameContent: false, flushDispatcherOperations: null);
        }
        finally
        {
            TryInvoke(app, "Shutdown");
        }
    }

    private static void RunSdkPortableBootstrapSmoke(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(inputs.SmokeAssemblyPath);
        Type bootstrapType = smokeAssembly.GetType(
            "ProGPU.Wpf.Sdk.ProGpuWpfSdkPortableBootstrap",
            throwOnError: true)!;

        RuntimeHelpers.RunModuleConstructor(smokeAssembly.ManifestModule.ModuleHandle);

        Assembly presentationFramework = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationFramework"));
        Type activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        try
        {
            AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "SDK portable bootstrap activation enabled");
            AssertEqual(
                true,
                loadContext.Assemblies.Any(assembly => string.Equals(assembly.GetName().Name, "ProGPU.Wpf", StringComparison.Ordinal)),
                "SDK portable bootstrap loaded ProGPU.Wpf");
            AssertEqual("ProGPU.Wpf.Sdk", bootstrapType.Namespace ?? string.Empty, "SDK portable bootstrap namespace");
        }
        finally
        {
            ClearPortableActivation(activationServiceType);
        }
    }

    private static void RunApplicationRunSmoke(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(inputs.SmokeAssemblyPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationCore"));
        Assembly presentationFramework = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationFramework"));

        object? app = null;
        SdkApplicationRunRecorder? recorder = null;
        Type? activationServiceType = null;
        bool runCompleted = false;

        try
        {
            app = Create(smokeAssembly, AppTypeName);
            InvokeVoid(app, "InitializeComponent");
            ValidateApp(app);

            recorder = RegisterPortableActivation(
                presentationFramework,
                presentationCore,
                app,
                out activationServiceType);

            object exitCode = Invoke(app, "Run");
            runCompleted = true;
            AssertEqual(0, exitCode, "Application.Run exit code");
            recorder.ValidateAfterRun();
        }
        finally
        {
            recorder?.Dispose();
            ClearPortableActivation(activationServiceType);

            if (!runCompleted && app is not null)
            {
                TryInvoke(app, "Shutdown");
            }
        }
    }

    private static SdkSmokeLoadContext CreateLoadContext(SmokeInputs inputs)
    {
        return new SdkSmokeLoadContext(
            inputs.RepoRoot,
            inputs.AppOutputRoot,
            inputs.SmokeAssemblyPath,
            inputs.WpfRoot,
            inputs.ProGpuRoot);
    }

    private static void ValidateApp(object app)
    {
        AssertEqual("MainWindow.xaml", GetProperty(app, "StartupUri").ToString() ?? string.Empty, "startup URI");

        object resources = GetProperty(app, "Resources");
        object accentBrush = Invoke(app, "TryFindResource", "SmokeAccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "application accent brush");
        AssertEqual("#FF356D9E", GetProperty(accentBrush, "Color").ToString() ?? string.Empty, "application accent brush color");
        object mergedAccentBrush = Invoke(app, "TryFindResource", "MergedAccentBrush");
        AssertType(mergedAccentBrush, "System.Windows.Media.SolidColorBrush", "application merged accent brush");
        AssertEqual("#FF6B8F3A", GetProperty(mergedAccentBrush, "Color").ToString() ?? string.Empty, "application merged accent brush color");
        object unsharedAccentBrush = Invoke(app, "TryFindResource", "UnsharedAccentBrush");
        object secondUnsharedAccentBrush = Invoke(app, "TryFindResource", "UnsharedAccentBrush");
        AssertType(unsharedAccentBrush, "System.Windows.Media.SolidColorBrush", "application unshared accent brush");
        AssertEqual("#FFC45A2B", GetProperty(unsharedAccentBrush, "Color").ToString() ?? string.Empty, "application unshared accent brush color");
        AssertNotSame(unsharedAccentBrush, secondUnsharedAccentBrush, "application x:Shared=false resource instance");
        object smokePanelMargin = Invoke(app, "TryFindResource", "SmokePanelMargin");
        AssertType(smokePanelMargin, "System.Windows.Thickness", "application merged panel margin");
        object providerGreeting = Invoke(app, "TryFindResource", "ProviderGreeting");
        AssertType(providerGreeting, "System.Windows.Data.ObjectDataProvider", "application object data provider");
        AssertEqual("provider:7", GetProperty(providerGreeting, "Data"), "application object data provider result");
        AssertAtLeast(1, GetCount(GetProperty(resources, "Keys")), "application resource key count");
    }

    private static void ValidateWindow(
        object window,
        bool validateFrameContent,
        Action<object>? flushDispatcherOperations)
    {
        AssertAssignableTo(window, "System.Windows.Window", "SDK smoke main window");
        AssertEqual("ProGPU WPF SDK Smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(420.0, GetProperty(window, "Width"), "window width");
        AssertEqual(840.0, GetProperty(window, "Height"), "window height");

        InvokeVoid(window, "UpdateLayout");

        object message = Invoke(window, "FindName", "Message");
        AssertType(message, "System.Windows.Controls.TextBlock", "message element");
        AssertEqual("ProGPU WPF SDK switch managed subsystem smoke", GetProperty(message, "Text"), "message text");
        object messageForeground = GetProperty(message, "Foreground");
        AssertType(messageForeground, "System.Windows.Media.SolidColorBrush", "message dynamic resource foreground");
        AssertEqual("#FF6B8F3A", GetProperty(messageForeground, "Color").ToString() ?? string.Empty, "message foreground color");
        object rootPanel = Invoke(window, "FindName", "RootPanel");
        AssertType(rootPanel, "System.Windows.Controls.StackPanel", "root panel element");

        object actionButton = Invoke(window, "FindName", "ActionButton");
        AssertType(actionButton, "System.Windows.Controls.Button", "action button");
        AssertEqual("ProGPU WPF SDK switch managed subsystem smoke", GetProperty(actionButton, "Content"), "button bound content");
        AssertType(GetProperty(actionButton, "Style"), "System.Windows.Style", "action button explicit style");
        object actionButtonTemplate = GetProperty(actionButton, "Template");
        AssertType(actionButtonTemplate, "System.Windows.Controls.ControlTemplate", "action button control template");
        object actionButtonTemplateRoot = Invoke(actionButtonTemplate, "LoadContent");
        AssertType(actionButtonTemplateRoot, "System.Windows.Controls.Border", "action button control template root");
        Type visualStateManagerType = GetRequiredType(actionButtonTemplateRoot.GetType().Assembly, "System.Windows.VisualStateManager");
        object visualStateGroups = InvokeStatic(visualStateManagerType, "GetVisualStateGroups", actionButtonTemplateRoot);
        AssertAtLeast(1, GetCount(visualStateGroups), "action button visual state group count");
        object actionButtonBackground = GetProperty(actionButton, "Background");
        AssertType(actionButtonBackground, "System.Windows.Media.SolidColorBrush", "action button dynamic resource background");
        AssertEqual("#FF356D9E", GetProperty(actionButtonBackground, "Color").ToString() ?? string.Empty, "action button background color");

        object clickStatus = Invoke(window, "FindName", "ClickStatus");
        AssertType(clickStatus, "System.Windows.Controls.TextBlock", "click status element");
        object clickStatusText = GetProperty(clickStatus, "Text");
        if (object.Equals("not clicked", clickStatusText))
        {
            InvokeVoid(actionButton, "OnClick");
        }

        AssertEqual("clicked", GetProperty(clickStatus, "Text"), "click status after generated event");

        object commandBindings = GetProperty(window, "CommandBindings");
        object commandBinding = EnumerateObjects(commandBindings).FirstOrDefault()
            ?? throw new InvalidOperationException("Expected an SDK smoke Window.CommandBindings entry.");
        object commandBindingCommand = GetProperty(commandBinding, "Command");
        AssertType(commandBindingCommand, "System.Windows.Input.RoutedUICommand", "window command binding command");
        AssertEqual("SmokeCommand", GetProperty(commandBindingCommand, "Name"), "window command binding command name");

        object inputBindings = GetProperty(window, "InputBindings");
        object keyBinding = EnumerateObjects(inputBindings).FirstOrDefault()
            ?? throw new InvalidOperationException("Expected an SDK smoke Window.InputBindings entry.");
        object keyBindingCommand = GetProperty(keyBinding, "Command");
        AssertType(keyBindingCommand, "System.Windows.Input.RoutedUICommand", "window key binding command");
        AssertEqual("SmokeCommand", GetProperty(keyBindingCommand, "Name"), "window key binding command name");
        AssertEqual("input binding payload", GetProperty(keyBinding, "CommandParameter"), "window key binding command parameter");
        AssertEqual("F6", GetProperty(keyBinding, "Key").ToString() ?? string.Empty, "window key binding key");
        AssertEqual("Control", GetProperty(keyBinding, "Modifiers").ToString() ?? string.Empty, "window key binding modifiers");

        object commandButton = Invoke(window, "FindName", "CommandButton");
        AssertType(commandButton, "System.Windows.Controls.Button", "command button");
        object commandButtonCommand = GetProperty(commandButton, "Command");
        AssertType(commandButtonCommand, "System.Windows.Input.RoutedUICommand", "command button command");
        AssertEqual("SmokeCommand", GetProperty(commandButtonCommand, "Name"), "command button command name");
        object commandButtonParameter = GetProperty(commandButton, "CommandParameter");
        AssertEqual("routed command payload", commandButtonParameter, "command button command parameter");
        AssertEqual(true, Invoke(commandButtonCommand, "CanExecute", commandButtonParameter, window), "command button routed command CanExecute");

        object commandStatus = Invoke(window, "FindName", "CommandStatus");
        AssertType(commandStatus, "System.Windows.Controls.TextBlock", "command status element");
        if (object.Equals("command not executed", GetProperty(commandStatus, "Text")))
        {
            InvokeVoid(commandButton, "OnClick");
            AssertEqual("routed command payload", GetProperty(window, "LastSmokeCommandParameter"), "window routed command executed parameter");
            AssertEqual("routed command payload", GetProperty(commandStatus, "Text"), "command status after routed command");
        }
        else
        {
            object lastCommandParameter = GetProperty(window, "LastSmokeCommandParameter");
            if (!object.Equals("routed command payload", lastCommandParameter)
                && !object.Equals("menu command payload", lastCommandParameter)
                && !object.Equals("context menu command payload", lastCommandParameter)
                && !object.Equals("toolbar command payload", lastCommandParameter))
            {
                throw new InvalidOperationException($"Unexpected smoke command parameter '{lastCommandParameter}'.");
            }
        }

        AssertAtLeast(1, GetProperty(window, "SmokeCommandCanExecuteCount"), "window routed command CanExecute count");
        AssertAtLeast(1, GetProperty(window, "SmokeCommandExecutionCount"), "window routed command execution count");

        object actionToolTip = GetProperty(actionButton, "ToolTip");
        AssertType(actionToolTip, "System.Windows.Controls.ToolTip", "action button tooltip");
        AssertEqual("Action tooltip content", GetProperty(actionToolTip, "Content"), "action tooltip content");
        AssertEqual("Right", GetProperty(actionToolTip, "Placement").ToString() ?? string.Empty, "action tooltip placement");

        object actionContextMenu = GetProperty(actionButton, "ContextMenu");
        AssertType(actionContextMenu, "System.Windows.Controls.ContextMenu", "action button context menu");
        object[] actionContextMenuItems = EnumerateObjects(GetProperty(actionContextMenu, "Items")).ToArray();
        AssertAtLeast(3, actionContextMenuItems.Length, "action context menu item count");
        object contextCommandMenuItem = actionContextMenuItems[0];
        AssertType(contextCommandMenuItem, "System.Windows.Controls.MenuItem", "context command menu item");
        AssertEqual("_Context command", GetProperty(contextCommandMenuItem, "Header"), "context command menu item header");
        object contextCommand = GetProperty(contextCommandMenuItem, "Command");
        AssertType(contextCommand, "System.Windows.Input.RoutedUICommand", "context command menu item command");
        object contextCommandParameter = GetProperty(contextCommandMenuItem, "CommandParameter");
        AssertEqual("context menu command payload", contextCommandParameter, "context command menu item parameter");
        int commandExecutionCountBeforeContextMenu = Convert.ToInt32(GetProperty(window, "SmokeCommandExecutionCount"));
        InvokeVoid(contextCommand, "Execute", contextCommandParameter, window);
        AssertAtLeast(commandExecutionCountBeforeContextMenu + 1, GetProperty(window, "SmokeCommandExecutionCount"), "context menu command execution count");
        AssertEqual("context menu command payload", GetProperty(window, "LastSmokeCommandParameter"), "context menu command payload observed");
        AssertEqual("context menu command payload", GetProperty(commandStatus, "Text"), "command status after context menu command");
        AssertType(actionContextMenuItems[1], "System.Windows.Controls.Separator", "action context menu separator");
        object contextCheckableMenuItem = actionContextMenuItems[2];
        AssertType(contextCheckableMenuItem, "System.Windows.Controls.MenuItem", "context checkable menu item");
        AssertEqual(true, GetProperty(contextCheckableMenuItem, "IsCheckable"), "context checkable menu item IsCheckable");
        object contextCheckableMenuItemChecked = GetProperty(contextCheckableMenuItem, "IsChecked");
        if (!object.Equals(true, contextCheckableMenuItemChecked)
            && !object.Equals(false, contextCheckableMenuItemChecked))
        {
            throw new InvalidOperationException($"Unexpected context checkable menu item checked state '{contextCheckableMenuItemChecked}'.");
        }

        SetProperty(contextCheckableMenuItem, "IsChecked", true);
        AssertEqual(true, GetProperty(contextCheckableMenuItem, "IsChecked"), "context checkable menu item checked");
        SetProperty(contextCheckableMenuItem, "IsChecked", false);
        AssertEqual(false, GetProperty(contextCheckableMenuItem, "IsChecked"), "context checkable menu item unchecked");

        object smokeMenu = Invoke(window, "FindName", "SmokeMenu");
        AssertType(smokeMenu, "System.Windows.Controls.Menu", "smoke menu");
        object smokeRootMenuItem = EnumerateObjects(GetProperty(smokeMenu, "Items")).First();
        AssertType(smokeRootMenuItem, "System.Windows.Controls.MenuItem", "smoke root menu item");
        object[] smokeMenuItems = EnumerateObjects(GetProperty(smokeRootMenuItem, "Items")).ToArray();
        AssertAtLeast(4, smokeMenuItems.Length, "smoke root menu item count");
        AssertType(smokeMenuItems[2], "System.Windows.Controls.Separator", "smoke menu separator");

        object commandMenuItem = Invoke(window, "FindName", "CommandMenuItem");
        AssertType(commandMenuItem, "System.Windows.Controls.MenuItem", "command menu item");
        object commandMenuItemCommand = GetProperty(commandMenuItem, "Command");
        AssertType(commandMenuItemCommand, "System.Windows.Input.RoutedUICommand", "command menu item command");
        AssertEqual("SmokeCommand", GetProperty(commandMenuItemCommand, "Name"), "command menu item command name");
        object commandMenuItemParameter = GetProperty(commandMenuItem, "CommandParameter");
        AssertEqual("menu command payload", commandMenuItemParameter, "command menu item command parameter");
        AssertSame(window, GetProperty(commandMenuItem, "CommandTarget"), "command menu item command target");
        AssertEqual(true, Invoke(commandMenuItemCommand, "CanExecute", commandMenuItemParameter, window), "command menu routed command CanExecute");
        int commandExecutionCountBeforeMenu = Convert.ToInt32(GetProperty(window, "SmokeCommandExecutionCount"));
        InvokeVoid(commandMenuItemCommand, "Execute", commandMenuItemParameter, window);
        AssertAtLeast(commandExecutionCountBeforeMenu + 1, GetProperty(window, "SmokeCommandExecutionCount"), "window routed command execution count after menu");
        AssertEqual("menu command payload", GetProperty(window, "LastSmokeCommandParameter"), "window routed command menu parameter");
        AssertEqual("menu command payload", GetProperty(commandStatus, "Text"), "command status after menu routed command");

        object menuStatus = Invoke(window, "FindName", "MenuStatus");
        AssertType(menuStatus, "System.Windows.Controls.TextBlock", "menu status element");
        object clickMenuItem = Invoke(window, "FindName", "ClickMenuItem");
        AssertType(clickMenuItem, "System.Windows.Controls.MenuItem", "click menu item");
        RaiseRoutedEvent(clickMenuItem, "ClickEvent");
        AssertAtLeast(1, GetProperty(window, "MenuClickCount"), "window menu click count");
        AssertEqual("menu click", GetProperty(menuStatus, "Text"), "menu status after click item");

        object checkableMenuItem = Invoke(window, "FindName", "CheckableMenuItem");
        AssertType(checkableMenuItem, "System.Windows.Controls.MenuItem", "checkable menu item");
        AssertEqual(true, GetProperty(checkableMenuItem, "IsCheckable"), "checkable menu item IsCheckable");
        AssertEqual(true, GetProperty(checkableMenuItem, "IsChecked"), "checkable menu item initial IsChecked");
        SetProperty(checkableMenuItem, "IsChecked", false);
        AssertEqual(false, GetProperty(checkableMenuItem, "IsChecked"), "checkable menu item toggled unchecked");
        AssertAtLeast(1, GetProperty(window, "MenuUncheckedCount"), "window menu unchecked count");
        AssertEqual("menu unchecked", GetProperty(menuStatus, "Text"), "menu status after unchecked item");
        SetProperty(checkableMenuItem, "IsChecked", true);
        AssertEqual(true, GetProperty(checkableMenuItem, "IsChecked"), "checkable menu item toggled checked");
        AssertAtLeast(1, GetProperty(window, "MenuCheckedCount"), "window menu checked count");
        AssertEqual("menu checked", GetProperty(menuStatus, "Text"), "menu status after checked item");

        object propertyTriggerStatus = Invoke(window, "FindName", "PropertyTriggerStatus");
        AssertType(propertyTriggerStatus, "System.Windows.Controls.TextBlock", "property trigger status element");
        AssertEqual("property trigger active", GetProperty(propertyTriggerStatus, "Text"), "property trigger text");
        object propertyTriggerForeground = GetProperty(propertyTriggerStatus, "Foreground");
        AssertType(propertyTriggerForeground, "System.Windows.Media.SolidColorBrush", "property trigger foreground");
        AssertEqual("#FF356D9E", GetProperty(propertyTriggerForeground, "Color").ToString() ?? string.Empty, "property trigger foreground color");

        object dataTriggerStatus = Invoke(window, "FindName", "DataTriggerStatus");
        AssertType(dataTriggerStatus, "System.Windows.Controls.TextBlock", "data trigger status element");
        AssertEqual("data trigger active", GetProperty(dataTriggerStatus, "Text"), "data trigger text");
        object dataTriggerForeground = GetProperty(dataTriggerStatus, "Foreground");
        AssertType(dataTriggerForeground, "System.Windows.Media.SolidColorBrush", "data trigger foreground");
        AssertEqual("#FF6B8F3A", GetProperty(dataTriggerForeground, "Color").ToString() ?? string.Empty, "data trigger foreground color");

        object multiTriggerStatus = Invoke(window, "FindName", "MultiTriggerStatus");
        AssertType(multiTriggerStatus, "System.Windows.Controls.TextBlock", "multi trigger status element");
        AssertEqual("multi trigger active", GetProperty(multiTriggerStatus, "Text"), "multi trigger text");
        object multiTriggerForeground = GetProperty(multiTriggerStatus, "Foreground");
        AssertType(multiTriggerForeground, "System.Windows.Media.SolidColorBrush", "multi trigger foreground");
        AssertEqual("#FF356D9E", GetProperty(multiTriggerForeground, "Color").ToString() ?? string.Empty, "multi trigger foreground color");

        object multiDataTriggerStatus = Invoke(window, "FindName", "MultiDataTriggerStatus");
        AssertType(multiDataTriggerStatus, "System.Windows.Controls.TextBlock", "multi data trigger status element");
        AssertEqual("multi data trigger active", GetProperty(multiDataTriggerStatus, "Text"), "multi data trigger text");
        object multiDataTriggerForeground = GetProperty(multiDataTriggerStatus, "Foreground");
        AssertType(multiDataTriggerForeground, "System.Windows.Media.SolidColorBrush", "multi data trigger foreground");
        AssertEqual("#FF6B8F3A", GetProperty(multiDataTriggerForeground, "Color").ToString() ?? string.Empty, "multi data trigger foreground color");

        object basedOnResourceText = Invoke(window, "FindName", "BasedOnResourceText");
        AssertType(basedOnResourceText, "System.Windows.Controls.TextBlock", "based-on resource text element");
        AssertEqual("based-on resource style", GetProperty(basedOnResourceText, "Text"), "based-on resource text");
        AssertEqual("SemiBold", GetProperty(basedOnResourceText, "FontWeight").ToString() ?? string.Empty, "based-on resource inherited font weight");
        object basedOnResourceForeground = GetProperty(basedOnResourceText, "Foreground");
        AssertType(basedOnResourceForeground, "System.Windows.Media.SolidColorBrush", "based-on resource foreground");
        AssertEqual("#FF356D9E", GetProperty(basedOnResourceForeground, "Color").ToString() ?? string.Empty, "based-on resource foreground color");

        object providerGreetingText = Invoke(window, "FindName", "ProviderGreetingText");
        AssertType(providerGreetingText, "System.Windows.Controls.TextBlock", "provider greeting text element");
        AssertEqual("provider:7", GetProperty(providerGreetingText, "Text"), "provider greeting text");
        object xmlSmokeData = Invoke(window, "FindResource", "XmlSmokeData");
        AssertType(xmlSmokeData, "System.Windows.Data.XmlDataProvider", "window XML data provider");
        object xmlProviderText = Invoke(window, "FindName", "XmlProviderText");
        AssertType(xmlProviderText, "System.Windows.Controls.TextBlock", "XML provider text element");
        AssertEqual("xml", GetProperty(xmlProviderText, "Text"), "XML provider XPath binding text");

        object unsharedBrushBorder = Invoke(window, "FindName", "UnsharedBrushBorder");
        AssertType(unsharedBrushBorder, "System.Windows.Controls.Border", "unshared brush border");
        object unsharedBorderBrush = GetProperty(unsharedBrushBorder, "Background");
        AssertType(unsharedBorderBrush, "System.Windows.Media.SolidColorBrush", "unshared border brush");
        AssertEqual("#FFC45A2B", GetProperty(unsharedBorderBrush, "Color").ToString() ?? string.Empty, "unshared border brush color");

        object inputBox = Invoke(window, "FindName", "InputBox");
        AssertType(inputBox, "System.Windows.Controls.TextBox", "input box");
        AssertEqual("editable package text", GetProperty(inputBox, "Text"), "input box bound text");
        object mutableStatusText = Invoke(window, "FindName", "MutableStatusText");
        AssertType(mutableStatusText, "System.Windows.Controls.TextBlock", "mutable status text element");
        AssertEqual("initial binding status", GetProperty(mutableStatusText, "Text"), "mutable status initial binding text");
        object validatedInputBox = Invoke(window, "FindName", "ValidatedInputBox");
        AssertType(validatedInputBox, "System.Windows.Controls.TextBox", "validated input box");
        AssertEqual("valid package text", GetProperty(validatedInputBox, "Text"), "validated input box initial text");
        object validationStatus = Invoke(window, "FindName", "ValidationStatus");
        AssertType(validationStatus, "System.Windows.Controls.TextBlock", "validation status element");
        Type validationType = GetRequiredType(validatedInputBox.GetType().Assembly, "System.Windows.Controls.Validation");
        AssertEqual(false, InvokeStatic(validationType, "GetHasError", validatedInputBox), "validated input initial validation state");
        AssertEqual("validation has error: False", GetProperty(validationStatus, "Text"), "validation status initial text");
        if (validateFrameContent)
        {
            object viewModel = GetProperty(window, "DataContext");
            SetProperty(viewModel, "MutableStatus", "updated binding status");
            flushDispatcherOperations?.Invoke(window);
            AssertEqual("updated binding status", GetProperty(mutableStatusText, "Text"), "mutable status property changed binding text");
            SetProperty(validatedInputBox, "Text", string.Empty);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(true, InvokeStatic(validationType, "GetHasError", validatedInputBox), "validated input empty validation state");
            AssertEqual("validation has error: True", GetProperty(validationStatus, "Text"), "validation status empty text");
            AssertEqual("valid package text", GetProperty(viewModel, "ValidationText"), "validated input rejected source update");
            SetProperty(validatedInputBox, "Text", "corrected package text");
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(false, InvokeStatic(validationType, "GetHasError", validatedInputBox), "validated input corrected validation state");
            AssertEqual("validation has error: False", GetProperty(validationStatus, "Text"), "validation status corrected text");
            AssertEqual("corrected package text", GetProperty(viewModel, "ValidationText"), "validated input corrected source update");
        }
        object routedEventSource = Invoke(window, "FindName", "RoutedEventSource");
        AssertType(routedEventSource, "ProGPU.Wpf.SdkSwitchSmoke.SmokeRoutedEventSource", "custom routed event source");
        AssertAssignableTo(routedEventSource, "System.Windows.FrameworkElement", "custom routed event source base type");
        object routedEventStatus = Invoke(window, "FindName", "RoutedEventStatus");
        AssertType(routedEventStatus, "System.Windows.Controls.TextBlock", "custom routed event status element");
        if (object.Equals("routed event not raised", GetProperty(routedEventStatus, "Text")))
        {
            InvokeVoid(routedEventSource, "RaiseSmokeBubbled");
        }

        AssertAtLeast(1, GetProperty(window, "SmokeRoutedEventCount"), "custom routed event count");
        AssertSame(rootPanel, GetProperty(window, "LastSmokeRoutedEventSender"), "custom routed event bubbled sender");
        AssertSame(routedEventSource, GetProperty(window, "LastSmokeRoutedEventSource"), "custom routed event original source");
        AssertEqual("SmokeBubbled", GetProperty(routedEventStatus, "Text"), "custom routed event status text");

        object itemsList = Invoke(window, "FindName", "ItemsList");
        AssertType(itemsList, "System.Windows.Controls.ListBox", "items list");
        AssertEqual(1, GetProperty(itemsList, "SelectedIndex"), "items list selected index");
        AssertType(GetProperty(itemsList, "ItemTemplate"), "System.Windows.DataTemplate", "items list item template");
        AssertAtLeast(3, GetCount(GetProperty(itemsList, "Items")), "items list count");
        object selectedItem = GetProperty(itemsList, "SelectedItem");
        AssertEqual("Scene", GetProperty(selectedItem, "Name"), "selected item name");
        AssertEqual("ProGPU", GetProperty(selectedItem, "Value"), "selected item value");
        object smokeStatusBar = Invoke(window, "FindName", "SmokeStatusBar");
        AssertType(smokeStatusBar, "System.Windows.Controls.Primitives.StatusBar", "smoke status bar");
        AssertAtLeast(3, GetCount(GetProperty(smokeStatusBar, "Items")), "smoke status bar item count");
        object statusReadyItem = Invoke(window, "FindName", "StatusReadyItem");
        AssertType(statusReadyItem, "System.Windows.Controls.Primitives.StatusBarItem", "status ready item");
        AssertEqual("Ready", GetProperty(statusReadyItem, "Content"), "status ready item content");
        object statusTextBlock = Invoke(window, "FindName", "StatusTextBlock");
        AssertType(statusTextBlock, "System.Windows.Controls.TextBlock", "status text block");
        AssertEqual("status text", GetProperty(statusTextBlock, "Tag"), "status text block tag");
        AssertEqual(GetProperty(selectedItem, "Name"), GetProperty(statusTextBlock, "Text"), "status selected item text");
        object itemsCountText = Invoke(window, "FindName", "ItemsCountText");
        AssertType(itemsCountText, "System.Windows.Controls.TextBlock", "items count text element");
        AssertEqual("items: 3", GetProperty(itemsCountText, "Text"), "initial items count binding text");
        object multiBindingSummaryText = Invoke(window, "FindName", "MultiBindingSummaryText");
        AssertType(multiBindingSummaryText, "System.Windows.Controls.TextBlock", "multi binding summary text element");
        AssertEqual("Scene:ProGPU", GetProperty(multiBindingSummaryText, "Text"), "multi binding converter text");

        object selectedItemPresenter = Invoke(window, "FindName", "SelectedItemPresenter");
        AssertType(selectedItemPresenter, "System.Windows.Controls.ContentControl", "selected item presenter");
        AssertSame(selectedItem, GetProperty(selectedItemPresenter, "Content"), "selected item presenter content");
        AssertType(GetProperty(selectedItemPresenter, "ContentTemplate"), "System.Windows.DataTemplate", "selected item presenter template");

        object layoutGrid = Invoke(window, "FindName", "LayoutGrid");
        AssertType(layoutGrid, "System.Windows.Controls.Grid", "layout grid");
        AssertEqual(2, GetCount(GetProperty(layoutGrid, "RowDefinitions")), "layout grid row definition count");
        AssertEqual(2, GetCount(GetProperty(layoutGrid, "ColumnDefinitions")), "layout grid column definition count");
        Type gridType = layoutGrid.GetType();
        object layoutLabel = Invoke(window, "FindName", "LayoutLabel");
        AssertType(layoutLabel, "System.Windows.Controls.TextBlock", "layout label element");
        AssertEqual("Selected:", GetProperty(layoutLabel, "Text"), "layout label text");
        AssertEqual(0, InvokeStatic(gridType, "GetRow", layoutLabel), "layout label grid row");
        AssertEqual(0, InvokeStatic(gridType, "GetColumn", layoutLabel), "layout label grid column");
        object convertedSelectedItemText = Invoke(window, "FindName", "ConvertedSelectedItemText");
        AssertType(convertedSelectedItemText, "System.Windows.Controls.TextBlock", "converted selected item text element");
        AssertEqual("Scene=ProGPU/Rendering", GetProperty(convertedSelectedItemText, "Text"), "converted selected item text");
        AssertEqual(0, InvokeStatic(gridType, "GetRow", convertedSelectedItemText), "converted selected item grid row");
        AssertEqual(1, InvokeStatic(gridType, "GetColumn", convertedSelectedItemText), "converted selected item grid column");
        object formattedInputText = Invoke(window, "FindName", "FormattedInputText");
        AssertType(formattedInputText, "System.Windows.Controls.TextBlock", "formatted input text element");
        AssertEqual("Input: editable package text", GetProperty(formattedInputText, "Text"), "formatted input binding text");
        AssertEqual(1, InvokeStatic(gridType, "GetRow", formattedInputText), "formatted input grid row");
        AssertEqual(0, InvokeStatic(gridType, "GetColumn", formattedInputText), "formatted input grid column");
        AssertEqual(2, InvokeStatic(gridType, "GetColumnSpan", formattedInputText), "formatted input grid column span");

        object groupedItemsViewSource = Invoke(window, "FindResource", "GroupedItems");
        AssertType(groupedItemsViewSource, "System.Windows.Data.CollectionViewSource", "grouped items collection view source");
        AssertAtLeast(1, GetCount(GetProperty(groupedItemsViewSource, "SortDescriptions")), "grouped items sort description count");
        AssertAtLeast(1, GetCount(GetProperty(groupedItemsViewSource, "GroupDescriptions")), "grouped items group description count");
        object groupedItemsView = GetProperty(groupedItemsViewSource, "View");
        AssertAtLeast(1, GetCount(GetProperty(groupedItemsView, "Groups")), "grouped items view group count");

        object groupedItemsControl = Invoke(window, "FindName", "GroupedItemsControl");
        AssertType(groupedItemsControl, "System.Windows.Controls.ItemsControl", "grouped items control");
        AssertType(GetProperty(groupedItemsControl, "ItemTemplate"), "System.Windows.DataTemplate", "grouped items item template");
        object groupedItemsGroupStyle = EnumerateObjects(GetProperty(groupedItemsControl, "GroupStyle")).FirstOrDefault()
            ?? throw new InvalidOperationException("Expected a grouped items GroupStyle entry.");
        AssertType(groupedItemsGroupStyle, "System.Windows.Controls.GroupStyle", "grouped items group style");
        AssertType(GetProperty(groupedItemsGroupStyle, "HeaderTemplate"), "System.Windows.DataTemplate", "grouped items group header template");

        object selectorItemsControl = Invoke(window, "FindName", "SelectorItemsControl");
        AssertType(selectorItemsControl, "System.Windows.Controls.ItemsControl", "selector items control");
        AssertAtLeast(3, GetCount(GetProperty(selectorItemsControl, "Items")), "selector items control count");
        object itemTemplateSelector = GetProperty(selectorItemsControl, "ItemTemplateSelector");
        AssertType(itemTemplateSelector, "ProGPU.Wpf.SdkSwitchSmoke.SmokeItemTemplateSelector", "smoke item template selector");
        object frameworkItemTemplate = Invoke(window, "FindResource", "SmokeFrameworkItemTemplate");
        object renderingItemTemplate = Invoke(window, "FindResource", "SmokeRenderingItemTemplate");
        AssertType(frameworkItemTemplate, "System.Windows.DataTemplate", "framework item data template");
        AssertType(renderingItemTemplate, "System.Windows.DataTemplate", "rendering item data template");
        object firstItem = EnumerateObjects(GetProperty(itemsList, "Items")).First();
        AssertSame(frameworkItemTemplate, Invoke(itemTemplateSelector, "SelectTemplate", firstItem, selectorItemsControl), "framework item selected template");
        AssertSame(renderingItemTemplate, Invoke(itemTemplateSelector, "SelectTemplate", selectedItem, selectorItemsControl), "rendering item selected template");

        object smokeDataGrid = Invoke(window, "FindName", "SmokeDataGrid");
        AssertType(smokeDataGrid, "System.Windows.Controls.DataGrid", "smoke data grid");
        AssertEqual(false, GetProperty(smokeDataGrid, "AutoGenerateColumns"), "smoke data grid AutoGenerateColumns");
        AssertEqual(false, GetProperty(smokeDataGrid, "CanUserAddRows"), "smoke data grid CanUserAddRows");
        AssertAtLeast(3, GetCount(GetProperty(smokeDataGrid, "Items")), "smoke data grid item count");
        AssertEqual(1, GetProperty(smokeDataGrid, "SelectedIndex"), "smoke data grid initial selected index");
        object dataGridSelectedItem = GetProperty(smokeDataGrid, "SelectedItem");
        AssertEqual("Scene", GetProperty(dataGridSelectedItem, "Name"), "smoke data grid selected item name");
        AssertEqual(false, GetProperty(dataGridSelectedItem, "IsActive"), "smoke data grid selected item active");
        object[] dataGridColumns = EnumerateObjects(GetProperty(smokeDataGrid, "Columns")).ToArray();
        AssertEqual(3, dataGridColumns.Length, "smoke data grid column count");
        AssertType(dataGridColumns[0], "System.Windows.Controls.DataGridTextColumn", "smoke data grid name column");
        AssertEqual("Name", GetProperty(dataGridColumns[0], "Header"), "smoke data grid name column header");
        object dataGridNameBinding = GetProperty(dataGridColumns[0], "Binding");
        AssertType(dataGridNameBinding, "System.Windows.Data.Binding", "smoke data grid name binding");
        AssertEqual("Name", GetBindingPath(dataGridNameBinding), "smoke data grid name binding path");
        AssertType(dataGridColumns[1], "System.Windows.Controls.DataGridTextColumn", "smoke data grid category column");
        AssertEqual("Category", GetProperty(dataGridColumns[1], "Header"), "smoke data grid category column header");
        object dataGridCategoryBinding = GetProperty(dataGridColumns[1], "Binding");
        AssertType(dataGridCategoryBinding, "System.Windows.Data.Binding", "smoke data grid category binding");
        AssertEqual("Category", GetBindingPath(dataGridCategoryBinding), "smoke data grid category binding path");
        AssertType(dataGridColumns[2], "System.Windows.Controls.DataGridCheckBoxColumn", "smoke data grid active column");
        AssertEqual("Active", GetProperty(dataGridColumns[2], "Header"), "smoke data grid active column header");
        object dataGridActiveBinding = GetProperty(dataGridColumns[2], "Binding");
        AssertType(dataGridActiveBinding, "System.Windows.Data.Binding", "smoke data grid active binding");
        AssertEqual("IsActive", GetBindingPath(dataGridActiveBinding), "smoke data grid active binding path");
        object dataGridStatus = Invoke(window, "FindName", "DataGridStatus");
        AssertType(dataGridStatus, "System.Windows.Controls.TextBlock", "data grid status element");
        AssertEqual("data grid: Scene", GetProperty(dataGridStatus, "Text"), "data grid initial selected text");
        if (validateFrameContent)
        {
            SetProperty(smokeDataGrid, "SelectedIndex", 2);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(2, GetProperty(smokeDataGrid, "SelectedIndex"), "smoke data grid changed selected index");
            object changedDataGridSelectedItem = GetProperty(smokeDataGrid, "SelectedItem");
            AssertEqual("XAML", GetProperty(changedDataGridSelectedItem, "Name"), "smoke data grid changed selected item");
            AssertEqual(true, GetProperty(changedDataGridSelectedItem, "IsActive"), "smoke data grid changed selected active");
            AssertEqual("data grid: XAML", GetProperty(dataGridStatus, "Text"), "data grid changed selected text");
            SetProperty(smokeDataGrid, "SelectedIndex", 1);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual("data grid: Scene", GetProperty(dataGridStatus, "Text"), "data grid restored selected text");
        }

        object smokeComboBox = Invoke(window, "FindName", "SmokeComboBox");
        AssertType(smokeComboBox, "System.Windows.Controls.ComboBox", "smoke combo box");
        AssertAtLeast(3, GetCount(GetProperty(smokeComboBox, "Items")), "smoke combo box item count");
        AssertEqual("Name", GetProperty(smokeComboBox, "DisplayMemberPath"), "smoke combo box display member path");
        AssertEqual("Value", GetProperty(smokeComboBox, "SelectedValuePath"), "smoke combo box selected value path");
        AssertEqual(1, GetProperty(smokeComboBox, "SelectedIndex"), "smoke combo box initial selected index");
        AssertEqual("ProGPU", GetProperty(smokeComboBox, "SelectedValue"), "smoke combo box initial selected value");
        object comboSelectedItem = GetProperty(smokeComboBox, "SelectedItem");
        AssertEqual("Scene", GetProperty(comboSelectedItem, "Name"), "smoke combo box initial selected item name");
        object selectorStatus = Invoke(window, "FindName", "SelectorStatus");
        AssertType(selectorStatus, "System.Windows.Controls.TextBlock", "selector status element");
        if (validateFrameContent)
        {
            int selectorSelectionCountBefore = Convert.ToInt32(GetProperty(window, "SelectorSelectionChangedCount"));
            SetProperty(smokeComboBox, "SelectedIndex", 2);
            AssertEqual(2, GetProperty(smokeComboBox, "SelectedIndex"), "smoke combo box changed selected index");
            AssertEqual("compiled", GetProperty(smokeComboBox, "SelectedValue"), "smoke combo box changed selected value");
            object changedComboSelectedItem = GetProperty(smokeComboBox, "SelectedItem");
            AssertEqual("XAML", GetProperty(changedComboSelectedItem, "Name"), "smoke combo box changed selected item");
            AssertAtLeast(selectorSelectionCountBefore + 1, GetProperty(window, "SelectorSelectionChangedCount"), "selector selection changed count");
            AssertEqual("selector selected: compiled", GetProperty(selectorStatus, "Text"), "selector status after combo selection");
        }

        object smokeTabs = Invoke(window, "FindName", "SmokeTabs");
        AssertType(smokeTabs, "System.Windows.Controls.TabControl", "smoke tab control");
        AssertAtLeast(2, GetCount(GetProperty(smokeTabs, "Items")), "smoke tab item count");
        AssertEqual(1, GetProperty(smokeTabs, "SelectedIndex"), "smoke tab initial selected index");
        object frameworkTab = Invoke(window, "FindName", "FrameworkTab");
        AssertType(frameworkTab, "System.Windows.Controls.TabItem", "framework tab item");
        AssertEqual("Framework", GetProperty(frameworkTab, "Header"), "framework tab header");
        object renderingTab = Invoke(window, "FindName", "RenderingTab");
        AssertType(renderingTab, "System.Windows.Controls.TabItem", "rendering tab item");
        AssertEqual("Rendering", GetProperty(renderingTab, "Header"), "rendering tab header");
        AssertSame(renderingTab, GetProperty(smokeTabs, "SelectedItem"), "smoke tab initial selected item");
        object tabStatus = Invoke(window, "FindName", "TabStatus");
        AssertType(tabStatus, "System.Windows.Controls.TextBlock", "tab status element");
        if (validateFrameContent)
        {
            int tabSelectionCountBefore = Convert.ToInt32(GetProperty(window, "TabSelectionChangedCount"));
            SetProperty(smokeTabs, "SelectedIndex", 0);
            AssertEqual(0, GetProperty(smokeTabs, "SelectedIndex"), "smoke tab changed selected index");
            AssertSame(frameworkTab, GetProperty(smokeTabs, "SelectedItem"), "smoke tab changed selected item");
            AssertAtLeast(tabSelectionCountBefore + 1, GetProperty(window, "TabSelectionChangedCount"), "tab selection changed count");
            AssertEqual("tab selected: Framework", GetProperty(tabStatus, "Text"), "tab status after tab selection");
        }

        object smokeToolBarTray = Invoke(window, "FindName", "SmokeToolBarTray");
        AssertType(smokeToolBarTray, "System.Windows.Controls.ToolBarTray", "smoke toolbar tray");
        AssertAtLeast(1, GetCount(GetProperty(smokeToolBarTray, "ToolBars")), "smoke toolbar tray toolbar count");
        object smokeToolBar = Invoke(window, "FindName", "SmokeToolBar");
        AssertType(smokeToolBar, "System.Windows.Controls.ToolBar", "smoke toolbar");
        AssertEqual("Smoke tools", GetProperty(smokeToolBar, "Header"), "smoke toolbar header");
        AssertAtLeast(3, GetCount(GetProperty(smokeToolBar, "Items")), "smoke toolbar item count");

        object toolBarCommandButton = Invoke(window, "FindName", "ToolBarCommandButton");
        AssertType(toolBarCommandButton, "System.Windows.Controls.Button", "toolbar command button");
        object toolbarCommand = GetProperty(toolBarCommandButton, "Command");
        AssertType(toolbarCommand, "System.Windows.Input.RoutedUICommand", "toolbar command button command");
        object toolbarCommandParameter = GetProperty(toolBarCommandButton, "CommandParameter");
        AssertEqual("toolbar command payload", toolbarCommandParameter, "toolbar command parameter");
        AssertSame(window, GetProperty(toolBarCommandButton, "CommandTarget"), "toolbar command target");
        int commandExecutionCountBeforeToolbar = Convert.ToInt32(GetProperty(window, "SmokeCommandExecutionCount"));
        InvokeVoid(toolBarCommandButton, "OnClick");
        AssertAtLeast(commandExecutionCountBeforeToolbar + 1, GetProperty(window, "SmokeCommandExecutionCount"), "toolbar command execution count");
        AssertEqual("toolbar command payload", GetProperty(window, "LastSmokeCommandParameter"), "toolbar command payload observed");
        AssertEqual("toolbar command payload", GetProperty(commandStatus, "Text"), "command status after toolbar command");

        object toolBarSeparator = Invoke(window, "FindName", "ToolBarSeparator");
        AssertType(toolBarSeparator, "System.Windows.Controls.Separator", "toolbar separator");
        object toolBarToggle = Invoke(window, "FindName", "ToolBarToggle");
        AssertType(toolBarToggle, "System.Windows.Controls.Primitives.ToggleButton", "toolbar toggle");
        object toolBarToggleChecked = GetProperty(toolBarToggle, "IsChecked");
        if (!object.Equals(true, toolBarToggleChecked)
            && !object.Equals(false, toolBarToggleChecked))
        {
            throw new InvalidOperationException($"Unexpected toolbar toggle checked state '{toolBarToggleChecked}'.");
        }

        SetProperty(toolBarToggle, "IsChecked", true);
        AssertEqual(true, GetProperty(toolBarToggle, "IsChecked"), "toolbar toggle checked");
        SetProperty(toolBarToggle, "IsChecked", false);
        AssertEqual(false, GetProperty(toolBarToggle, "IsChecked"), "toolbar toggle unchecked");

        object smokeGroupBox = Invoke(window, "FindName", "SmokeGroupBox");
        AssertType(smokeGroupBox, "System.Windows.Controls.GroupBox", "smoke group box");
        AssertEqual("Managed range", GetProperty(smokeGroupBox, "Header"), "smoke group box header");
        AssertType(GetProperty(smokeGroupBox, "Content"), "System.Windows.Controls.StackPanel", "smoke group box content");

        object smokeExpander = Invoke(window, "FindName", "SmokeExpander");
        AssertType(smokeExpander, "System.Windows.Controls.Expander", "smoke expander");
        AssertEqual("Range details", GetProperty(smokeExpander, "Header"), "smoke expander header");
        object smokeExpanderIsExpanded = GetProperty(smokeExpander, "IsExpanded");
        if (!object.Equals(true, smokeExpanderIsExpanded)
            && !object.Equals(false, smokeExpanderIsExpanded))
        {
            throw new InvalidOperationException($"Unexpected smoke expander state '{smokeExpanderIsExpanded}'.");
        }

        object smokeScrollViewer = Invoke(window, "FindName", "SmokeScrollViewer");
        AssertType(smokeScrollViewer, "System.Windows.Controls.ScrollViewer", "smoke scroll viewer");
        AssertEqual("Auto", GetProperty(smokeScrollViewer, "VerticalScrollBarVisibility").ToString() ?? string.Empty, "smoke scroll viewer vertical visibility");
        AssertEqual("Disabled", GetProperty(smokeScrollViewer, "HorizontalScrollBarVisibility").ToString() ?? string.Empty, "smoke scroll viewer horizontal visibility");
        object scrollContentPanel = Invoke(window, "FindName", "ScrollContentPanel");
        AssertType(scrollContentPanel, "System.Windows.Controls.StackPanel", "scroll content panel");
        AssertAtLeast(3, GetCount(GetProperty(scrollContentPanel, "Children")), "scroll content child count");

        object rangeStatus = Invoke(window, "FindName", "RangeStatus");
        AssertType(rangeStatus, "System.Windows.Controls.TextBlock", "range status element");
        SetProperty(smokeExpander, "IsExpanded", true);
        int expanderCollapsedCountBefore = Convert.ToInt32(GetProperty(window, "ExpanderCollapsedCount"));
        SetProperty(smokeExpander, "IsExpanded", false);
        AssertEqual(false, GetProperty(smokeExpander, "IsExpanded"), "smoke expander collapsed state");
        AssertAtLeast(expanderCollapsedCountBefore + 1, GetProperty(window, "ExpanderCollapsedCount"), "smoke expander collapsed count");
        AssertEqual("range collapsed", GetProperty(rangeStatus, "Text"), "range status after collapse");
        int expanderExpandedCountBefore = Convert.ToInt32(GetProperty(window, "ExpanderExpandedCount"));
        SetProperty(smokeExpander, "IsExpanded", true);
        AssertEqual(true, GetProperty(smokeExpander, "IsExpanded"), "smoke expander expanded state");
        AssertAtLeast(expanderExpandedCountBefore + 1, GetProperty(window, "ExpanderExpandedCount"), "smoke expander expanded count");
        AssertEqual("range expanded", GetProperty(rangeStatus, "Text"), "range status after expand");

        object smokeSlider = Invoke(window, "FindName", "SmokeSlider");
        AssertType(smokeSlider, "System.Windows.Controls.Slider", "smoke slider");
        AssertEqual(0.0, GetProperty(smokeSlider, "Minimum"), "smoke slider minimum");
        AssertEqual(10.0, GetProperty(smokeSlider, "Maximum"), "smoke slider maximum");
        object smokeProgressBar = Invoke(window, "FindName", "SmokeProgressBar");
        AssertType(smokeProgressBar, "System.Windows.Controls.ProgressBar", "smoke progress bar");
        AssertEqual(0.0, GetProperty(smokeProgressBar, "Minimum"), "smoke progress bar minimum");
        AssertEqual(10.0, GetProperty(smokeProgressBar, "Maximum"), "smoke progress bar maximum");
        SetProperty(smokeSlider, "Value", 4.0);
        flushDispatcherOperations?.Invoke(window);
        AssertEqual(4.0, GetProperty(smokeSlider, "Value"), "smoke slider normalized value");
        AssertEqual(4.0, GetProperty(smokeProgressBar, "Value"), "smoke progress bound value");
        int rangeValueChangedCountBefore = Convert.ToInt32(GetProperty(window, "RangeValueChangedCount"));
        SetProperty(smokeSlider, "Value", 6.5);
        flushDispatcherOperations?.Invoke(window);
        AssertEqual(6.5, GetProperty(smokeSlider, "Value"), "smoke slider changed value");
        AssertEqual(6.5, GetProperty(smokeProgressBar, "Value"), "smoke progress changed bound value");
        AssertAtLeast(rangeValueChangedCountBefore + 1, GetProperty(window, "RangeValueChangedCount"), "range value changed count");
        AssertEqual("range value: 6.5", GetProperty(rangeStatus, "Text"), "range status after slider value");

        if (validateFrameContent)
        {
            object viewModel = GetProperty(window, "DataContext");
            object items = GetProperty(viewModel, "Items");
            object dynamicItem = Create(selectedItem.GetType(), "Binding", "dynamic", "Framework");
            InvokeVoid(items, "Add", dynamicItem);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(4, GetCount(GetProperty(itemsList, "Items")), "items list count after collection change");
            AssertEqual(4, GetCount(GetProperty(selectorItemsControl, "Items")), "selector items count after collection change");
            AssertEqual(4, GetCount(GetProperty(smokeDataGrid, "Items")), "data grid items count after collection change");
            AssertEqual("items: 4", GetProperty(itemsCountText, "Text"), "items count binding text after collection change");
            AssertSame(frameworkItemTemplate, Invoke(itemTemplateSelector, "SelectTemplate", dynamicItem, selectorItemsControl), "dynamic framework item selected template");
        }
        object hierarchyTree = Invoke(window, "FindName", "HierarchyTree");
        AssertType(hierarchyTree, "System.Windows.Controls.TreeView", "hierarchy tree");
        AssertAtLeast(3, GetCount(GetProperty(hierarchyTree, "Items")), "hierarchy tree root item count");
        object hierarchyTemplate = GetProperty(hierarchyTree, "ItemTemplate");
        AssertType(hierarchyTemplate, "System.Windows.HierarchicalDataTemplate", "hierarchy tree item template");
        AssertType(GetProperty(hierarchyTemplate, "ItemsSource"), "System.Windows.Data.Binding", "hierarchy item source binding");
        object hierarchyTemplateRoot = Invoke(hierarchyTemplate, "LoadContent");
        AssertType(hierarchyTemplateRoot, "System.Windows.Controls.TextBlock", "hierarchy template root");
        object firstItemChildren = GetProperty(firstItem, "Children");
        AssertAtLeast(1, GetCount(firstItemChildren), "hierarchy first item child count");
        object firstChild = EnumerateObjects(firstItemChildren).First();
        AssertEqual("Startup", GetProperty(firstChild, "Name"), "hierarchy first child name");

        object compiledSmokePanel = Invoke(window, "FindName", "CompiledSmokePanel");
        AssertType(compiledSmokePanel, "ProGPU.Wpf.SdkSwitchSmoke.SmokePanel", "compiled user control");
        AssertAssignableTo(compiledSmokePanel, "System.Windows.Controls.UserControl", "compiled user control base type");
        AssertEqual("Compiled user control", GetProperty(compiledSmokePanel, "Caption"), "compiled user control dependency property");
        AssertEqual("ProGPU", GetProperty(compiledSmokePanel, "PanelContent"), "compiled user control bound dependency property");
        object panelCaption = Invoke(compiledSmokePanel, "FindName", "PanelCaption");
        AssertType(panelCaption, "System.Windows.Controls.TextBlock", "compiled user control caption element");
        AssertEqual("Compiled user control", GetProperty(panelCaption, "Text"), "compiled user control element-name binding");
        object panelRelativeCaption = Invoke(compiledSmokePanel, "FindName", "PanelRelativeCaption");
        AssertType(panelRelativeCaption, "System.Windows.Controls.TextBlock", "compiled user control relative-source element");
        AssertEqual("Compiled user control", GetProperty(panelRelativeCaption, "Text"), "compiled user control relative-source binding");
        object panelContentPresenter = Invoke(compiledSmokePanel, "FindName", "PanelContentPresenter");
        AssertType(panelContentPresenter, "System.Windows.Controls.ContentPresenter", "compiled user control content presenter");
        AssertEqual("ProGPU", GetProperty(panelContentPresenter, "Content"), "compiled user control content binding");

        object themedSmokeControl = Invoke(window, "FindName", "ThemedSmokeControl");
        AssertType(themedSmokeControl, "ProGPU.Wpf.SdkSwitchSmoke.SmokeThemedControl", "themed custom control");
        AssertAssignableTo(themedSmokeControl, "System.Windows.Controls.Control", "themed custom control base type");
        AssertEqual("Generic theme default style", GetProperty(themedSmokeControl, "Text"), "themed custom control dependency property");
        Invoke(themedSmokeControl, "ApplyTemplate");
        object themedControlTemplate = GetProperty(themedSmokeControl, "Template");
        AssertType(themedControlTemplate, "System.Windows.Controls.ControlTemplate", "themed custom control default template");
        object themedTemplateText = Invoke(themedControlTemplate, "FindName", "ThemeText", themedSmokeControl);
        AssertType(themedTemplateText, "System.Windows.Controls.TextBlock", "themed custom control template text");
        AssertEqual("Generic theme default style", GetProperty(themedTemplateText, "Text"), "themed custom control template binding");
        object themedTemplateForeground = GetProperty(themedTemplateText, "Foreground");
        AssertType(themedTemplateForeground, "System.Windows.Media.SolidColorBrush", "themed custom control foreground");
        AssertEqual("#FF356D9E", GetProperty(themedTemplateForeground, "Color").ToString() ?? string.Empty, "themed custom control foreground color");
        object themedTemplateRoot = Invoke(themedControlTemplate, "FindName", "ThemeRoot", themedSmokeControl);
        AssertType(themedTemplateRoot, "System.Windows.Controls.Border", "themed custom control template root");
        object themedTemplateBackground = GetProperty(themedTemplateRoot, "Background");
        AssertType(themedTemplateBackground, "System.Windows.Media.SolidColorBrush", "themed custom control background");
        AssertEqual("#FF6B8F3A", GetProperty(themedTemplateBackground, "Color").ToString() ?? string.Empty, "themed custom control background color");

        object smokeFrame = Invoke(window, "FindName", "SmokeFrame");
        AssertType(smokeFrame, "System.Windows.Controls.Frame", "compiled page frame");
        string smokeFrameSource = GetProperty(smokeFrame, "Source").ToString() ?? string.Empty;
        AssertEqual(true, smokeFrameSource.Contains("ProGPU.Wpf.SdkSwitchSmoke", StringComparison.Ordinal), "compiled page frame source assembly");
        AssertEqual(true, smokeFrameSource.EndsWith("component/SmokePage.xaml", StringComparison.Ordinal), "compiled page frame source component path");
        if (validateFrameContent)
        {
            object smokePage = GetProperty(smokeFrame, "Content");
            AssertType(smokePage, "ProGPU.Wpf.SdkSwitchSmoke.SmokePage", "compiled frame page");
            AssertAssignableTo(smokePage, "System.Windows.Controls.Page", "compiled frame page base type");
            AssertEqual("Compiled Smoke Page", GetProperty(smokePage, "Title"), "compiled page title");
            object pageTitle = Invoke(smokePage, "FindName", "PageTitle");
            AssertType(pageTitle, "System.Windows.Controls.TextBlock", "compiled page title element");
            AssertEqual("Compiled page content", GetProperty(pageTitle, "Text"), "compiled page title text");
            object pageTitleForeground = GetProperty(pageTitle, "Foreground");
            AssertType(pageTitleForeground, "System.Windows.Media.SolidColorBrush", "compiled page dynamic resource foreground");
            AssertEqual("#FF356D9E", GetProperty(pageTitleForeground, "Color").ToString() ?? string.Empty, "compiled page dynamic resource foreground color");
            object pageSubtitle = Invoke(smokePage, "FindName", "PageSubtitle");
            AssertType(pageSubtitle, "System.Windows.Controls.TextBlock", "compiled page subtitle element");
            AssertEqual("Frame loaded SDK-built BAML", GetProperty(pageSubtitle, "Text"), "compiled page subtitle text");
        }

        object documentBox = Invoke(window, "FindName", "DocumentBox");
        AssertType(documentBox, "System.Windows.Controls.RichTextBox", "rich text box");
        object document = GetProperty(documentBox, "Document");
        AssertType(document, "System.Windows.Documents.FlowDocument", "rich text document");
        AssertAtLeast(2, GetCount(GetProperty(document, "Blocks")), "rich text block count");
    }

    private static SdkApplicationRunRecorder RegisterPortableActivation(
        Assembly presentationFramework,
        Assembly presentationCore,
        object application,
        out Type activationServiceType)
    {
        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        MethodInfo register = activationServiceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "Register");

        var recorder = new SdkApplicationRunRecorder(
            presentationCore,
            application,
            activationServiceType);
        recorder.RegisterMediaContextRenderService();

        register.Invoke(
            null,
            new object?[]
            {
                new Func<object, object>(recorder.Activate),
                new Action<object>(recorder.Show),
                new Action<object>(recorder.Hide),
                new Action<object, object>(recorder.SetWindowState),
                new Action<object, string>(recorder.SetTitle),
                new Action<object, double, double>(recorder.SetClientSize),
                new Action<object>(recorder.Close),
                new Action<object>(recorder.Run),
                new Action<object>(recorder.Dispose)
            });

        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");
        return recorder;
    }

    private static void ClearPortableActivation(Type? activationServiceType)
    {
        activationServiceType?.GetMethod(
            "Clear",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
    }

    private static object Create(Assembly assembly, string typeName)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not create '{typeName}'.");
    }

    private static object Create(Type type, params object?[] args)
    {
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args,
            culture: null)
            ?? throw new InvalidOperationException($"Could not create '{type.FullName}'.");
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)!
            ?? throw new TypeLoadException(typeName);
    }

    private static object Invoke(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        try
        {
            return method.Invoke(instance, args)
                ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeVoid(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        try
        {
            method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void TryInvoke(object instance, string methodName)
    {
        MethodInfo? method = GetCompatibleMethod(instance.GetType(), methodName, Array.Empty<object?>());
        try
        {
            method?.Invoke(instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void RaiseRoutedEvent(object source, string routedEventFieldName)
    {
        FieldInfo field = source.GetType().GetField(
            routedEventFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(source.GetType().FullName, routedEventFieldName);
        object routedEvent = field.GetValue(null)
            ?? throw new InvalidOperationException($"Routed event field '{routedEventFieldName}' returned null.");
        Type eventArgsType = GetRequiredType(routedEvent.GetType().Assembly, "System.Windows.RoutedEventArgs");
        object eventArgs = Activator.CreateInstance(eventArgsType, routedEvent, source)
            ?? throw new InvalidOperationException("Could not create RoutedEventArgs.");
        InvokeVoid(source, "RaiseEvent", eventArgs);
    }

    private static object InvokeStatic(Type type, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleStaticMethod(type, methodName, args)
            ?? throw new MissingMethodException(type.FullName, methodName);

        try
        {
            return method.Invoke(null, args)
                ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeStaticVoid(Type type, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleStaticMethod(type, methodName, args)
            ?? throw new MissingMethodException(type.FullName, methodName);

        try
        {
            method.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static MethodInfo? GetCompatibleMethod(Type type, string methodName, object?[] args)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => ParametersMatch(method.GetParameters(), args))
            .OrderBy(method => GetDeclaringTypeDistance(type, method.DeclaringType))
            .FirstOrDefault();
    }

    private static MethodInfo? GetCompatibleStaticMethod(Type type, string methodName, object?[] args)
    {
        return type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => ParametersMatch(method.GetParameters(), args))
            .FirstOrDefault();
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length)
        {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            object? arg = args[i];
            if (arg is null)
            {
                if (parameters[i].ParameterType.IsValueType &&
                    Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameters[i].ParameterType.IsAssignableFrom(arg.GetType()))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetDeclaringTypeDistance(Type actualType, Type? declaringType)
    {
        int distance = 0;
        for (Type? type = actualType; type is not null; type = type.BaseType)
        {
            if (type == declaringType)
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
    }

    private static object GetProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        return property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null.");
    }

    private static string GetBindingPath(object binding)
    {
        object propertyPath = GetProperty(binding, "Path");
        return GetProperty(propertyPath, "Path").ToString() ?? string.Empty;
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);
        return property.GetValue(null)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null.");
    }

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static int GetCount(object collection)
    {
        if (collection is ICollection nonGenericCollection)
        {
            return nonGenericCollection.Count;
        }

        PropertyInfo? countProperty = collection.GetType().GetProperty(
            "Count",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (countProperty?.GetValue(collection) is object count)
        {
            return Convert.ToInt32(count);
        }

        throw new MissingMemberException(collection.GetType().FullName, "Count");
    }

    private static IEnumerable<object> EnumerateObjects(object collection)
    {
        if (collection is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Object '{collection.GetType().FullName}' is not enumerable.");
        }

        foreach (object? item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static void AssertType(object value, string expectedTypeName, string description)
    {
        if (!string.Equals(value.GetType().FullName, expectedTypeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{description}: expected type '{expectedTypeName}', actual '{value.GetType().FullName}'.");
        }
    }

    private static void AssertAssignableTo(object value, string expectedBaseTypeName, string description)
    {
        for (Type? type = value.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, expectedBaseTypeName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{description}: expected assignable to '{expectedBaseTypeName}', actual '{value.GetType().FullName}'.");
    }

    private static void AssertEqual(object expected, object actual, string description)
    {
        if (!object.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertSame(object expected, object actual, string description)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected same instance.");
        }
    }

    private static void AssertNotSame(object unexpected, object actual, string description)
    {
        if (ReferenceEquals(unexpected, actual))
        {
            throw new InvalidOperationException($"{description}: expected different instances.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, object actualValue, string description)
    {
        int actual = Convert.ToInt32(actualValue);
        if (actual < expectedMinimum)
        {
            throw new InvalidOperationException($"{description}: expected at least {expectedMinimum}, actual {actual}.");
        }
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description} was not found.", path);
        }
    }

    private static void RequireAnyFile(string root, IReadOnlyList<string> fileNames, string description)
    {
        foreach (string fileName in fileNames)
        {
            if (Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Any())
            {
                return;
            }
        }

        throw new FileNotFoundException(
            $"{description} was not found under '{root}'. Expected one of: {string.Join(", ", fileNames)}.");
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{description} was not found: {path}");
        }
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "global.json")) &&
                Directory.Exists(Path.Combine(directory, "src", "Microsoft.DotNet.Wpf")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the WPF repository root.");
    }

    private sealed record SmokeInputs(
        string RepoRoot,
        string AppOutputRoot,
        string SmokeAssemblyPath,
        string WpfRoot,
        string ProGpuRoot);

    private sealed class SdkApplicationRunRecorder : IDisposable
    {
        private readonly Assembly _presentationCore;
        private readonly object _application;
        private readonly Type _activationServiceType;
        private IDisposable? _mediaContextRenderRegistration;
        private RecordingActivation? _activation;

        public SdkApplicationRunRecorder(
            Assembly presentationCore,
            object application,
            Type activationServiceType)
        {
            _presentationCore = presentationCore;
            _application = application;
            _activationServiceType = activationServiceType;
        }

        public int ActivateCount { get; private set; }

        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public int RunCount { get; private set; }

        public int RenderRequestCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void RegisterMediaContextRenderService()
        {
            Type serviceType = GetRequiredType(_presentationCore, PortableMediaContextRenderServiceTypeName);
            object registration = InvokeStatic(serviceType, "Register", new Action<TimeSpan>(RequestRender));
            _mediaContextRenderRegistration = registration as IDisposable
                ?? throw new InvalidOperationException("PortableMediaContextRenderService.Register did not return IDisposable.");
            AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable MediaContext render service enabled");
        }

        public object Activate(object window)
        {
            if (ActivateCount != 0)
            {
                throw new InvalidOperationException("Expected exactly one SDK startup window activation.");
            }

            AssertType(window, MainWindowTypeName, "activated SDK startup window");
            ValidateWindow(window, validateFrameContent: false, flushDispatcherOperations: null);

            object presentationSource = CreatePortablePresentationSource(window);
            ActivateCount++;
            _activation = new RecordingActivation(window, presentationSource)
            {
                Title = GetProperty(window, "Title").ToString() ?? string.Empty,
                Width = Convert.ToDouble(GetProperty(window, "Width")),
                Height = Convert.ToDouble(GetProperty(window, "Height"))
            };
            return _activation;
        }

        public void Show(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            ShowCount++;
            typedActivation.IsVisible = true;
            FlushDispatcherOperations(typedActivation.Window, "Loaded", "Render");
        }

        public void Hide(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            HideCount++;
            typedActivation.IsVisible = false;
        }

        public void SetWindowState(object activation, object windowState)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.WindowState = windowState;
        }

        public void SetTitle(object activation, string title)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.Title = title;
        }

        public void SetClientSize(object activation, double width, double height)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.Width = width;
            typedActivation.Height = height;
        }

        public void Close(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            CloseCount++;
            typedActivation.IsClosed = true;
        }

        public void Run(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            RunCount++;
            AssertEqual(true, typedActivation.IsVisible, "SDK startup window visible before run");
            AssertEqual("ProGPU WPF SDK Smoke", typedActivation.Title, "activated SDK window title");
            AssertEqual(420.0, typedActivation.Width, "activated SDK window width");
            AssertEqual(840.0, typedActivation.Height, "activated SDK window height");
            AssertSame(typedActivation.Window, GetProperty(_application, "MainWindow"), "SDK Application.MainWindow");
            InvokeVoid(typedActivation.Window, "UpdateLayout");
            FlushDispatcherOperations(typedActivation.Window, "Loaded", "Render", "ApplicationIdle");
            ValidateWindow(
                typedActivation.Window,
                validateFrameContent: true,
                flushDispatcherOperations: window => FlushDispatcherOperations(window, "DataBind", "Loaded", "Render", "ApplicationIdle"));
        }

        public void Dispose(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            DisposeCount++;
            typedActivation.DisposePresentationSource();
        }

        public void ValidateAfterRun()
        {
            AssertEqual(1, ActivateCount, "SDK startup window activation count");
            AssertEqual(1, ShowCount, "SDK startup window show count");
            AssertEqual(1, RunCount, "SDK startup window run count");
            AssertEqual(true, RenderRequestCount > 0, "SDK portable MediaContext render request count");
            AssertEqual(1, CloseCount, "SDK startup window close count");
            AssertEqual(1, DisposeCount, "SDK startup window dispose count");

            if (_activation is null)
            {
                throw new InvalidOperationException("Application.Run did not create an SDK recording activation.");
            }

            AssertEqual(true, _activation.IsClosed, "SDK recording activation close state");
            AssertEqual(true, _activation.IsDisposed, "SDK recording activation dispose state");
            AssertEqual(0, HideCount, "SDK startup window hide count");
        }

        public void Dispose()
        {
            _mediaContextRenderRegistration?.Dispose();
            _mediaContextRenderRegistration = null;
            _activation?.DisposePresentationSource();
        }

        private void RequestRender(TimeSpan delay)
        {
            RenderRequestCount++;
        }

        private object CreatePortablePresentationSource(object window)
        {
            Type presentationSourceType = GetRequiredType(_presentationCore, PortablePresentationSourceTypeName);
            object presentationSource = Create(presentationSourceType);
            SetProperty(presentationSource, "RootVisual", window);
            return presentationSource;
        }

        private void FlushDispatcherOperations(object window, params string[] priorities)
        {
            MethodInfo method = _activationServiceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate =>
                    string.Equals(candidate.Name, "FlushDispatcherOperations", StringComparison.Ordinal) &&
                    candidate.GetParameters().Length == 2);
            Type priorityType = method.GetParameters()[1].ParameterType;

            foreach (string priority in priorities)
            {
                object markerPriority = Enum.Parse(priorityType, priority);
                InvokeStaticVoid(_activationServiceType, "FlushDispatcherOperations", window, markerPriority);
            }
        }

        private RecordingActivation AssertSameActivation(object activation)
        {
            if (!ReferenceEquals(_activation, activation) || _activation is null)
            {
                throw new InvalidOperationException("Unexpected SDK portable window activation instance.");
            }

            return _activation;
        }
    }

    private sealed class RecordingActivation
    {
        public RecordingActivation(object window, object presentationSource)
        {
            Window = window;
            PresentationSource = presentationSource;
        }

        public object Window { get; }

        public object PresentationSource { get; }

        public bool IsVisible { get; set; }

        public bool IsClosed { get; set; }

        public bool IsDisposed { get; private set; }

        public object? WindowState { get; set; }

        public string Title { get; set; } = string.Empty;

        public double Width { get; set; }

        public double Height { get; set; }

        public void DisposePresentationSource()
        {
            if (IsDisposed)
            {
                return;
            }

            if (PresentationSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else
            {
                InvokeVoid(PresentationSource, "Dispose");
            }

            IsDisposed = true;
        }
    }

    private sealed class SdkSmokeLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly string _repoRoot;
        private readonly string _appOutputRoot;
        private readonly string _wpfRoot;
        private readonly string _proGpuRoot;
        private readonly string _smokeAssemblyPath;
        private readonly AssemblyDependencyResolver _resolver;

        public SdkSmokeLoadContext(
            string repoRoot,
            string appOutputRoot,
            string smokeAssemblyPath,
            string wpfRoot,
            string proGpuRoot)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _appOutputRoot = appOutputRoot;
            _smokeAssemblyPath = smokeAssemblyPath;
            _wpfRoot = wpfRoot;
            _proGpuRoot = proGpuRoot;
            _resolver = new AssemblyDependencyResolver(typeof(Program).Assembly.Location);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = TryResolveAssemblyPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        private string? TryResolveAssemblyPath(AssemblyName assemblyName)
        {
            string fileName = assemblyName.Name + ".dll";
            string? path = assemblyName.Name switch
            {
                SmokeAssemblyName => _smokeAssemblyPath,
                "WindowsBase" or "System.Xaml" or "PresentationCore" or "PresentationFramework" or "PresentationUI" or "ReachFramework" or "System.Printing" or "UIAutomationTypes" or "UIAutomationProvider" or "System.Windows.Input.Manipulations" or "System.Windows.Primitives" or "PresentationFramework.Aero2" or "PresentationFramework.Fluent" =>
                    TryFindAssembly(_appOutputRoot, fileName) ?? Path.Combine(_wpfRoot, fileName),
                "ProGPU.Wpf" or "ProGPU.Backend" or "ProGPU.Scene" or "ProGPU.Vector" or "ProGPU.Text" =>
                    TryFindAssembly(_appOutputRoot, fileName) ?? Path.Combine(_proGpuRoot, fileName),
                _ => null
            };

            if (path is not null && File.Exists(path))
            {
                return path;
            }

            path = TryFindArtifactAssembly(assemblyName.Name, "net11.0")
                ?? TryFindArtifactAssembly(assemblyName.Name, "net10.0")
                ?? _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null && File.Exists(path) ? path : null;
        }

        private static string? TryFindAssembly(string root, string fileName)
        {
            string path = Path.Combine(root, fileName);
            return File.Exists(path) ? path : null;
        }

        private string? TryFindArtifactAssembly(string? assemblyName, string targetFramework)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return null;
            }

            string path = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName,
                "Debug",
                targetFramework,
                assemblyName + ".dll");
            return File.Exists(path) ? path : null;
        }

        public void Dispose()
        {
            Unload();
        }
    }
}
