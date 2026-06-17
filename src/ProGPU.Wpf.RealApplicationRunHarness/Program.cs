using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private const string CompilerHarnessAssemblyName = "ProGPU.Wpf.RealXamlCompilerHarness";
    private const string AppTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow";
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";

    [STAThread]
    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationFrameworkPath = FindArtifactAssembly(repoRoot, "PresentationFramework");
            string presentationCorePath = FindArtifactAssembly(repoRoot, "PresentationCore");
            string compilerHarnessPath = FindArtifactAssembly(repoRoot, CompilerHarnessAssemblyName);

            RunHarness(repoRoot, presentationFrameworkPath, presentationCorePath, compilerHarnessPath);
            Console.WriteLine("Real WPF Application.Run smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHarness(
        string repoRoot,
        string presentationFrameworkPath,
        string presentationCorePath,
        string compilerHarnessPath)
    {
        var loadContext = new WpfAssemblyLoadContext(
            repoRoot,
            presentationFrameworkPath,
            presentationCorePath,
            compilerHarnessPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyPath(presentationCorePath);
        Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
        Assembly compilerHarness = loadContext.LoadFromAssemblyPath(compilerHarnessPath);

        object? application = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(compilerHarness, AppTypeName);
            Invoke(application, "InitializeComponent");
            ValidateApplication(application);

            ActivationRecorder recorder = RegisterPortableActivation(
                presentationFramework,
                presentationCore,
                compilerHarness,
                application,
                out activationServiceType);

            object exitCode = Invoke(application, "Run");
            AssertEqual(0, exitCode, "Application.Run exit code");
            recorder.ValidateAfterRun();
        }
        finally
        {
            activationServiceType?.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);

            if (application != null)
            {
                TryInvoke(application, "Shutdown");
            }

            loadContext.Unload();
        }
    }

    private static void ValidateApplication(object application)
    {
        AssertEqual("MainWindow.xaml", GetProperty(application, "StartupUri").ToString(), "startup URI");

        object resources = GetProperty(application, "Resources");
        AssertCollectionCount(GetProperty(resources, "Keys"), expected: 7, "application resource keys");
        object mergedDictionaries = GetProperty(resources, "MergedDictionaries");
        AssertCollectionCount(mergedDictionaries, expected: 1, "application merged dictionaries");
        object smokeResources = GetCollectionItem(mergedDictionaries, 0);
        AssertType(smokeResources, "System.Windows.ResourceDictionary", "compiled merged resource dictionary");
        AssertEqual("SmokeResources.xaml", GetProperty(smokeResources, "Source").ToString(), "compiled merged resource dictionary source");

        object accentBrush = GetDictionaryValue(resources, "AccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "accent brush");
        AssertEqual("#FF356D9E", GetProperty(accentBrush, "Color").ToString(), "accent brush color");

        object replacementAccentBrush = GetDictionaryValue(resources, "ReplacementAccentBrush");
        AssertType(replacementAccentBrush, "System.Windows.Media.SolidColorBrush", "replacement accent brush");
        AssertEqual("#FF9C4A2F", GetProperty(replacementAccentBrush, "Color").ToString(), "replacement accent brush color");

        object smokeButtonTemplate = GetDictionaryValue(resources, "SmokeButtonTemplate");
        AssertType(smokeButtonTemplate, "System.Windows.Controls.ControlTemplate", "button control template");

        object textBoxStyle = GetDictionaryValue(resources, "SmokeTextBoxStyle");
        AssertType(textBoxStyle, "System.Windows.Style", "TextBox style");
        AssertEqual("System.Windows.Controls.TextBox", GetProperty(textBoxStyle, "TargetType").ToString(), "TextBox style target");

        object basedOnTextBoxStyle = GetDictionaryValue(resources, "BasedOnTextBoxStyle");
        AssertType(basedOnTextBoxStyle, "System.Windows.Style", "BasedOn TextBox style");
        AssertEqual("System.Windows.Controls.TextBox", GetProperty(basedOnTextBoxStyle, "TargetType").ToString(), "BasedOn TextBox style target");
        AssertSame(textBoxStyle, GetProperty(basedOnTextBoxStyle, "BasedOn"), "compiled TextBox BasedOn base style");

        object triggeredButtonStyle = GetDictionaryValue(resources, "TriggeredButtonStyle");
        AssertType(triggeredButtonStyle, "System.Windows.Style", "triggered Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(triggeredButtonStyle, "TargetType").ToString(), "triggered Button style target");

        object multiTriggeredButtonStyle = GetDictionaryValue(resources, "MultiTriggeredButtonStyle");
        AssertType(multiTriggeredButtonStyle, "System.Windows.Style", "multi-triggered Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(multiTriggeredButtonStyle, "TargetType").ToString(), "multi-triggered Button style target");

        object mergedAccentBrush = Invoke(application, "TryFindResource", "MergedAccentBrush");
        AssertType(mergedAccentBrush, "System.Windows.Media.SolidColorBrush", "merged accent brush");
        AssertEqual("#FF547A48", GetProperty(mergedAccentBrush, "Color").ToString(), "merged accent brush color");

        object mergedBlockMargin = Invoke(application, "TryFindResource", "MergedBlockMargin");
        AssertType(mergedBlockMargin, "System.Windows.Thickness", "merged block margin");
        AssertEqual(8.0, GetProperty(mergedBlockMargin, "Top"), "merged block margin top");
    }

    private static void ValidateMainWindow(object window, object application)
    {
        AssertType(window, MainWindowTypeName, "startup window");
        AssertEqual("ProGPU WPF XAML smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(420.0, GetProperty(window, "Width"), "window width");
        AssertEqual(260.0, GetProperty(window, "Height"), "window height");

        object content = GetProperty(window, "Content");
        AssertType(content, "System.Windows.Controls.StackPanel", "window content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expected: 24, "stack panel children");

        object textBlock = GetCollectionItem(children, 0);
        AssertType(textBlock, "System.Windows.Controls.TextBlock", "compiled TextBlock");
        AssertEqual("Real WPF XAML compiler smoke", GetProperty(textBlock, "Text"), "compiled TextBlock text");
        AssertEqual("#FF356D9E", GetProperty(GetProperty(textBlock, "Foreground"), "Color").ToString(), "compiled TextBlock foreground");

        object inputBox = GetField(window, "InputBox");
        AssertType(inputBox, "System.Windows.Controls.TextBox", "compiled named TextBox");
        AssertEqual("compiled TextBox", GetProperty(inputBox, "Text"), "compiled TextBox text");
        ValidateTextBoxSelection(inputBox);

        object resources = GetProperty(application, "Resources");
        object expectedStyle = GetDictionaryValue(resources, "SmokeTextBoxStyle");
        object actualStyle = GetProperty(inputBox, "Style");
        AssertSame(expectedStyle, actualStyle, "compiled TextBox style");

        object basedOnTextBox = GetField(window, "BasedOnTextBox");
        AssertType(basedOnTextBox, "System.Windows.Controls.TextBox", "compiled BasedOn TextBox");
        AssertEqual("compiled BasedOn TextBox", GetProperty(basedOnTextBox, "Text"), "compiled BasedOn TextBox text");
        object basedOnStyle = GetDictionaryValue(resources, "BasedOnTextBoxStyle");
        AssertSame(basedOnStyle, GetProperty(basedOnTextBox, "Style"), "compiled TextBox BasedOn style");
        AssertSame(expectedStyle, GetProperty(basedOnStyle, "BasedOn"), "compiled TextBox BasedOn base style");
        AssertEqual("based on text box style", GetProperty(basedOnTextBox, "Tag"), "compiled TextBox BasedOn local setter");
        AssertEqual(180.0, GetProperty(basedOnTextBox, "MinWidth"), "compiled TextBox BasedOn inherited MinWidth");
        object basedOnMargin = GetProperty(basedOnTextBox, "Margin");
        AssertEqual(8.0, GetProperty(basedOnMargin, "Top"), "compiled TextBox BasedOn inherited margin top");

        object foundInputBox = Invoke(window, "FindName", "InputBox");
        AssertSame(inputBox, foundInputBox, "compiled namescope lookup");

        ValidateRichFlowDocument(window);

        ValidateBindingAndCommand(window);
        ValidateAdvancedBindingFeatures(window);
        ValidateObjectDataProvider(window);
        ValidateMarkupExtension(window);
        ValidateMergedResourceDictionary(window, application);
        ValidateNestedUserControl(window);
        ValidateReadOnlyGridCollectionsAndAttachedProperties(window);
        ValidateImplicitMergedStyle(window, application);
        ValidateXamlEventHandler(window);
        ValidateStyleEventSetter(window);
        ValidateRoutedCommand(window);
        ValidateStyleAndDataTrigger(window, application);
        ValidateTemplateAndDynamicResource(window, application);
        ValidateItemsBindingAndTemplate(window);
    }

    private static void ValidateTextBoxSelection(object inputBox)
    {
        Invoke(inputBox, "Select", 9, 7);
        AssertEqual(9, GetProperty(inputBox, "SelectionStart"), "compiled TextBox selection start");
        AssertEqual(7, GetProperty(inputBox, "SelectionLength"), "compiled TextBox selection length");
        AssertEqual("TextBox", GetProperty(inputBox, "SelectedText"), "compiled TextBox selected text");

        SetProperty(inputBox, "SelectedText", "selection");
        AssertEqual("compiled selection", GetProperty(inputBox, "Text"), "compiled TextBox selected text replacement");
        AssertEqual(9, GetProperty(inputBox, "SelectionStart"), "compiled TextBox replacement selection start");
        AssertEqual(9, GetProperty(inputBox, "SelectionLength"), "compiled TextBox replacement selection length");
        AssertEqual("selection", GetProperty(inputBox, "SelectedText"), "compiled TextBox replacement selected text");
    }

    private static void ValidateRichFlowDocument(object window)
    {
        object richTextBox = GetField(window, "DocumentBox");
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled RichTextBox");

        object flowDocument = GetProperty(richTextBox, "Document");
        AssertType(flowDocument, "System.Windows.Documents.FlowDocument", "compiled FlowDocument");

        object blocks = GetProperty(flowDocument, "Blocks");
        AssertCollectionCount(blocks, expected: 2, "compiled FlowDocument blocks");

        object introParagraph = GetCollectionItem(blocks, 0);
        AssertType(introParagraph, "System.Windows.Documents.Paragraph", "compiled FlowDocument intro paragraph");

        object inlines = GetProperty(introParagraph, "Inlines");

        object bold = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Bold", "compiled FlowDocument bold inline");
        object boldRun = GetFirstCollectionItemOfType(GetProperty(bold, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument bold run");
        AssertEqual("rich", GetProperty(boldRun, "Text"), "compiled FlowDocument bold run text");

        object hyperlink = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Hyperlink", "compiled FlowDocument hyperlink");
        AssertEqual("https://example.test/progpu-wpf", GetProperty(hyperlink, "NavigateUri").ToString(), "compiled FlowDocument hyperlink URI");
        object hyperlinkRun = GetFirstCollectionItemOfType(GetProperty(hyperlink, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument hyperlink run");
        AssertEqual("link", GetProperty(hyperlinkRun, "Text"), "compiled FlowDocument hyperlink run text");

        object list = GetCollectionItem(blocks, 1);
        AssertType(list, "System.Windows.Documents.List", "compiled FlowDocument list");
        AssertEqual("Decimal", GetProperty(list, "MarkerStyle").ToString(), "compiled FlowDocument marker style");
        object listItems = GetProperty(list, "ListItems");
        AssertCollectionCount(listItems, expected: 2, "compiled FlowDocument list items");
        AssertFlowDocumentListItemText(GetCollectionItem(listItems, 0), "first document item", "first");
        AssertFlowDocumentListItemText(GetCollectionItem(listItems, 1), "second document item", "second");

        object textRange = Create(
            flowDocument.GetType().Assembly,
            "System.Windows.Documents.TextRange",
            GetProperty(flowDocument, "ContentStart"),
            GetProperty(flowDocument, "ContentEnd"));
        string text = GetProperty(textRange, "Text").ToString() ?? string.Empty;
        AssertContains("compiled", text, "compiled FlowDocument TextRange paragraph text");
        AssertContains("rich", text, "compiled FlowDocument TextRange bold text");
        AssertContains("FlowDocument", text, "compiled FlowDocument TextRange document text");
        AssertContains("link", text, "compiled FlowDocument TextRange hyperlink text");
        AssertContains("first document item", text, "compiled FlowDocument TextRange first list item");
        AssertContains("second document item", text, "compiled FlowDocument TextRange second list item");
    }

    private static void AssertFlowDocumentListItemText(object listItem, string expectedText, string description)
    {
        AssertType(listItem, "System.Windows.Documents.ListItem", $"compiled FlowDocument {description} list item");
        object paragraph = GetCollectionItem(GetProperty(listItem, "Blocks"), 0);
        AssertType(paragraph, "System.Windows.Documents.Paragraph", $"compiled FlowDocument {description} list paragraph");
        object run = GetFirstCollectionItemOfType(GetProperty(paragraph, "Inlines"), "System.Windows.Documents.Run", $"compiled FlowDocument {description} list run");
        AssertEqual(expectedText, GetProperty(run, "Text"), $"compiled FlowDocument {description} list text");
    }

    private static void ValidateBindingAndCommand(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        AssertType(dataContext, "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow+SmokeViewModel", "compiled binding DataContext");
        AssertEqual("bound greeting from real WPF", GetProperty(dataContext, "Greeting"), "bound view-model greeting");
        AssertEqual("run bound command", GetProperty(dataContext, "ButtonText"), "bound view-model button text");
        AssertEqual("style trigger target", GetProperty(dataContext, "TriggerButtonText"), "bound view-model trigger button text");

        object bindingBlock = GetField(window, "BindingBlock");
        AssertType(bindingBlock, "System.Windows.Controls.TextBlock", "compiled binding TextBlock");
        AssertEqual("bound greeting from real WPF", GetProperty(bindingBlock, "Text"), "compiled TextBlock binding");
        SetProperty(dataContext, "Greeting", "updated greeting from property change");
        AssertEqual("updated greeting from property change", GetProperty(bindingBlock, "Text"), "compiled TextBlock property-change binding");

        object commandButton = GetField(window, "CommandButton");
        AssertType(commandButton, "System.Windows.Controls.Button", "compiled command Button");
        AssertEqual("run bound command", GetProperty(commandButton, "Content"), "compiled Button content binding");

        object viewModelCommand = GetProperty(dataContext, "SmokeCommand");
        object buttonCommand = GetProperty(commandButton, "Command");
        AssertSame(viewModelCommand, buttonCommand, "compiled Button command binding");
        AssertEqual(0, GetProperty(viewModelCommand, "ExecutionCount"), "bound command initial execution count");
        Invoke(buttonCommand, "Execute", new object?[] { null });
        AssertEqual(1, GetProperty(viewModelCommand, "ExecutionCount"), "bound command execution count");
    }

    private static void ValidateAdvancedBindingFeatures(object window)
    {
        object dataContext = GetProperty(window, "DataContext");

        object priorityBindingBlock = GetField(window, "PriorityBindingBlock");
        AssertType(priorityBindingBlock, "System.Windows.Controls.TextBlock", "compiled PriorityBinding TextBlock");
        AssertEqual(
            "updated greeting from property change",
            GetProperty(priorityBindingBlock, "Text"),
            "compiled PriorityBinding fallback value");
        object priorityBindingExpression = GetPriorityBindingExpression(priorityBindingBlock, "TextProperty");
        object parentPriorityBinding = GetProperty(priorityBindingExpression, "ParentPriorityBinding");
        object priorityBindings = GetProperty(parentPriorityBinding, "Bindings");
        AssertCollectionCount(priorityBindings, expected: 2, "compiled PriorityBinding child bindings");
        AssertBindingObjectPath(GetCollectionItem(priorityBindings, 0), "MissingPriorityText", "compiled PriorityBinding first path");
        AssertBindingObjectPath(GetCollectionItem(priorityBindings, 1), "Greeting", "compiled PriorityBinding fallback path");

        object multiBindingBlock = GetField(window, "MultiBindingBlock");
        AssertType(multiBindingBlock, "System.Windows.Controls.TextBlock", "compiled MultiBinding TextBlock");
        AssertEqual(
            "updated greeting from property change / run bound command",
            GetProperty(multiBindingBlock, "Text"),
            "compiled MultiBinding string-format value");

        object validatedBox = GetField(window, "ValidatedBox");
        AssertType(validatedBox, "System.Windows.Controls.TextBox", "compiled validation TextBox");
        AssertEqual("valid binding text", GetProperty(validatedBox, "Text"), "compiled validation TextBox initial text");
        AssertEqual("valid binding text", GetProperty(dataContext, "ValidatedText"), "compiled validation source initial value");
        AssertBindingPath(validatedBox, "TextProperty", "ValidatedText", "compiled validation binding path");

        Type validationType = validatedBox.GetType().Assembly.GetType("System.Windows.Controls.Validation", throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Controls.Validation");
        AssertEqual(false, GetDependencyPropertyValue(validatedBox, validationType, "HasErrorProperty"), "compiled validation initial error state");

        SetProperty(validatedBox, "Text", string.Empty);
        object bindingExpression = GetBindingExpression(validatedBox, "TextProperty");
        Invoke(bindingExpression, "UpdateSource");
        AssertEqual(string.Empty, GetProperty(dataContext, "ValidatedText"), "compiled validation invalid source value");
        AssertEqual(true, GetDependencyPropertyValue(validatedBox, validationType, "HasErrorProperty"), "compiled validation error state");

        SetProperty(validatedBox, "Text", "valid binding text restored");
        Invoke(bindingExpression, "UpdateSource");
        AssertEqual("valid binding text restored", GetProperty(dataContext, "ValidatedText"), "compiled validation restored source value");
        AssertEqual(false, GetDependencyPropertyValue(validatedBox, validationType, "HasErrorProperty"), "compiled validation restored error state");
    }

    private static void ValidatePostShowBindingFeatures(object window)
    {
        object relativeSourceBlock = GetField(window, "RelativeSourceBlock");
        AssertType(relativeSourceBlock, "System.Windows.Controls.TextBlock", "compiled RelativeSource TextBlock");
        AssertEqual("ancestor binding source", GetProperty(relativeSourceBlock, "Text"), "compiled RelativeSource ancestor binding value");
        AssertBindingPath(relativeSourceBlock, "TextProperty", "Tag", "compiled RelativeSource binding path");
    }

    private static void ValidatePostShowItemTemplateTriggerActivation(Assembly presentationCore, object window)
    {
        object itemsList = GetField(window, "ItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");
        object betaItem = GetCollectionItem(sourceItems, 1);

        Invoke(itemsList, "ScrollIntoView", betaItem);
        Invoke(itemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(itemsList, "ItemContainerGenerator");
        object betaContainer = Invoke(itemContainerGenerator, "ContainerFromItem", betaItem);
        AssertType(betaContainer, "System.Windows.Controls.ListBoxItem", "compiled DataTemplate generated item container");
        Invoke(betaContainer, "ApplyTemplate");
        Invoke(betaContainer, "UpdateLayout");

        object betaTextBlock = FindVisualDescendantByName(presentationCore, betaContainer, "ItemTextBlock")
            ?? throw new InvalidOperationException("Expected generated item container to contain ItemTextBlock.");
        AssertType(betaTextBlock, "System.Windows.Controls.TextBlock", "compiled DataTemplate generated TextBlock");
        AssertEqual("item beta", GetProperty(betaTextBlock, "Text"), "compiled DataTemplate generated TextBlock binding");
        AssertEqual("template trigger active", GetProperty(betaTextBlock, "Tag"), "compiled DataTemplate trigger active generated value");
    }

    private static void ValidateObjectDataProvider(object window)
    {
        object provider = Invoke(window, "TryFindResource", "ProviderGreeting");
        AssertType(provider, "System.Windows.Data.ObjectDataProvider", "compiled ObjectDataProvider resource");
        AssertEqual(false, GetProperty(provider, "IsAsynchronous"), "compiled ObjectDataProvider synchronous flag");
        AssertEqual("CreateProviderGreeting", GetProperty(provider, "MethodName"), "compiled ObjectDataProvider method name");
        Type providerFactoryType = window.GetType().Assembly.GetType("ProGPU.Wpf.RealXamlCompilerHarness.ProviderDataFactory", throwOnError: true)
            ?? throw new TypeLoadException("ProGPU.Wpf.RealXamlCompilerHarness.ProviderDataFactory");
        AssertSame(providerFactoryType, GetProperty(provider, "ObjectType"), "compiled ObjectDataProvider object type");
        AssertType(GetProperty(provider, "ObjectInstance"), "ProGPU.Wpf.RealXamlCompilerHarness.ProviderDataFactory", "compiled ObjectDataProvider object instance");
        AssertEqual("provider data 7", GetProperty(provider, "Data"), "compiled ObjectDataProvider data");

        object methodParameters = GetProperty(provider, "MethodParameters");
        AssertCollectionCount(methodParameters, expected: 2, "compiled ObjectDataProvider method parameters");
        AssertEqual("provider", GetCollectionItem(methodParameters, 0), "compiled ObjectDataProvider first parameter");
        AssertEqual("7", GetCollectionItem(methodParameters, 1), "compiled ObjectDataProvider second parameter");

        object providerGreetingBlock = GetField(window, "ProviderGreetingBlock");
        AssertType(providerGreetingBlock, "System.Windows.Controls.TextBlock", "compiled ObjectDataProvider TextBlock");
        AssertEqual("provider data 7", GetProperty(providerGreetingBlock, "Text"), "compiled ObjectDataProvider bound text");

        object bindingExpression = GetBindingExpression(providerGreetingBlock, "TextProperty");
        object parentBinding = GetProperty(bindingExpression, "ParentBinding");
        AssertSame(provider, GetProperty(parentBinding, "Source"), "compiled ObjectDataProvider binding source");
    }

    private static void ValidateMarkupExtension(object window)
    {
        object markupExtensionBlock = GetField(window, "MarkupExtensionBlock");
        AssertType(markupExtensionBlock, "System.Windows.Controls.TextBlock", "compiled MarkupExtension TextBlock");
        AssertEqual("compiled markup extension", GetProperty(markupExtensionBlock, "Text"), "compiled MarkupExtension provided text");
    }

    private static void ValidateMergedResourceDictionary(object window, object application)
    {
        object expectedBrush = Invoke(application, "TryFindResource", "MergedAccentBrush");
        object expectedMargin = Invoke(application, "TryFindResource", "MergedBlockMargin");

        object mergedResourceBlock = GetField(window, "MergedResourceBlock");
        AssertType(mergedResourceBlock, "System.Windows.Controls.TextBlock", "compiled merged-resource TextBlock");
        AssertEqual("compiled merged resource", GetProperty(mergedResourceBlock, "Text"), "compiled merged-resource TextBlock text");
        AssertSame(expectedBrush, GetProperty(mergedResourceBlock, "Foreground"), "compiled merged-resource foreground");

        object actualMargin = GetProperty(mergedResourceBlock, "Margin");
        AssertEqual(GetProperty(expectedMargin, "Left"), GetProperty(actualMargin, "Left"), "compiled merged-resource margin left");
        AssertEqual(GetProperty(expectedMargin, "Top"), GetProperty(actualMargin, "Top"), "compiled merged-resource margin top");
        AssertEqual(GetProperty(expectedMargin, "Right"), GetProperty(actualMargin, "Right"), "compiled merged-resource margin right");
        AssertEqual(GetProperty(expectedMargin, "Bottom"), GetProperty(actualMargin, "Bottom"), "compiled merged-resource margin bottom");
    }

    private static void ValidateNestedUserControl(object window)
    {
        object nestedControl = GetField(window, "NestedControl");
        AssertType(nestedControl, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeUserControl", "compiled nested UserControl");

        object foundNestedControl = Invoke(window, "FindName", "NestedControl");
        AssertSame(nestedControl, foundNestedControl, "compiled nested UserControl namescope lookup");

        object resources = GetProperty(nestedControl, "Resources");
        object userControlBrush = GetDictionaryValue(resources, "UserControlBrush");
        AssertEqual("#FF3F6E5A", GetProperty(userControlBrush, "Color").ToString(), "compiled UserControl brush color");

        object controlTitle = GetField(nestedControl, "ControlTitle");
        AssertType(controlTitle, "System.Windows.Controls.TextBlock", "compiled UserControl title TextBlock");
        AssertEqual("compiled user control", GetProperty(controlTitle, "Text"), "compiled UserControl title text");
        AssertSame(userControlBrush, GetProperty(controlTitle, "Foreground"), "compiled UserControl resource brush");

        object elementNameMirror = GetField(nestedControl, "ElementNameMirror");
        AssertType(elementNameMirror, "System.Windows.Controls.TextBlock", "compiled UserControl element-name TextBlock");
        AssertEqual("compiled user control", GetProperty(elementNameMirror, "Text"), "compiled UserControl ElementName binding value");
        AssertBindingPath(elementNameMirror, "TextProperty", "Text", "compiled UserControl ElementName binding path");

        object controlEventButton = GetField(nestedControl, "ControlEventButton");
        AssertType(controlEventButton, "System.Windows.Controls.Button", "compiled UserControl event Button");
        AssertEqual("user control event", GetProperty(controlEventButton, "Content"), "compiled UserControl event Button content");
        AssertEqual(0, GetProperty(nestedControl, "ControlClickCount"), "compiled UserControl initial click count");
        Invoke(controlEventButton, "OnClick");
        AssertEqual(1, GetProperty(nestedControl, "ControlClickCount"), "compiled UserControl click handler count");
        AssertEqual("ControlEventButton", GetProperty(nestedControl, "LastControlClickSenderName"), "compiled UserControl click sender name");
        AssertEqual("Click", GetProperty(nestedControl, "LastControlClickRoutedEventName"), "compiled UserControl click routed event name");
    }

    private static void ValidateReadOnlyGridCollectionsAndAttachedProperties(object window)
    {
        object layoutGrid = GetField(window, "AttachedLayoutGrid");
        AssertType(layoutGrid, "System.Windows.Controls.Grid", "compiled attached-layout Grid");
        AssertCollectionCount(GetProperty(layoutGrid, "RowDefinitions"), expected: 2, "compiled Grid row definitions");
        AssertCollectionCount(GetProperty(layoutGrid, "ColumnDefinitions"), expected: 2, "compiled Grid column definitions");
        AssertCollectionCount(GetProperty(layoutGrid, "Children"), expected: 2, "compiled Grid children");

        object firstCell = GetField(window, "GridFirstCell");
        AssertType(firstCell, "System.Windows.Controls.TextBlock", "compiled Grid first cell");
        AssertEqual("grid alpha", GetProperty(firstCell, "Text"), "compiled Grid first-cell text");
        AssertEqual(0, GetDependencyPropertyValue(firstCell, layoutGrid.GetType(), "RowProperty"), "compiled Grid first-cell row");
        AssertEqual(0, GetDependencyPropertyValue(firstCell, layoutGrid.GetType(), "ColumnProperty"), "compiled Grid first-cell column");

        object secondCell = GetField(window, "GridSecondCell");
        AssertType(secondCell, "System.Windows.Controls.TextBlock", "compiled Grid second cell");
        AssertEqual("grid beta", GetProperty(secondCell, "Text"), "compiled Grid second-cell text");
        AssertEqual(1, GetDependencyPropertyValue(secondCell, layoutGrid.GetType(), "RowProperty"), "compiled Grid second-cell row");
        AssertEqual(1, GetDependencyPropertyValue(secondCell, layoutGrid.GetType(), "ColumnProperty"), "compiled Grid second-cell column");
    }

    private static void ValidateImplicitMergedStyle(object window, object application)
    {
        object implicitStyleCheckBox = GetField(window, "ImplicitStyleCheckBox");
        AssertType(implicitStyleCheckBox, "System.Windows.Controls.CheckBox", "compiled implicit-style CheckBox");
        AssertEqual(true, GetProperty(implicitStyleCheckBox, "IsChecked"), "compiled implicit-style CheckBox checked state");

        object expectedStyle = Invoke(application, "TryFindResource", implicitStyleCheckBox.GetType());
        AssertType(expectedStyle, "System.Windows.Style", "merged implicit CheckBox style");
        AssertSame(expectedStyle, GetProperty(implicitStyleCheckBox, "Style"), "compiled implicit CheckBox style");
        AssertEqual("implicit merged style", GetProperty(implicitStyleCheckBox, "Tag"), "compiled implicit CheckBox style tag");

        object expectedMargin = Invoke(application, "TryFindResource", "MergedBlockMargin");
        object actualMargin = GetProperty(implicitStyleCheckBox, "Margin");
        AssertEqual(GetProperty(expectedMargin, "Top"), GetProperty(actualMargin, "Top"), "compiled implicit CheckBox style margin top");
    }

    private static void ValidateXamlEventHandler(object window)
    {
        object eventButton = GetField(window, "EventButton");
        AssertType(eventButton, "System.Windows.Controls.Button", "compiled event Button");
        AssertEqual("run xaml event", GetProperty(eventButton, "Content"), "compiled event Button content");
        AssertEqual(0, GetProperty(window, "XamlClickCount"), "XAML event initial click count");

        Invoke(eventButton, "OnClick");

        AssertEqual(1, GetProperty(window, "XamlClickCount"), "compiled XAML Click handler count");
        AssertEqual("EventButton", GetProperty(window, "LastXamlClickSenderName"), "compiled XAML Click sender name");
        AssertEqual("Click", GetProperty(window, "LastXamlClickRoutedEventName"), "compiled XAML Click routed event name");
    }

    private static void ValidateStyleEventSetter(object window)
    {
        object styledEventButton = GetField(window, "StyledEventButton");
        AssertType(styledEventButton, "System.Windows.Controls.Button", "compiled EventSetter Button");
        AssertEqual("run style event", GetProperty(styledEventButton, "Content"), "compiled EventSetter Button content");
        AssertEqual("event setter style", GetProperty(styledEventButton, "Tag"), "compiled EventSetter style setter");

        object style = GetProperty(styledEventButton, "Style");
        AssertType(style, "System.Windows.Style", "compiled EventSetter style");
        object eventSetters = GetProperty(style, "Setters");
        AssertAtLeast(2, GetProperty(eventSetters, "Count"), "compiled EventSetter style setters");
        AssertEqual(0, GetProperty(window, "StyledClickCount"), "compiled EventSetter initial click count");

        Invoke(styledEventButton, "OnClick");

        AssertEqual(1, GetProperty(window, "StyledClickCount"), "compiled EventSetter Click handler count");
        AssertEqual("StyledEventButton", GetProperty(window, "LastStyledClickSenderName"), "compiled EventSetter Click sender name");
        AssertEqual("Click", GetProperty(window, "LastStyledClickRoutedEventName"), "compiled EventSetter Click routed event name");
    }

    private static void ValidateStyleAndDataTrigger(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object accentBrush = GetDictionaryValue(resources, "AccentBrush");
        object replacementAccentBrush = GetDictionaryValue(resources, "ReplacementAccentBrush");
        object expectedStyle = GetDictionaryValue(resources, "TriggeredButtonStyle");
        object expectedMultiStyle = GetDictionaryValue(resources, "MultiTriggeredButtonStyle");
        object dataContext = GetProperty(window, "DataContext");

        object triggeredButton = GetField(window, "TriggeredButton");
        AssertType(triggeredButton, "System.Windows.Controls.Button", "compiled triggered Button");
        AssertSame(expectedStyle, GetProperty(triggeredButton, "Style"), "compiled Button triggered style");
        AssertEqual("style trigger target", GetProperty(triggeredButton, "Content"), "compiled Button trigger content binding");
        AssertEqual(false, GetProperty(dataContext, "IsWarning"), "style trigger initial view-model state");
        AssertEqual(false, GetProperty(dataContext, "IsCritical"), "multi trigger initial critical view-model state");
        AssertEqual("trigger inactive", GetProperty(triggeredButton, "Tag"), "compiled DataTrigger inactive value");
        AssertSame(accentBrush, GetProperty(triggeredButton, "Background"), "compiled DataTrigger inactive brush");

        object multiTriggeredButton = GetField(window, "MultiTriggeredButton");
        AssertType(multiTriggeredButton, "System.Windows.Controls.Button", "compiled multi-triggered Button");
        AssertSame(expectedMultiStyle, GetProperty(multiTriggeredButton, "Style"), "compiled Button MultiDataTrigger style");
        AssertEqual("style trigger target", GetProperty(multiTriggeredButton, "Content"), "compiled Button MultiDataTrigger content binding");
        AssertEqual("multi trigger inactive", GetProperty(multiTriggeredButton, "Tag"), "compiled MultiDataTrigger inactive value");
        AssertSame(accentBrush, GetProperty(multiTriggeredButton, "Background"), "compiled MultiDataTrigger inactive brush");

        SetProperty(dataContext, "IsWarning", true);
        AssertEqual(true, GetProperty(dataContext, "IsWarning"), "style trigger updated view-model state");
        AssertEqual("trigger active", GetProperty(triggeredButton, "Tag"), "compiled DataTrigger active value");
        AssertSame(replacementAccentBrush, GetProperty(triggeredButton, "Background"), "compiled DataTrigger active brush");
        AssertEqual("multi trigger inactive", GetProperty(multiTriggeredButton, "Tag"), "compiled MultiDataTrigger partial-condition value");
        AssertSame(accentBrush, GetProperty(multiTriggeredButton, "Background"), "compiled MultiDataTrigger partial-condition brush");

        SetProperty(dataContext, "IsCritical", true);
        AssertEqual(true, GetProperty(dataContext, "IsCritical"), "multi trigger updated critical view-model state");
        AssertEqual("multi trigger active", GetProperty(multiTriggeredButton, "Tag"), "compiled MultiDataTrigger active value");
        AssertSame(replacementAccentBrush, GetProperty(multiTriggeredButton, "Background"), "compiled MultiDataTrigger active brush");
    }

    private static void ValidateRoutedCommand(object window)
    {
        object inputBox = GetField(window, "InputBox");
        object routedCommandButton = GetField(window, "RoutedCommandButton");
        AssertType(routedCommandButton, "System.Windows.Controls.Button", "compiled routed command Button");
        AssertEqual("run routed command", GetProperty(routedCommandButton, "Content"), "compiled routed command Button content");
        AssertSame(inputBox, GetProperty(routedCommandButton, "CommandTarget"), "compiled routed command target");

        object commandParameter = GetProperty(routedCommandButton, "CommandParameter");
        AssertEqual("routed command payload", commandParameter, "compiled routed command parameter");

        object routedCommand = GetProperty(routedCommandButton, "Command");
        AssertType(routedCommand, "System.Windows.Input.RoutedUICommand", "compiled routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(routedCommand, "Name"), "compiled routed command name");
        AssertEqual(0, GetProperty(window, "RoutedCommandExecutionCount"), "routed command initial execution count");

        object canExecute = InvokeTwoArgumentCommand(routedCommand, "CanExecute", commandParameter, inputBox);
        AssertEqual(true, canExecute, "routed command CanExecute result");
        AssertAtLeast(1, GetProperty(window, "RoutedCommandCanExecuteCount"), "routed command CanExecute handler count");

        InvokeTwoArgumentCommand(routedCommand, "Execute", commandParameter, inputBox);
        AssertEqual(1, GetProperty(window, "RoutedCommandExecutionCount"), "routed command execution count");
        AssertEqual("routed command payload", GetProperty(window, "LastRoutedCommandParameter"), "routed command executed parameter");
    }

    private static void ValidateTemplateAndDynamicResource(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object accentBrush = GetDictionaryValue(resources, "AccentBrush");
        object replacementAccentBrush = GetDictionaryValue(resources, "ReplacementAccentBrush");
        object expectedTemplate = GetDictionaryValue(resources, "SmokeButtonTemplate");

        object templatedButton = GetField(window, "TemplatedButton");
        AssertType(templatedButton, "System.Windows.Controls.Button", "compiled templated Button");
        AssertEqual("templated button", GetProperty(templatedButton, "Content"), "compiled templated Button content");
        AssertSame(expectedTemplate, GetProperty(templatedButton, "Template"), "compiled Button control template");
        AssertEqual(true, Invoke(templatedButton, "ApplyTemplate"), "compiled Button template application");

        object templateBorder = Invoke(expectedTemplate, "FindName", "TemplateBorder", templatedButton);
        AssertType(templateBorder, "System.Windows.Controls.Border", "compiled ControlTemplate named part");
        AssertSame(accentBrush, GetProperty(templateBorder, "Background"), "compiled ControlTemplate dynamic resource initial value");
        AssertEqual(1.0, GetProperty(templateBorder, "Opacity"), "compiled ControlTemplate trigger initial opacity");

        SetDictionaryValue(resources, "AccentBrush", replacementAccentBrush);
        AssertSame(replacementAccentBrush, GetProperty(templateBorder, "Background"), "compiled ControlTemplate dynamic resource update");

        SetProperty(templatedButton, "IsEnabled", false);
        AssertEqual(false, GetProperty(templatedButton, "IsEnabled"), "compiled ControlTemplate trigger source state");
        AssertEqual(0.42, GetProperty(templateBorder, "Opacity"), "compiled ControlTemplate trigger disabled opacity");
    }

    private static void ValidateItemsBindingAndTemplate(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");
        AssertCollectionCount(sourceItems, expected: 2, "view-model items");

        object itemsList = GetField(window, "ItemsList");
        AssertType(itemsList, "System.Windows.Controls.ListBox", "compiled item ListBox");
        AssertSame(sourceItems, GetProperty(itemsList, "ItemsSource"), "compiled ListBox ItemsSource binding");
        AssertCollectionCount(GetProperty(itemsList, "Items"), expected: 2, "compiled ListBox generated items");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(itemsList, "SelectedItem"), "compiled ListBox initial selected item");

        object firstItem = GetCollectionItem(sourceItems, 0);
        SetProperty(itemsList, "SelectedItem", firstItem);
        AssertSame(firstItem, GetProperty(dataContext, "SelectedItem"), "compiled ListBox two-way selected item binding");

        object itemTemplate = GetProperty(itemsList, "ItemTemplate");
        AssertType(itemTemplate, "System.Windows.DataTemplate", "compiled ListBox item template");
        object templateRoot = Invoke(itemTemplate, "LoadContent");
        AssertType(templateRoot, "System.Windows.Controls.TextBlock", "compiled DataTemplate root");
        AssertEqual("ItemTextBlock", GetProperty(templateRoot, "Name"), "compiled DataTemplate named root");
        AssertEqual("template trigger inactive", GetProperty(templateRoot, "Tag"), "compiled DataTemplate root default tag");
        AssertBindingPath(templateRoot, "TextProperty", "Name", "compiled DataTemplate text binding path");
        object dataTemplateTriggers = GetProperty(itemTemplate, "Triggers");
        AssertCollectionCount(dataTemplateTriggers, expected: 1, "compiled DataTemplate triggers");
        object dataTemplateTrigger = GetCollectionItem(dataTemplateTriggers, 0);
        AssertType(dataTemplateTrigger, "System.Windows.DataTrigger", "compiled DataTemplate DataTrigger");
        AssertBindingObjectPath(GetProperty(dataTemplateTrigger, "Binding"), "Name", "compiled DataTemplate DataTrigger binding path");
        AssertEqual("item beta", GetProperty(dataTemplateTrigger, "Value"), "compiled DataTemplate DataTrigger value");
        object dataTemplateTriggerSetters = GetProperty(dataTemplateTrigger, "Setters");
        AssertCollectionCount(dataTemplateTriggerSetters, expected: 1, "compiled DataTemplate DataTrigger setters");
        object dataTemplateTriggerSetter = GetCollectionItem(dataTemplateTriggerSetters, 0);
        AssertType(dataTemplateTriggerSetter, "System.Windows.Setter", "compiled DataTemplate DataTrigger setter");
        AssertEqual("ItemTextBlock", GetProperty(dataTemplateTriggerSetter, "TargetName"), "compiled DataTemplate DataTrigger setter target");
        AssertEqual("Tag", GetProperty(GetProperty(dataTemplateTriggerSetter, "Property"), "Name"), "compiled DataTemplate DataTrigger setter property");
        AssertEqual("template trigger active", GetProperty(dataTemplateTriggerSetter, "Value"), "compiled DataTemplate DataTrigger setter value");

        object sortedItemsViewSource = Invoke(window, "TryFindResource", "SortedItemsView");
        AssertType(sortedItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled CollectionViewSource resource");
        object sortDescriptions = GetProperty(sortedItemsViewSource, "SortDescriptions");
        AssertCollectionCount(sortDescriptions, expected: 1, "compiled CollectionViewSource sort descriptions");
        object sortDescription = GetCollectionItem(sortDescriptions, 0);
        AssertEqual("Name", GetProperty(sortDescription, "PropertyName"), "compiled CollectionViewSource sort property");
        AssertEqual("Descending", GetProperty(sortDescription, "Direction").ToString(), "compiled CollectionViewSource sort direction");

        object sortedItemsList = GetField(window, "SortedItemsList");
        AssertType(sortedItemsList, "System.Windows.Controls.ListBox", "compiled sorted ListBox");
        AssertSame(GetProperty(sortedItemsViewSource, "View"), GetProperty(sortedItemsList, "ItemsSource"), "compiled ListBox CollectionViewSource binding");
        object sortedItems = GetProperty(sortedItemsList, "Items");
        AssertCollectionCount(sortedItems, expected: 2, "compiled sorted ListBox generated items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource initial first item");
        AssertEqual("item alpha", GetProperty(GetCollectionItem(sortedItems, 1), "Name"), "compiled CollectionViewSource initial second item");

        object thirdItem = Create(window.GetType().Assembly, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItem", "item gamma");
        AddToCollection(sourceItems, thirdItem);
        AssertCollectionCount(GetProperty(itemsList, "Items"), expected: 3, "compiled ListBox collection-change items");
        AssertCollectionCount(sortedItems, expected: 3, "compiled sorted ListBox collection-change items");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource collection-change first item");
    }

    private static ActivationRecorder RegisterPortableActivation(
        Assembly presentationFramework,
        Assembly presentationCore,
        Assembly compilerHarness,
        object application,
        out Type activationServiceType)
    {
        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        MethodInfo register = activationServiceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "Register");

        var recorder = new ActivationRecorder(presentationCore, compilerHarness, application);
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

    private static object Create(Assembly assembly, string typeName, params object?[] parameters)
    {
        Type type = GetRequiredType(assembly, typeName);
        return Activator.CreateInstance(type, parameters)
            ?? throw new InvalidOperationException($"Failed to create '{typeName}'.");
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Expected '{instance.GetType().FullName}.{propertyName}' to have a value.");
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
    }

    private static object? TryGetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static object? FindVisualDescendantByName(Assembly presentationCore, object root, string name)
    {
        if (string.Equals(TryGetProperty(root, "Name")?.ToString(), name, StringComparison.Ordinal))
        {
            return root;
        }

        Type visualTreeHelperType = GetRequiredType(presentationCore, "System.Windows.Media.VisualTreeHelper");
        int count = Convert.ToInt32(InvokeStatic(visualTreeHelperType, "GetChildrenCount", root));
        for (int i = 0; i < count; i++)
        {
            object child = InvokeStatic(visualTreeHelperType, "GetChild", root, i);
            object? match = FindVisualDescendantByName(presentationCore, child, name);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(instance, value);
    }

    private static object GetField(object instance, string fieldName)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance)
                    ?? throw new InvalidOperationException($"Expected '{type.FullName}.{fieldName}' to have a value.");
            }
        }

        throw new MissingFieldException(instance.GetType().FullName, fieldName);
    }

    private static object GetDictionaryValue(object dictionary, object key)
    {
        if (dictionary is IDictionary nonGenericDictionary && nonGenericDictionary.Contains(key))
        {
            return nonGenericDictionary[key]
                ?? throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        object value = Invoke(dictionary, "get_Item", key);
        if (value == null)
        {
            throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        return value;
    }

    private static void SetDictionaryValue(object dictionary, object key, object value)
    {
        if (dictionary is IDictionary nonGenericDictionary)
        {
            nonGenericDictionary[key] = value;
            return;
        }

        Invoke(dictionary, "set_Item", key, value);
    }

    private static object GetCollectionItem(object collection, int index)
    {
        if (collection is IList list)
        {
            return list[index]
                ?? throw new InvalidOperationException($"Collection item {index} had a null value.");
        }

        if (collection is IEnumerable enumerable)
        {
            int currentIndex = 0;
            foreach (object? item in enumerable)
            {
                if (currentIndex == index)
                {
                    return item
                        ?? throw new InvalidOperationException($"Collection item {index} had a null value.");
                }

                currentIndex++;
            }
        }

        return Invoke(collection, "get_Item", index);
    }

    private static object GetFirstCollectionItemOfType(object collection, string expectedFullName, string description)
    {
        if (collection is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (item != null && string.Equals(item.GetType().FullName, expectedFullName, StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }

        throw new InvalidOperationException($"Expected {description} to contain '{expectedFullName}'.");
    }

    private static object GetDependencyPropertyValue(object dependencyObject, Type ownerType, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = ownerType.GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(ownerType.FullName, dependencyPropertyFieldName);
        return Invoke(dependencyObject, "GetValue", dependencyProperty.GetValue(null));
    }

    private static void AddToCollection(object collection, object item)
    {
        MethodInfo add = collection.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { item.GetType() },
            modifiers: null)
            ?? collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == "Add" &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsAssignableFrom(item.GetType()))
            ?? throw new MissingMethodException(collection.GetType().FullName, "Add");
        add.Invoke(collection, new[] { item });
    }

    private static void AssertBindingPath(
        object dependencyObject,
        string dependencyPropertyFieldName,
        string expectedPath,
        string description)
    {
        object bindingExpression = GetBindingExpression(dependencyObject, dependencyPropertyFieldName);
        object parentBinding = GetProperty(bindingExpression, "ParentBinding");
        object path = GetProperty(parentBinding, "Path");
        AssertEqual(expectedPath, GetProperty(path, "Path"), description);
    }

    private static void AssertBindingObjectPath(object binding, string expectedPath, string description)
    {
        object path = GetProperty(binding, "Path");
        AssertEqual(expectedPath, GetProperty(path, "Path"), description);
    }

    private static object GetBindingExpression(object dependencyObject, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = dependencyObject.GetType().GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(dependencyObject.GetType().FullName, dependencyPropertyFieldName);
        MethodInfo getBindingExpression = dependencyObject.GetType().GetMethod(
            "GetBindingExpression",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(dependencyObject.GetType().FullName, "GetBindingExpression");

        object? bindingExpression = getBindingExpression.Invoke(dependencyObject, new[] { dependencyProperty.GetValue(null) });
        if (bindingExpression == null)
        {
            throw new InvalidOperationException(
                $"Expected '{dependencyObject.GetType().FullName}.{dependencyPropertyFieldName}' to have a binding expression.");
        }

        return bindingExpression;
    }

    private static object GetPriorityBindingExpression(object dependencyObject, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = dependencyObject.GetType().GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(dependencyObject.GetType().FullName, dependencyPropertyFieldName);
        Type bindingOperationsType = dependencyObject.GetType().Assembly.GetType(
            "System.Windows.Data.BindingOperations",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Data.BindingOperations");
        MethodInfo getPriorityBindingExpression = bindingOperationsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "GetPriorityBindingExpression", StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 2)
            ?? throw new MissingMethodException(bindingOperationsType.FullName, "GetPriorityBindingExpression");

        object? bindingExpression = getPriorityBindingExpression.Invoke(null, new[] { dependencyObject, dependencyProperty.GetValue(null) });
        if (bindingExpression == null)
        {
            throw new InvalidOperationException(
                $"Expected '{dependencyObject.GetType().FullName}.{dependencyPropertyFieldName}' to have a priority binding expression.");
        }

        return bindingExpression;
    }

    private static object Invoke(object instance, string methodName, params object?[] parameters)
    {
        MethodInfo method = instance.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        return method.Invoke(instance, parameters) ?? new object();
    }

    private static object InvokeStatic(Type type, string methodName, params object?[] parameters)
    {
        MethodInfo method = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(type.FullName, methodName);

        return method.Invoke(null, parameters) ?? new object();
    }

    private static object InvokeTwoArgumentCommand(object command, string methodName, object? parameter, object target)
    {
        MethodInfo method = command.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == 2 &&
                    candidateParameters[1].ParameterType.IsAssignableFrom(target.GetType());
            })
            ?? throw new MissingMethodException(command.GetType().FullName, methodName);

        return method.Invoke(command, new[] { parameter, target }) ?? new object();
    }

    private static void TryInvoke(object instance, string methodName, params object?[] parameters)
    {
        try
        {
            Invoke(instance, methodName, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void AssertCollectionCount(object collection, int expected, string description)
    {
        object count =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");
        AssertEqual(expected, count, description);
    }

    private static void AssertType(object instance, string expectedFullName, string description)
    {
        if (!string.Equals(instance.GetType().FullName, expectedFullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedFullName}', got '{instance.GetType().FullName}'.");
        }
    }

    private static void AssertSame(object expected, object actual, string description)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to reference the same object.");
        }
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static void AssertContains(string expectedSubstring, string actual, string description)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {description} to contain '{expectedSubstring}', got '{actual}'.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, object actual, string description)
    {
        int actualValue = Convert.ToInt32(actual);
        if (actualValue < expectedMinimum)
        {
            throw new InvalidOperationException($"Expected {description} to be at least {expectedMinimum}, got {actualValue}.");
        }
    }

    private static string FindArtifactAssembly(string repoRoot, string assemblyName)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName);
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Artifacts directory was not found: {artifactsRoot}");
        }

        string[] candidates = Directory.GetFiles(
            artifactsRoot,
            $"{assemblyName}.dll",
            SearchOption.AllDirectories);

        string? selected = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net11.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return selected
            ?? throw new FileNotFoundException($"Could not locate a net11.0 {assemblyName}.dll artifact.", artifactsRoot);
    }

    private static string? TryFindArtifactAssembly(string repoRoot, AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
        {
            return null;
        }

        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName.Name);
        if (!Directory.Exists(artifactsRoot))
        {
            return null;
        }

        return Directory
            .GetFiles(artifactsRoot, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net11.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string marker = Path.Combine(
                directory.FullName,
                "src",
                "Microsoft.DotNet.Wpf",
                "src",
                "PresentationFramework",
                "PresentationFramework.csproj");

            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WPF repository root.");
    }

    private sealed class ActivationRecorder
    {
        private readonly Assembly _presentationCore;
        private readonly Assembly _compilerHarness;
        private readonly object _application;
        private object? _activation;

        public ActivationRecorder(Assembly presentationCore, Assembly compilerHarness, object application)
        {
            _presentationCore = presentationCore;
            _compilerHarness = compilerHarness;
            _application = application;
        }

        public int ActivateCount { get; private set; }

        public int ShowCount { get; private set; }

        public int RunCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public object Activate(object window)
        {
            if (ActivateCount != 0)
            {
                throw new InvalidOperationException("Expected exactly one startup window activation.");
            }

            AssertType(window, MainWindowTypeName, "activated startup window");
            AssertSame(GetRequiredType(_compilerHarness, MainWindowTypeName), window.GetType(), "activated startup window type");
            ValidateMainWindow(window, _application);

            ActivateCount++;
            _activation = new RecordingActivation(window)
            {
                Title = GetProperty(window, "Title").ToString() ?? string.Empty,
                Width = Convert.ToDouble(GetProperty(window, "Width")),
                Height = Convert.ToDouble(GetProperty(window, "Height"))
            };
            return _activation;
        }

        public void Show(object activation)
        {
            AssertSameActivation(activation);
            ShowCount++;
            ((RecordingActivation)activation).IsVisible = true;
        }

        public void Hide(object activation)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).IsVisible = false;
        }

        public void SetWindowState(object activation, object windowState)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).WindowState = windowState;
        }

        public void SetTitle(object activation, string title)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).Title = title;
        }

        public void SetClientSize(object activation, double width, double height)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).Width = width;
            ((RecordingActivation)activation).Height = height;
        }

        public void Close(object activation)
        {
            AssertSameActivation(activation);
            CloseCount++;
            ((RecordingActivation)activation).IsClosed = true;
        }

        public void Run(object activation)
        {
            AssertSameActivation(activation);
            RunCount++;
            var typedActivation = (RecordingActivation)activation;
            AssertEqual(true, typedActivation.IsVisible, "startup window visible before run");
            AssertEqual("ProGPU WPF XAML smoke", typedActivation.Title, "activated window title");
            AssertEqual(420.0, typedActivation.Width, "activated window width");
            AssertEqual(260.0, typedActivation.Height, "activated window height");
            Invoke(typedActivation.Window, "UpdateLayout");
            ValidatePostShowItemTemplateTriggerActivation(_presentationCore, typedActivation.Window);
        }

        public void Dispose(object activation)
        {
            AssertSameActivation(activation);
            DisposeCount++;
            ((RecordingActivation)activation).IsDisposed = true;
        }

        public void ValidateAfterRun()
        {
            AssertEqual(1, ActivateCount, "startup window activation count");
            AssertEqual(1, ShowCount, "startup window show count");
            if (RunCount != 1)
            {
                throw new InvalidOperationException(
                    $"Expected portable run-loop count to be '1', got '{RunCount}'. " +
                    $"MainWindow={DescribeMainWindow()}, Activation={DescribeActivation()}.");
            }
            AssertEqual(1, CloseCount, "startup window close count");
            AssertEqual(1, DisposeCount, "startup window dispose count");

            if (_activation is not RecordingActivation activation)
            {
                throw new InvalidOperationException("Application.Run did not create a recording activation.");
            }

            AssertEqual(true, activation.IsClosed, "recorded activation close state");
            AssertEqual(true, activation.IsDisposed, "recorded activation dispose state");
            ValidatePostShowBindingFeatures(activation.Window);
            ValidatePostShowItemTemplateTriggerActivation(_presentationCore, activation.Window);
        }

        private void AssertSameActivation(object activation)
        {
            if (!ReferenceEquals(_activation, activation))
            {
                throw new InvalidOperationException("Portable activation callback received an unknown activation object.");
            }
        }

        private string DescribeMainWindow()
        {
            object? mainWindow = TryGetProperty(_application, "MainWindow");
            if (mainWindow == null)
            {
                return "<null>";
            }

            object? portableActivation = TryGetProperty(mainWindow, "PortableWindowActivation");
            return $"{mainWindow.GetType().FullName}, PortableWindowActivation={(portableActivation == null ? "<null>" : portableActivation.GetType().FullName)}";
        }

        private string DescribeActivation()
        {
            return _activation == null ? "<null>" : _activation.GetType().FullName ?? "<unknown>";
        }
    }

    private sealed class RecordingActivation
    {
        public RecordingActivation(object window)
        {
            Window = window;
        }

        public object Window { get; }

        public bool IsVisible { get; set; }

        public bool IsClosed { get; set; }

        public bool IsDisposed { get; set; }

        public string Title { get; set; } = string.Empty;

        public double Width { get; set; }

        public double Height { get; set; }

        public object? WindowState { get; set; }
    }

    private sealed class WpfAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _repoRoot;
        private readonly string _presentationFrameworkPath;
        private readonly string _presentationCorePath;
        private readonly string _compilerHarnessPath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(
            string repoRoot,
            string presentationFrameworkPath,
            string presentationCorePath,
            string compilerHarnessPath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationFrameworkPath = presentationFrameworkPath;
            _presentationCorePath = presentationCorePath;
            _compilerHarnessPath = compilerHarnessPath;
            _resolver = new AssemblyDependencyResolver(compilerHarnessPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, CompilerHarnessAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_compilerHarnessPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationFramework", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationFrameworkPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string? artifactAssemblyPath = TryFindArtifactAssembly(_repoRoot, assemblyName);
            if (artifactAssemblyPath != null)
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string outputAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName.Name}.dll");
            if (File.Exists(outputAssemblyPath))
            {
                return LoadFromAssemblyPath(outputAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
