using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private const string CompilerHarnessAssemblyName = "ProGPU.Wpf.RealXamlCompilerHarness";
    private const string AppTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow";
    private const string PortableMediaContextRenderServiceTypeName = "System.Windows.Media.PortableMediaContextRenderService";
    private const string PortablePresentationSourceTypeName = "System.Windows.PortablePresentationSource";
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
        ActivationRecorder? recorder = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(compilerHarness, AppTypeName);
            Invoke(application, "InitializeComponent");
            ValidateApplication(application);

            recorder = RegisterPortableActivation(
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
            recorder?.Dispose();

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
        AssertCollectionCount(GetProperty(resources, "Keys"), expected: 8, "application resource keys");
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

        object unsharedAccentBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        object secondUnsharedAccentBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        AssertType(unsharedAccentBrush, "System.Windows.Media.SolidColorBrush", "unshared accent brush");
        AssertEqual("#FF4D6F8E", GetProperty(unsharedAccentBrush, "Color").ToString(), "unshared accent brush color");
        AssertNotSame(unsharedAccentBrush, secondUnsharedAccentBrush, "compiled x:Shared=false resource lookup");

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

    private static void ValidateMainWindow(Assembly presentationCore, object window, object application)
    {
        AssertType(window, MainWindowTypeName, "startup window");
        AssertEqual("ProGPU WPF XAML smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(420.0, GetProperty(window, "Width"), "window width");
        AssertEqual(260.0, GetProperty(window, "Height"), "window height");

        object content = GetProperty(window, "Content");
        AssertType(content, "System.Windows.Controls.StackPanel", "window content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expected: 60, "stack panel children");

        object textBlock = GetCollectionItem(children, 0);
        AssertType(textBlock, "System.Windows.Controls.TextBlock", "compiled TextBlock");
        AssertEqual("Real WPF XAML compiler smoke", GetProperty(textBlock, "Text"), "compiled TextBlock text");
        AssertEqual("#FF356D9E", GetProperty(GetProperty(textBlock, "Foreground"), "Color").ToString(), "compiled TextBlock foreground");

        object inputBox = GetField(window, "InputBox");
        AssertType(inputBox, "System.Windows.Controls.TextBox", "compiled named TextBox");
        AssertEqual("compiled TextBox", GetProperty(inputBox, "Text"), "compiled TextBox text");
        ValidateTextBoxSelection(inputBox);
        ValidatePasswordBox(window);

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
        ValidateXmlDataProvider(window);
        ValidateStoryboardEventTrigger(window);
        ValidateMarkupExtension(window);
        ValidateMergedResourceDictionary(window, application);
        ValidateUnsharedResource(window, application);
        ValidateNestedUserControl(window);
        ValidateReadOnlyGridCollectionsAndAttachedProperties(window);
        ValidateLayoutPanels(window);
        ValidateScrollingControls(window);
        ValidateDateSelectionControls(window);
        ValidateImplicitMergedStyle(window, application);
        ValidateToggleChoiceControls(window);
        ValidateXamlEventHandler(window);
        ValidateStyleEventSetter(window);
        ValidateRoutedCommand(window);
        ValidateInputBinding(window);
        ValidateMenuItems(window);
        ValidateContextMenuAndToolTip(window);
        ValidateToolBarAndStatusBar(window);
        ValidateRangeControls(window);
        ValidateStyleAndDataTrigger(window, application);
        ValidateTemplateAndDynamicResource(window, application);
        ValidateItemsBindingAndTemplate(window);
        ValidateComboBox(window);
        ValidateListViewGridView(window);
        ValidateImplicitDataTemplate(window);
        ValidateContentTemplateSelector(window);
        ValidateHierarchicalDataTemplate(window);
        ValidateTabControl(window);
        ValidateSectionControls(window);
        ValidateAdornerDecorator(window);
        ValidateAccessKeyFocusScope(presentationCore, window);
        ValidateNavigationFrame(window);
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

    private static void ValidatePasswordBox(object window)
    {
        object passwordBox = GetField(window, "CredentialBox");
        AssertType(passwordBox, "System.Windows.Controls.PasswordBox", "compiled PasswordBox");
        AssertEqual(12, GetProperty(passwordBox, "MaxLength"), "compiled PasswordBox max length");
        AssertEqual('#', GetProperty(passwordBox, "PasswordChar"), "compiled PasswordBox password char");
        AssertEqual(string.Empty, GetProperty(passwordBox, "Password"), "compiled PasswordBox initial password");
        object securePassword = GetProperty(passwordBox, "SecurePassword");
        AssertEqual(0, GetProperty(securePassword, "Length"), "compiled PasswordBox initial secure password length");
        AssertEqual(0, GetProperty(window, "PasswordChangedCount"), "compiled PasswordBox initial changed count");

        SetProperty(passwordBox, "Password", "secret42");

        AssertEqual("secret42", GetProperty(passwordBox, "Password"), "compiled PasswordBox updated password");
        securePassword = GetProperty(passwordBox, "SecurePassword");
        AssertEqual(8, GetProperty(securePassword, "Length"), "compiled PasswordBox secure password length");
        AssertEqual(1, GetProperty(window, "PasswordChangedCount"), "compiled PasswordBox PasswordChanged count");
        AssertEqual("CredentialBox", GetProperty(window, "LastPasswordChangedSenderName"), "compiled PasswordBox PasswordChanged sender");
        AssertEqual("PasswordChanged", GetProperty(window, "LastPasswordChangedRoutedEventName"), "compiled PasswordBox PasswordChanged routed event");

        Invoke(passwordBox, "Clear");

        AssertEqual(string.Empty, GetProperty(passwordBox, "Password"), "compiled PasswordBox cleared password");
        securePassword = GetProperty(passwordBox, "SecurePassword");
        AssertEqual(0, GetProperty(securePassword, "Length"), "compiled PasswordBox cleared secure password length");
        AssertEqual(2, GetProperty(window, "PasswordChangedCount"), "compiled PasswordBox clear changed count");
    }

    private static void ValidateRichFlowDocument(object window)
    {
        object richTextBox = GetField(window, "DocumentBox");
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled RichTextBox");

        object flowDocument = GetProperty(richTextBox, "Document");
        AssertType(flowDocument, "System.Windows.Documents.FlowDocument", "compiled FlowDocument");

        object blocks = GetProperty(flowDocument, "Blocks");
        AssertCollectionCount(blocks, expected: 5, "compiled FlowDocument blocks");

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

        object inlineContainer = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.InlineUIContainer", "compiled FlowDocument inline UI container");
        object inlineButton = GetProperty(inlineContainer, "Child");
        AssertType(inlineButton, "System.Windows.Controls.Button", "compiled FlowDocument inline Button");
        AssertEqual("inline document button", GetProperty(inlineButton, "Content"), "compiled FlowDocument inline Button content");

        object selection = GetProperty(richTextBox, "Selection");
        Invoke(selection, "Select", GetProperty(boldRun, "ContentStart"), GetProperty(boldRun, "ContentEnd"));
        AssertEqual("rich", (GetProperty(selection, "Text").ToString() ?? string.Empty).Trim(), "compiled RichTextBox selection text");

        object section = GetCollectionItem(blocks, 1);
        AssertType(section, "System.Windows.Documents.Section", "compiled FlowDocument section");
        object sectionBlocks = GetProperty(section, "Blocks");
        AssertCollectionCount(sectionBlocks, expected: 1, "compiled FlowDocument section blocks");
        AssertFlowDocumentParagraphText(GetCollectionItem(sectionBlocks, 0), "section block text", "section");

        object blockContainer = GetCollectionItem(blocks, 2);
        AssertType(blockContainer, "System.Windows.Documents.BlockUIContainer", "compiled FlowDocument block UI container");
        object blockButton = GetProperty(blockContainer, "Child");
        AssertType(blockButton, "System.Windows.Controls.Button", "compiled FlowDocument block Button");
        AssertEqual("block document button", GetProperty(blockButton, "Content"), "compiled FlowDocument block Button content");

        object table = GetCollectionItem(blocks, 3);
        AssertType(table, "System.Windows.Documents.Table", "compiled FlowDocument table");
        AssertCollectionCount(GetProperty(table, "Columns"), expected: 2, "compiled FlowDocument table columns");
        object rowGroups = GetProperty(table, "RowGroups");
        AssertCollectionCount(rowGroups, expected: 1, "compiled FlowDocument table row groups");
        object rows = GetProperty(GetCollectionItem(rowGroups, 0), "Rows");
        AssertCollectionCount(rows, expected: 1, "compiled FlowDocument table rows");
        object cells = GetProperty(GetCollectionItem(rows, 0), "Cells");
        AssertCollectionCount(cells, expected: 2, "compiled FlowDocument table cells");
        AssertFlowDocumentTableCellText(GetCollectionItem(cells, 0), "table alpha", "first");
        AssertFlowDocumentTableCellText(GetCollectionItem(cells, 1), "table beta", "second");

        object list = GetCollectionItem(blocks, 4);
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
        AssertContains("section block text", text, "compiled FlowDocument TextRange section text");
        AssertContains("table alpha", text, "compiled FlowDocument TextRange first table cell");
        AssertContains("table beta", text, "compiled FlowDocument TextRange second table cell");
        AssertContains("first document item", text, "compiled FlowDocument TextRange first list item");
        AssertContains("second document item", text, "compiled FlowDocument TextRange second list item");
    }

    private static void AssertFlowDocumentParagraphText(object paragraph, string expectedText, string description)
    {
        AssertType(paragraph, "System.Windows.Documents.Paragraph", $"compiled FlowDocument {description} paragraph");
        object run = GetFirstCollectionItemOfType(GetProperty(paragraph, "Inlines"), "System.Windows.Documents.Run", $"compiled FlowDocument {description} run");
        AssertEqual(expectedText, GetProperty(run, "Text"), $"compiled FlowDocument {description} text");
    }

    private static void AssertFlowDocumentTableCellText(object tableCell, string expectedText, string description)
    {
        AssertType(tableCell, "System.Windows.Documents.TableCell", $"compiled FlowDocument {description} table cell");
        AssertFlowDocumentParagraphText(
            GetCollectionItem(GetProperty(tableCell, "Blocks"), 0),
            expectedText,
            $"{description} table cell");
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

        object convertedBindingBlock = GetField(window, "ConvertedBindingBlock");
        AssertType(convertedBindingBlock, "System.Windows.Controls.TextBlock", "compiled converter TextBlock");
        AssertEqual(
            "converted:UPDATED GREETING FROM PROPERTY CHANGE",
            GetProperty(convertedBindingBlock, "Text"),
            "compiled converter binding value");
        object convertedBindingExpression = GetBindingExpression(convertedBindingBlock, "TextProperty");
        object convertedBinding = GetProperty(convertedBindingExpression, "ParentBinding");
        AssertBindingObjectPath(convertedBinding, "Greeting", "compiled converter binding path");
        AssertType(GetProperty(convertedBinding, "Converter"), "ProGPU.Wpf.RealXamlCompilerHarness.SmokeUpperConverter", "compiled converter binding resource");
        AssertEqual("converted", GetProperty(convertedBinding, "ConverterParameter"), "compiled converter parameter");

        object convertedMultiBindingBlock = GetField(window, "ConvertedMultiBindingBlock");
        AssertType(convertedMultiBindingBlock, "System.Windows.Controls.TextBlock", "compiled MultiBinding converter TextBlock");
        AssertEqual(
            "converted-multi:updated greeting from property change|run bound command",
            GetProperty(convertedMultiBindingBlock, "Text"),
            "compiled MultiBinding converter value");
        object convertedMultiBindingExpression = GetMultiBindingExpression(convertedMultiBindingBlock, "TextProperty");
        object convertedMultiBinding = GetProperty(convertedMultiBindingExpression, "ParentMultiBinding");
        AssertType(GetProperty(convertedMultiBinding, "Converter"), "ProGPU.Wpf.RealXamlCompilerHarness.SmokeJoinConverter", "compiled MultiBinding converter resource");
        AssertEqual("converted-multi", GetProperty(convertedMultiBinding, "ConverterParameter"), "compiled MultiBinding converter parameter");
        object convertedMultiBindings = GetProperty(convertedMultiBinding, "Bindings");
        AssertCollectionCount(convertedMultiBindings, expected: 2, "compiled MultiBinding converter child bindings");
        AssertBindingObjectPath(GetCollectionItem(convertedMultiBindings, 0), "Greeting", "compiled MultiBinding converter first path");
        AssertBindingObjectPath(GetCollectionItem(convertedMultiBindings, 1), "ButtonText", "compiled MultiBinding converter second path");

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

        object ruleValidatedBox = GetField(window, "RuleValidatedBox");
        AssertType(ruleValidatedBox, "System.Windows.Controls.TextBox", "compiled ValidationRule TextBox");
        AssertEqual("rule: valid binding text", GetProperty(ruleValidatedBox, "Text"), "compiled ValidationRule TextBox initial text");
        AssertEqual("rule: valid binding text", GetProperty(dataContext, "RuleValidatedText"), "compiled ValidationRule source initial value");
        object ruleBindingExpression = GetBindingExpression(ruleValidatedBox, "TextProperty");
        object ruleBinding = GetProperty(ruleBindingExpression, "ParentBinding");
        AssertBindingObjectPath(ruleBinding, "RuleValidatedText", "compiled ValidationRule binding path");
        object validationRules = GetProperty(ruleBinding, "ValidationRules");
        AssertCollectionCount(validationRules, expected: 1, "compiled Binding ValidationRules");
        object validationRule = GetCollectionItem(validationRules, 0);
        AssertType(validationRule, "ProGPU.Wpf.RealXamlCompilerHarness.SmokePrefixValidationRule", "compiled custom ValidationRule");
        AssertEqual("rule:", GetProperty(validationRule, "RequiredPrefix"), "compiled custom ValidationRule parameter");
        AssertEqual(false, GetDependencyPropertyValue(ruleValidatedBox, validationType, "HasErrorProperty"), "compiled ValidationRule initial error state");

        SetProperty(ruleValidatedBox, "Text", "invalid rule text");
        Invoke(ruleBindingExpression, "UpdateSource");
        AssertEqual("rule: valid binding text", GetProperty(dataContext, "RuleValidatedText"), "compiled ValidationRule rejected source value");
        AssertEqual(true, GetDependencyPropertyValue(ruleValidatedBox, validationType, "HasErrorProperty"), "compiled ValidationRule error state");

        SetProperty(ruleValidatedBox, "Text", "rule: restored binding text");
        Invoke(ruleBindingExpression, "UpdateSource");
        AssertEqual("rule: restored binding text", GetProperty(dataContext, "RuleValidatedText"), "compiled ValidationRule restored source value");
        AssertEqual(false, GetDependencyPropertyValue(ruleValidatedBox, validationType, "HasErrorProperty"), "compiled ValidationRule restored error state");

        ValidateBindingGroup(window, dataContext, validationType);
    }

    private static void ValidateBindingGroup(object window, object dataContext, Type validationType)
    {
        object panel = GetField(window, "BindingGroupPanel");
        AssertType(panel, "System.Windows.Controls.StackPanel", "compiled BindingGroup panel");

        object bindingGroup = GetProperty(panel, "BindingGroup");
        AssertType(bindingGroup, "System.Windows.Data.BindingGroup", "compiled BindingGroup");
        AssertEqual("SmokeBindingGroup", GetProperty(bindingGroup, "Name"), "compiled BindingGroup name");
        AssertCollectionCount(GetProperty(bindingGroup, "Items"), expected: 1, "compiled BindingGroup items");

        object validationRules = GetProperty(bindingGroup, "ValidationRules");
        AssertCollectionCount(validationRules, expected: 1, "compiled BindingGroup ValidationRules");
        object validationRule = GetCollectionItem(validationRules, 0);
        AssertType(validationRule, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeBindingGroupValidationRule", "compiled BindingGroup custom ValidationRule");
        AssertEqual("BindingGroupFirstName", GetProperty(validationRule, "FirstProperty"), "compiled BindingGroup first property");
        AssertEqual("BindingGroupLastName", GetProperty(validationRule, "SecondProperty"), "compiled BindingGroup second property");
        AssertEqual("group:", GetProperty(validationRule, "RequiredPrefix"), "compiled BindingGroup required prefix");

        object firstBox = GetField(window, "BindingGroupFirstBox");
        object lastBox = GetField(window, "BindingGroupLastBox");
        AssertType(firstBox, "System.Windows.Controls.TextBox", "compiled BindingGroup first TextBox");
        AssertType(lastBox, "System.Windows.Controls.TextBox", "compiled BindingGroup last TextBox");
        AssertEqual("group: Ada", GetProperty(firstBox, "Text"), "compiled BindingGroup first initial text");
        AssertEqual("group: Lovelace", GetProperty(lastBox, "Text"), "compiled BindingGroup last initial text");
        AssertEqual("group: Ada", GetProperty(dataContext, "BindingGroupFirstName"), "compiled BindingGroup first initial source");
        AssertEqual("group: Lovelace", GetProperty(dataContext, "BindingGroupLastName"), "compiled BindingGroup last initial source");
        AssertBindingPath(firstBox, "TextProperty", "BindingGroupFirstName", "compiled BindingGroup first binding path");
        AssertBindingPath(lastBox, "TextProperty", "BindingGroupLastName", "compiled BindingGroup last binding path");

        AssertEqual(false, GetDependencyPropertyValue(panel, validationType, "HasErrorProperty"), "compiled BindingGroup initial error state");
        AssertEqual(true, Invoke(bindingGroup, "ValidateWithoutUpdate"), "compiled BindingGroup initial validation");

        SetProperty(firstBox, "Text", "invalid Ada");
        SetProperty(lastBox, "Text", "group: Hopper");
        AssertEqual(false, Invoke(bindingGroup, "CommitEdit"), "compiled BindingGroup rejected commit");
        AssertEqual("group: Ada", GetProperty(dataContext, "BindingGroupFirstName"), "compiled BindingGroup rejected first source");
        AssertEqual("group: Lovelace", GetProperty(dataContext, "BindingGroupLastName"), "compiled BindingGroup rejected last source");
        AssertEqual(true, GetDependencyPropertyValue(panel, validationType, "HasErrorProperty"), "compiled BindingGroup rejected error state");

        SetProperty(firstBox, "Text", "group: Grace");
        SetProperty(lastBox, "Text", "group: Hopper");
        AssertEqual(true, Invoke(bindingGroup, "CommitEdit"), "compiled BindingGroup accepted commit");
        AssertEqual("group: Grace", GetProperty(dataContext, "BindingGroupFirstName"), "compiled BindingGroup accepted first source");
        AssertEqual("group: Hopper", GetProperty(dataContext, "BindingGroupLastName"), "compiled BindingGroup accepted last source");
        AssertEqual(false, GetDependencyPropertyValue(panel, validationType, "HasErrorProperty"), "compiled BindingGroup accepted error state");
    }

    private static void ValidatePostShowBindingFeatures(object window)
    {
        object relativeSourceBlock = GetField(window, "RelativeSourceBlock");
        AssertType(relativeSourceBlock, "System.Windows.Controls.TextBlock", "compiled RelativeSource TextBlock");
        AssertEqual("ancestor binding source", GetProperty(relativeSourceBlock, "Text"), "compiled RelativeSource ancestor binding value");
        AssertBindingPath(relativeSourceBlock, "TextProperty", "Tag", "compiled RelativeSource binding path");
    }

    private static void ValidatePostShowLoadedEvent(object window)
    {
        object storyboardTargetBlock = GetField(window, "StoryboardTargetBlock");
        AssertEqual(true, GetProperty(storyboardTargetBlock, "IsLoaded"), "compiled Storyboard target loaded state");
        AssertEqual(0.37, GetProperty(storyboardTargetBlock, "Opacity"), "compiled Storyboard target post-Loaded opacity");
        ValidateLoadedEventHandlerState(window);
    }

    private static void ValidateLoadedEventHandlerState(object window)
    {
        AssertEqual(1, GetProperty(window, "StoryboardTargetLoadedCount"), "compiled Storyboard target Loaded handler count");
        AssertEqual("StoryboardTargetBlock", GetProperty(window, "LastStoryboardTargetLoadedSenderName"), "compiled Storyboard target Loaded sender name");
        AssertEqual("Loaded", GetProperty(window, "LastStoryboardTargetLoadedRoutedEventName"), "compiled Storyboard target Loaded routed event name");
    }

    private static void ValidatePostShowItemTemplateTriggerActivation(Assembly presentationCore, object window)
    {
        object itemsList = GetField(window, "ItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");
        object alphaItem = GetCollectionItem(sourceItems, 0);
        ValidateGeneratedItemTemplateTextBlock(
            presentationCore,
            itemsList,
            alphaItem,
            "item alpha",
            "container trigger inactive",
            "template trigger inactive",
            "compiled DataTemplate inactive generated item container",
            "compiled ItemContainerStyle trigger inactive generated value",
            "compiled DataTemplate inactive generated TextBlock",
            "compiled DataTemplate inactive generated TextBlock binding",
            "compiled DataTemplate trigger inactive generated value");

        object betaItem = GetCollectionItem(sourceItems, 1);
        ValidateGeneratedItemTemplateTextBlock(
            presentationCore,
            itemsList,
            betaItem,
            "item beta",
            "container trigger active",
            "template trigger active",
            "compiled DataTemplate active generated item container",
            "compiled ItemContainerStyle trigger active generated value",
            "compiled DataTemplate active generated TextBlock",
            "compiled DataTemplate active generated TextBlock binding",
            "compiled DataTemplate trigger active generated value");
    }

    private static void ValidatePostShowGroupStyleHeader(Assembly presentationCore, object window)
    {
        object groupedItemsList = GetField(window, "GroupedItemsList");
        Invoke(groupedItemsList, "ApplyTemplate");
        Invoke(groupedItemsList, "UpdateLayout");

        object groupHeaderTextBlock = FindVisualDescendantByName(presentationCore, groupedItemsList, "GroupHeaderTextBlock")
            ?? throw new InvalidOperationException("Expected grouped ListBox to generate GroupHeaderTextBlock.");
        AssertType(groupHeaderTextBlock, "System.Windows.Controls.TextBlock", "compiled GroupStyle generated header TextBlock");
        AssertEqual("primary group", GetProperty(groupHeaderTextBlock, "Text"), "compiled GroupStyle header generated binding");
        AssertEqual("group header template", GetProperty(groupHeaderTextBlock, "Tag"), "compiled GroupStyle header generated value");
    }

    private static void ValidateGeneratedItemTemplateTextBlock(
        Assembly presentationCore,
        object itemsList,
        object item,
        string expectedText,
        string expectedContainerTag,
        string expectedTag,
        string itemContainerDescription,
        string itemContainerTagDescription,
        string textBlockDescription,
        string bindingDescription,
        string tagDescription)
    {
        Invoke(itemsList, "ScrollIntoView", item);
        Invoke(itemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(itemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", itemContainerDescription);
        AssertEqual(expectedContainerTag, GetProperty(itemContainer, "Tag"), itemContainerTagDescription);
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object itemTextBlock = FindVisualDescendantByName(presentationCore, itemContainer, "ItemTextBlock")
            ?? throw new InvalidOperationException("Expected generated item container to contain ItemTextBlock.");
        AssertType(itemTextBlock, "System.Windows.Controls.TextBlock", textBlockDescription);
        AssertEqual(expectedText, GetProperty(itemTextBlock, "Text"), bindingDescription);
        AssertEqual(expectedTag, GetProperty(itemTextBlock, "Tag"), tagDescription);
    }

    private static void ValidatePostShowItemTemplateSelector(Assembly presentationCore, object window)
    {
        object selectorItemsList = GetField(window, "SelectorItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");

        ValidateGeneratedSelectedTemplateTextBlock(
            presentationCore,
            selectorItemsList,
            GetCollectionItem(sourceItems, 0),
            "item alpha",
            "selector alpha template",
            "compiled DataTemplateSelector alpha generated item container",
            "compiled DataTemplateSelector alpha generated TextBlock",
            "compiled DataTemplateSelector alpha generated TextBlock binding",
            "compiled DataTemplateSelector alpha generated value");

        ValidateGeneratedSelectedTemplateTextBlock(
            presentationCore,
            selectorItemsList,
            GetCollectionItem(sourceItems, 1),
            "item beta",
            "selector default template",
            "compiled DataTemplateSelector default generated item container",
            "compiled DataTemplateSelector default generated TextBlock",
            "compiled DataTemplateSelector default generated TextBlock binding",
            "compiled DataTemplateSelector default generated value");
    }

    private static void ValidateGeneratedSelectedTemplateTextBlock(
        Assembly presentationCore,
        object selectorItemsList,
        object item,
        string expectedText,
        string expectedTag,
        string itemContainerDescription,
        string textBlockDescription,
        string bindingDescription,
        string tagDescription)
    {
        Invoke(selectorItemsList, "ScrollIntoView", item);
        Invoke(selectorItemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(selectorItemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", itemContainerDescription);
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object itemTextBlock = FindVisualDescendantByName(presentationCore, itemContainer, "SelectorTemplateTextBlock")
            ?? throw new InvalidOperationException("Expected selector-generated item container to contain SelectorTemplateTextBlock.");
        AssertType(itemTextBlock, "System.Windows.Controls.TextBlock", textBlockDescription);
        AssertEqual(expectedText, GetProperty(itemTextBlock, "Text"), bindingDescription);
        AssertEqual(expectedTag, GetProperty(itemTextBlock, "Tag"), tagDescription);
    }

    private static void ValidatePostShowItemContainerStyleSelector(Assembly presentationCore, object window)
    {
        object styleSelectorItemsList = GetField(window, "StyleSelectorItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");

        ValidateGeneratedStyleSelectorItem(
            presentationCore,
            styleSelectorItemsList,
            GetCollectionItem(sourceItems, 0),
            "item alpha",
            "style selector alpha container",
            "compiled ItemContainerStyleSelector alpha generated item container",
            "compiled ItemContainerStyleSelector alpha generated container style",
            "compiled ItemContainerStyleSelector alpha generated TextBlock",
            "compiled ItemContainerStyleSelector alpha generated TextBlock binding");

        ValidateGeneratedStyleSelectorItem(
            presentationCore,
            styleSelectorItemsList,
            GetCollectionItem(sourceItems, 1),
            "item beta",
            "style selector default container",
            "compiled ItemContainerStyleSelector default generated item container",
            "compiled ItemContainerStyleSelector default generated container style",
            "compiled ItemContainerStyleSelector default generated TextBlock",
            "compiled ItemContainerStyleSelector default generated TextBlock binding");
    }

    private static void ValidateGeneratedStyleSelectorItem(
        Assembly presentationCore,
        object styleSelectorItemsList,
        object item,
        string expectedText,
        string expectedContainerTag,
        string itemContainerDescription,
        string itemContainerTagDescription,
        string textBlockDescription,
        string bindingDescription)
    {
        Invoke(styleSelectorItemsList, "ScrollIntoView", item);
        Invoke(styleSelectorItemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(styleSelectorItemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", itemContainerDescription);
        AssertEqual(expectedContainerTag, GetProperty(itemContainer, "Tag"), itemContainerTagDescription);
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object itemTextBlock = FindVisualDescendantByName(presentationCore, itemContainer, "StyleSelectorItemTextBlock")
            ?? throw new InvalidOperationException("Expected style-selector-generated item container to contain StyleSelectorItemTextBlock.");
        AssertType(itemTextBlock, "System.Windows.Controls.TextBlock", textBlockDescription);
        AssertEqual(expectedText, GetProperty(itemTextBlock, "Text"), bindingDescription);
        AssertEqual("style selector item template", GetProperty(itemTextBlock, "Tag"), "compiled ItemContainerStyleSelector generated TextBlock tag");
    }

    private static void ValidatePostShowImplicitDataTemplate(Assembly presentationCore, object window)
    {
        object implicitTemplateHost = GetField(window, "ImplicitTemplateHost");
        Invoke(implicitTemplateHost, "ApplyTemplate");
        Invoke(implicitTemplateHost, "UpdateLayout");

        object detailTextBlock = FindVisualDescendantByName(presentationCore, implicitTemplateHost, "ImplicitDetailTextBlock")
            ?? throw new InvalidOperationException("Expected implicit data template host to contain ImplicitDetailTextBlock.");
        AssertType(detailTextBlock, "System.Windows.Controls.TextBlock", "compiled implicit DataTemplate generated TextBlock");
        AssertEqual("detail from implicit template", GetProperty(detailTextBlock, "Text"), "compiled implicit DataTemplate generated TextBlock binding");
        AssertEqual("implicit data template", GetProperty(detailTextBlock, "Tag"), "compiled implicit DataTemplate generated value");
    }

    private static void ValidatePostShowContentTemplateSelector(Assembly presentationCore, object window)
    {
        object selectorTemplateHost = GetField(window, "SelectorTemplateHost");
        Invoke(selectorTemplateHost, "ApplyTemplate");
        Invoke(selectorTemplateHost, "UpdateLayout");

        object detailTextBlock = FindVisualDescendantByName(presentationCore, selectorTemplateHost, "SelectedDetailTextBlock")
            ?? throw new InvalidOperationException("Expected ContentTemplateSelector host to contain SelectedDetailTextBlock.");
        AssertType(detailTextBlock, "System.Windows.Controls.TextBlock", "compiled ContentTemplateSelector generated TextBlock");
        AssertEqual("detail from implicit template", GetProperty(detailTextBlock, "Text"), "compiled ContentTemplateSelector generated TextBlock binding");
        AssertEqual("content template selector selected", GetProperty(detailTextBlock, "Tag"), "compiled ContentTemplateSelector generated value");
    }

    private static void ValidatePostShowHierarchicalDataTemplate(Assembly presentationCore, object window)
    {
        object nodeTree = GetField(window, "NodeTree");
        object sourceNodes = GetProperty(GetProperty(window, "DataContext"), "Nodes");
        object rootNode = GetCollectionItem(sourceNodes, 0);
        Invoke(nodeTree, "UpdateLayout");

        object rootContainer = Invoke(GetProperty(nodeTree, "ItemContainerGenerator"), "ContainerFromItem", rootNode);
        AssertType(rootContainer, "System.Windows.Controls.TreeViewItem", "compiled HierarchicalDataTemplate root container");
        Invoke(rootContainer, "ApplyTemplate");
        SetProperty(rootContainer, "IsExpanded", true);
        Invoke(rootContainer, "UpdateLayout");
        Invoke(nodeTree, "UpdateLayout");

        object rootTextBlock = FindVisualDescendantByName(presentationCore, rootContainer, "NodeTextBlock")
            ?? throw new InvalidOperationException("Expected generated root TreeViewItem to contain NodeTextBlock.");
        AssertType(rootTextBlock, "System.Windows.Controls.TextBlock", "compiled HierarchicalDataTemplate root generated TextBlock");
        AssertEqual("root node", GetProperty(rootTextBlock, "Text"), "compiled HierarchicalDataTemplate root generated TextBlock binding");
        AssertEqual("hierarchical template", GetProperty(rootTextBlock, "Tag"), "compiled HierarchicalDataTemplate root generated value");

        object rootChildren = GetProperty(rootNode, "Children");
        AssertCollectionCount(GetProperty(rootContainer, "Items"), expected: 2, "compiled HierarchicalDataTemplate generated child items");
        object childNode = GetCollectionItem(rootChildren, 0);
        object childContainer = Invoke(GetProperty(rootContainer, "ItemContainerGenerator"), "ContainerFromItem", childNode);
        AssertType(childContainer, "System.Windows.Controls.TreeViewItem", "compiled HierarchicalDataTemplate child container");
        Invoke(childContainer, "ApplyTemplate");
        Invoke(childContainer, "UpdateLayout");

        object childTextBlock = FindVisualDescendantByName(presentationCore, childContainer, "NodeTextBlock")
            ?? throw new InvalidOperationException("Expected generated child TreeViewItem to contain NodeTextBlock.");
        AssertType(childTextBlock, "System.Windows.Controls.TextBlock", "compiled HierarchicalDataTemplate child generated TextBlock");
        AssertEqual("child alpha", GetProperty(childTextBlock, "Text"), "compiled HierarchicalDataTemplate child generated TextBlock binding");
        AssertEqual("hierarchical template", GetProperty(childTextBlock, "Tag"), "compiled HierarchicalDataTemplate child generated value");
    }

    private static void ValidatePostShowTabControl(Assembly presentationCore, object window)
    {
        object tabControl = GetField(window, "SmokeTabControl");
        Invoke(tabControl, "ApplyTemplate");
        Invoke(tabControl, "UpdateLayout");

        object items = GetProperty(tabControl, "Items");
        object betaTab = GetCollectionItem(items, 1);
        AssertSame(betaTab, GetProperty(tabControl, "SelectedItem"), "compiled TabControl post-show selected item");
        AssertEqual(1, GetProperty(tabControl, "SelectedIndex"), "compiled TabControl post-show selected index");
        AssertSame(GetProperty(betaTab, "Content"), GetProperty(tabControl, "SelectedContent"), "compiled TabControl post-show selected content");

        object betaContent = FindVisualDescendantByName(presentationCore, tabControl, "BetaTabContent")
            ?? throw new InvalidOperationException("Expected selected TabControl content to contain BetaTabContent.");
        AssertType(betaContent, "System.Windows.Controls.TextBlock", "compiled TabControl beta generated content");
        AssertEqual("beta tab content", GetProperty(betaContent, "Text"), "compiled TabControl beta generated content text");
        AssertEqual("tab beta content", GetProperty(betaContent, "Tag"), "compiled TabControl beta generated content tag");

        SetProperty(tabControl, "SelectedIndex", 0);
        Invoke(tabControl, "UpdateLayout");

        object alphaTab = GetCollectionItem(items, 0);
        AssertSame(alphaTab, GetProperty(tabControl, "SelectedItem"), "compiled TabControl selected item after index change");
        AssertEqual(0, GetProperty(tabControl, "SelectedIndex"), "compiled TabControl selected index after change");
        AssertSame(GetProperty(alphaTab, "Content"), GetProperty(tabControl, "SelectedContent"), "compiled TabControl selected content after change");

        object alphaContent = FindVisualDescendantByName(presentationCore, tabControl, "AlphaTabContent")
            ?? throw new InvalidOperationException("Expected selected TabControl content to contain AlphaTabContent.");
        AssertType(alphaContent, "System.Windows.Controls.TextBlock", "compiled TabControl alpha generated content");
        AssertEqual("alpha tab content", GetProperty(alphaContent, "Text"), "compiled TabControl alpha generated content text");
        AssertEqual("tab alpha content", GetProperty(alphaContent, "Tag"), "compiled TabControl alpha generated content tag");

        SetProperty(tabControl, "SelectedIndex", 1);
        Invoke(tabControl, "UpdateLayout");
    }

    private static void ValidatePostShowSectionControls(Assembly presentationCore, object window)
    {
        object expander = GetField(window, "SmokeExpander");
        Invoke(expander, "ApplyTemplate");
        Invoke(expander, "UpdateLayout");

        object expanderHeader = FindVisualDescendantByName(presentationCore, expander, "ExpanderHeaderTextBlock")
            ?? throw new InvalidOperationException("Expected Expander to generate ExpanderHeaderTextBlock.");
        AssertType(expanderHeader, "System.Windows.Controls.TextBlock", "compiled Expander generated header");
        AssertEqual("detail from implicit template", GetProperty(expanderHeader, "Text"), "compiled Expander generated header binding");
        AssertEqual("expander header template", GetProperty(expanderHeader, "Tag"), "compiled Expander generated header tag");

        object expanderContent = FindVisualDescendantByName(presentationCore, expander, "ExpanderContentText")
            ?? throw new InvalidOperationException("Expected expanded Expander to generate ExpanderContentText.");
        AssertType(expanderContent, "System.Windows.Controls.TextBlock", "compiled Expander generated content");
        AssertEqual("updated greeting from property change", GetProperty(expanderContent, "Text"), "compiled Expander generated content binding");
        AssertEqual("expander content", GetProperty(expanderContent, "Tag"), "compiled Expander generated content tag");

        SetProperty(expander, "IsExpanded", false);
        AssertEqual(false, GetProperty(expander, "IsExpanded"), "compiled Expander collapsed state");
        SetProperty(expander, "IsExpanded", true);
        Invoke(expander, "UpdateLayout");
        AssertEqual(true, GetProperty(expander, "IsExpanded"), "compiled Expander restored expanded state");

        object groupBox = GetField(window, "SmokeGroupBox");
        Invoke(groupBox, "ApplyTemplate");
        Invoke(groupBox, "UpdateLayout");

        object groupHeader = FindVisualDescendantByName(presentationCore, groupBox, "GroupBoxHeaderTextBlock")
            ?? throw new InvalidOperationException("Expected GroupBox to generate GroupBoxHeaderTextBlock.");
        AssertType(groupHeader, "System.Windows.Controls.TextBlock", "compiled GroupBox generated header");
        AssertEqual("detail from implicit template", GetProperty(groupHeader, "Text"), "compiled GroupBox generated header binding");
        AssertEqual("group box header template", GetProperty(groupHeader, "Tag"), "compiled GroupBox generated header tag");

        object groupContent = FindVisualDescendantByName(presentationCore, groupBox, "GroupBoxContentText")
            ?? throw new InvalidOperationException("Expected GroupBox to generate GroupBoxContentText.");
        AssertType(groupContent, "System.Windows.Controls.TextBlock", "compiled GroupBox generated content");
        AssertEqual("run bound command", GetProperty(groupContent, "Text"), "compiled GroupBox generated content binding");
        AssertEqual("group box content", GetProperty(groupContent, "Tag"), "compiled GroupBox generated content tag");
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

    private static void ValidateXmlDataProvider(object window)
    {
        object provider = Invoke(window, "TryFindResource", "ProviderXml");
        AssertType(provider, "System.Windows.Data.XmlDataProvider", "compiled XmlDataProvider resource");
        AssertEqual("/Smoke/Message", GetProperty(provider, "XPath"), "compiled XmlDataProvider XPath");
        AssertEqual(false, GetProperty(provider, "IsAsynchronous"), "compiled XmlDataProvider synchronous flag");

        object xmlProviderBlock = GetField(window, "XmlProviderBlock");
        AssertType(xmlProviderBlock, "System.Windows.Controls.TextBlock", "compiled XmlDataProvider TextBlock");
        AssertEqual("xml provider text", GetProperty(xmlProviderBlock, "Text"), "compiled XmlDataProvider XPath bound text");

        object bindingExpression = GetBindingExpression(xmlProviderBlock, "TextProperty");
        object parentBinding = GetProperty(bindingExpression, "ParentBinding");
        AssertSame(provider, GetProperty(parentBinding, "Source"), "compiled XmlDataProvider binding source");
        AssertEqual("@Text", GetProperty(parentBinding, "XPath"), "compiled XmlDataProvider binding XPath");
    }

    private static void ValidateStoryboardEventTrigger(object window)
    {
        object storyboardTargetBlock = GetField(window, "StoryboardTargetBlock");
        AssertType(storyboardTargetBlock, "System.Windows.Controls.TextBlock", "compiled Storyboard target TextBlock");
        AssertEqual("compiled storyboard target", GetProperty(storyboardTargetBlock, "Text"), "compiled Storyboard target text");
        AssertEqual(1.0, GetProperty(storyboardTargetBlock, "Opacity"), "compiled Storyboard target initial opacity");
        AssertEqual(0, GetProperty(window, "StoryboardTargetLoadedCount"), "compiled Storyboard target initial Loaded count");

        object triggers = GetProperty(storyboardTargetBlock, "Triggers");
        AssertCollectionCount(triggers, expected: 1, "compiled EventTrigger collection");
        object eventTrigger = GetCollectionItem(triggers, 0);
        AssertType(eventTrigger, "System.Windows.EventTrigger", "compiled EventTrigger");
        AssertEqual("Loaded", GetProperty(GetProperty(eventTrigger, "RoutedEvent"), "Name"), "compiled EventTrigger routed event");

        object actions = GetProperty(eventTrigger, "Actions");
        AssertCollectionCount(actions, expected: 1, "compiled EventTrigger actions");
        object beginStoryboard = GetCollectionItem(actions, 0);
        AssertType(beginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled BeginStoryboard action");

        object storyboard = GetProperty(beginStoryboard, "Storyboard");
        AssertType(storyboard, "System.Windows.Media.Animation.Storyboard", "compiled Storyboard");
        object children = GetProperty(storyboard, "Children");
        AssertCollectionCount(children, expected: 1, "compiled Storyboard children");
        object doubleAnimation = GetCollectionItem(children, 0);
        AssertType(doubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled DoubleAnimation");
        AssertEqual(0.37, GetProperty(doubleAnimation, "To"), "compiled DoubleAnimation target value");
        AssertEqual("00:00:00", GetProperty(doubleAnimation, "Duration").ToString(), "compiled DoubleAnimation duration");
        AssertEqual("HoldEnd", GetProperty(doubleAnimation, "FillBehavior").ToString(), "compiled DoubleAnimation fill behavior");

        object storyboardTriggerButton = GetField(window, "StoryboardTriggerButton");
        AssertType(storyboardTriggerButton, "System.Windows.Controls.Button", "compiled click Storyboard trigger Button");
        AssertEqual("run storyboard trigger", GetProperty(storyboardTriggerButton, "Content"), "compiled click Storyboard trigger Button content");
        AssertEqual(1.0, GetProperty(storyboardTriggerButton, "Opacity"), "compiled click Storyboard trigger Button initial opacity");

        object clickTriggers = GetProperty(storyboardTriggerButton, "Triggers");
        AssertCollectionCount(clickTriggers, expected: 1, "compiled click EventTrigger collection");
        object clickEventTrigger = GetCollectionItem(clickTriggers, 0);
        AssertType(clickEventTrigger, "System.Windows.EventTrigger", "compiled click EventTrigger");
        AssertEqual("Click", GetProperty(GetProperty(clickEventTrigger, "RoutedEvent"), "Name"), "compiled click EventTrigger routed event");

        object clickActions = GetProperty(clickEventTrigger, "Actions");
        AssertCollectionCount(clickActions, expected: 1, "compiled click EventTrigger actions");
        object clickBeginStoryboard = GetCollectionItem(clickActions, 0);
        AssertType(clickBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled click BeginStoryboard action");

        object clickStoryboard = GetProperty(clickBeginStoryboard, "Storyboard");
        AssertType(clickStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled click Storyboard");
        object clickChildren = GetProperty(clickStoryboard, "Children");
        AssertCollectionCount(clickChildren, expected: 1, "compiled click Storyboard children");
        object clickDoubleAnimation = GetCollectionItem(clickChildren, 0);
        AssertType(clickDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled click DoubleAnimation");
        AssertEqual(0.64, GetProperty(clickDoubleAnimation, "To"), "compiled click DoubleAnimation target value");
        AssertEqual("00:00:00", GetProperty(clickDoubleAnimation, "Duration").ToString(), "compiled click DoubleAnimation duration");
        AssertEqual("HoldEnd", GetProperty(clickDoubleAnimation, "FillBehavior").ToString(), "compiled click DoubleAnimation fill behavior");
    }

    private static void ValidatePostShowClickStoryboardEventTrigger(object window, Action flushRender)
    {
        object storyboardTriggerButton = GetField(window, "StoryboardTriggerButton");
        AssertEqual(1.0, GetProperty(storyboardTriggerButton, "Opacity"), "compiled click Storyboard trigger Button pre-click opacity");

        Invoke(storyboardTriggerButton, "OnClick");
        flushRender();

        AssertEqual(0.64, GetProperty(storyboardTriggerButton, "Opacity"), "compiled click Storyboard trigger Button post-click opacity");
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

    private static void ValidateUnsharedResource(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object dictionaryBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        object secondDictionaryBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        AssertNotSame(dictionaryBrush, secondDictionaryBrush, "compiled x:Shared=false dictionary lookup");

        object borderA = GetField(window, "UnsharedResourceBorderA");
        object borderB = GetField(window, "UnsharedResourceBorderB");
        AssertType(borderA, "System.Windows.Controls.Border", "compiled unshared-resource first Border");
        AssertType(borderB, "System.Windows.Controls.Border", "compiled unshared-resource second Border");

        object backgroundA = GetProperty(borderA, "Background");
        object backgroundB = GetProperty(borderB, "Background");
        AssertType(backgroundA, "System.Windows.Media.SolidColorBrush", "compiled unshared-resource first brush");
        AssertType(backgroundB, "System.Windows.Media.SolidColorBrush", "compiled unshared-resource second brush");
        AssertEqual("#FF4D6F8E", GetProperty(backgroundA, "Color").ToString(), "compiled unshared-resource first color");
        AssertEqual("#FF4D6F8E", GetProperty(backgroundB, "Color").ToString(), "compiled unshared-resource second color");
        AssertNotSame(backgroundA, backgroundB, "compiled x:Shared=false StaticResource consumers");
        AssertNotSame(dictionaryBrush, backgroundA, "compiled x:Shared=false dictionary and first consumer");
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

    private static void ValidateLayoutPanels(object window)
    {
        object layoutPanel = GetField(window, "LayoutPanelSmoke");
        AssertType(layoutPanel, "System.Windows.Controls.StackPanel", "compiled layout panel host");
        AssertCollectionCount(GetProperty(layoutPanel, "Children"), expected: 3, "compiled layout panel host children");

        object dockPanel = GetField(window, "DockPanelSmoke");
        AssertType(dockPanel, "System.Windows.Controls.DockPanel", "compiled DockPanel");
        AssertEqual(false, GetProperty(dockPanel, "LastChildFill"), "compiled DockPanel LastChildFill");
        AssertCollectionCount(GetProperty(dockPanel, "Children"), expected: 2, "compiled DockPanel children");

        object dockLeft = GetField(window, "DockPanelLeftChild");
        AssertType(dockLeft, "System.Windows.Controls.TextBlock", "compiled DockPanel left child");
        AssertEqual("dock left", GetProperty(dockLeft, "Text"), "compiled DockPanel left child text");
        AssertEqual("Left", GetDependencyPropertyValue(dockLeft, dockPanel.GetType(), "DockProperty").ToString(), "compiled DockPanel left attached Dock");

        object dockRight = GetField(window, "DockPanelRightChild");
        AssertType(dockRight, "System.Windows.Controls.TextBlock", "compiled DockPanel right child");
        AssertEqual("dock right", GetProperty(dockRight, "Text"), "compiled DockPanel right child text");
        AssertEqual("Right", GetDependencyPropertyValue(dockRight, dockPanel.GetType(), "DockProperty").ToString(), "compiled DockPanel right attached Dock");

        object canvas = GetField(window, "CanvasSmoke");
        AssertType(canvas, "System.Windows.Controls.Canvas", "compiled Canvas");
        AssertEqual(120.0, GetProperty(canvas, "Width"), "compiled Canvas width");
        AssertEqual(32.0, GetProperty(canvas, "Height"), "compiled Canvas height");
        AssertCollectionCount(GetProperty(canvas, "Children"), expected: 1, "compiled Canvas children");

        object canvasChild = GetField(window, "CanvasChild");
        AssertType(canvasChild, "System.Windows.Controls.TextBlock", "compiled Canvas child");
        AssertEqual("canvas child", GetProperty(canvasChild, "Text"), "compiled Canvas child text");
        AssertEqual(12.0, GetDependencyPropertyValue(canvasChild, canvas.GetType(), "LeftProperty"), "compiled Canvas left attached property");
        AssertEqual(6.0, GetDependencyPropertyValue(canvasChild, canvas.GetType(), "TopProperty"), "compiled Canvas top attached property");

        object wrapPanel = GetField(window, "WrapPanelSmoke");
        AssertType(wrapPanel, "System.Windows.Controls.WrapPanel", "compiled WrapPanel");
        AssertEqual("Horizontal", GetProperty(wrapPanel, "Orientation").ToString(), "compiled WrapPanel orientation");
        AssertEqual(64.0, GetProperty(wrapPanel, "ItemWidth"), "compiled WrapPanel item width");
        AssertEqual(20.0, GetProperty(wrapPanel, "ItemHeight"), "compiled WrapPanel item height");
        AssertCollectionCount(GetProperty(wrapPanel, "Children"), expected: 2, "compiled WrapPanel children");

        object wrapFirst = GetField(window, "WrapFirstChild");
        AssertType(wrapFirst, "System.Windows.Controls.TextBlock", "compiled WrapPanel first child");
        AssertEqual("wrap one", GetProperty(wrapFirst, "Text"), "compiled WrapPanel first child text");

        object wrapSecond = GetField(window, "WrapSecondChild");
        AssertType(wrapSecond, "System.Windows.Controls.TextBlock", "compiled WrapPanel second child");
        AssertEqual("wrap two", GetProperty(wrapSecond, "Text"), "compiled WrapPanel second child text");
    }

    private static void ValidateScrollingControls(object window)
    {
        object scrollingPanel = GetField(window, "ScrollingSmokePanel");
        AssertType(scrollingPanel, "System.Windows.Controls.StackPanel", "compiled scrolling panel host");
        AssertCollectionCount(GetProperty(scrollingPanel, "Children"), expected: 2, "compiled scrolling panel host children");

        object scrollViewer = GetField(window, "ScrollViewerSmoke");
        AssertType(scrollViewer, "System.Windows.Controls.ScrollViewer", "compiled ScrollViewer");
        AssertEqual(160.0, GetProperty(scrollViewer, "Width"), "compiled ScrollViewer width");
        AssertEqual(48.0, GetProperty(scrollViewer, "Height"), "compiled ScrollViewer height");
        AssertEqual(false, GetProperty(scrollViewer, "CanContentScroll"), "compiled ScrollViewer CanContentScroll");
        AssertEqual("Disabled", GetProperty(scrollViewer, "HorizontalScrollBarVisibility").ToString(), "compiled ScrollViewer horizontal visibility");
        AssertEqual("Visible", GetProperty(scrollViewer, "VerticalScrollBarVisibility").ToString(), "compiled ScrollViewer vertical visibility");

        object scrollContent = GetField(window, "ScrollViewerContent");
        AssertType(scrollContent, "System.Windows.Controls.StackPanel", "compiled ScrollViewer content");
        AssertSame(scrollContent, GetProperty(scrollViewer, "Content"), "compiled ScrollViewer content object");
        AssertCollectionCount(GetProperty(scrollContent, "Children"), expected: 6, "compiled ScrollViewer content children");
        object firstItem = GetField(window, "ScrollViewerFirstItem");
        AssertType(firstItem, "System.Windows.Controls.TextBlock", "compiled ScrollViewer first item");
        AssertEqual("scroll first", GetProperty(firstItem, "Text"), "compiled ScrollViewer first item text");
        object sixthItem = GetField(window, "ScrollViewerSixthItem");
        AssertType(sixthItem, "System.Windows.Controls.TextBlock", "compiled ScrollViewer sixth item");
        AssertEqual("scroll sixth", GetProperty(sixthItem, "Text"), "compiled ScrollViewer sixth item text");

        object scrollBar = GetField(window, "VerticalScrollBarSmoke");
        AssertType(scrollBar, "System.Windows.Controls.Primitives.ScrollBar", "compiled vertical ScrollBar");
        AssertEqual("Vertical", GetProperty(scrollBar, "Orientation").ToString(), "compiled ScrollBar orientation");
        AssertEqual(0.0, GetProperty(scrollBar, "Minimum"), "compiled ScrollBar minimum");
        AssertEqual(10.0, GetProperty(scrollBar, "Maximum"), "compiled ScrollBar maximum");
        AssertEqual(4.0, GetProperty(scrollBar, "Value"), "compiled ScrollBar initial value");
        AssertEqual(1.0, GetProperty(scrollBar, "SmallChange"), "compiled ScrollBar small change");
        AssertEqual(3.0, GetProperty(scrollBar, "LargeChange"), "compiled ScrollBar large change");
        AssertEqual(2.0, GetProperty(scrollBar, "ViewportSize"), "compiled ScrollBar viewport size");

        SetProperty(scrollBar, "Value", 7.0);
        AssertEqual(7.0, GetProperty(scrollBar, "Value"), "compiled ScrollBar updated value");
    }

    private static void ValidatePostShowScrollingControls(object window)
    {
        object scrollViewer = GetField(window, "ScrollViewerSmoke");
        Invoke(scrollViewer, "UpdateLayout");
        double scrollableHeight = Convert.ToDouble(GetProperty(scrollViewer, "ScrollableHeight"));
        if (scrollableHeight <= 0)
        {
            throw new InvalidOperationException($"Expected compiled ScrollViewer scrollable height to be positive, got '{scrollableHeight}'.");
        }

        double targetOffset = Math.Min(12.0, scrollableHeight);
        Invoke(scrollViewer, "ScrollToVerticalOffset", targetOffset);
        Invoke(window, "UpdateLayout");

        AssertEqual(targetOffset, GetProperty(scrollViewer, "VerticalOffset"), "compiled ScrollViewer vertical offset");
    }

    private static void ValidateDateSelectionControls(object window)
    {
        object datePanel = GetField(window, "DateSelectionSmokePanel");
        AssertType(datePanel, "System.Windows.Controls.StackPanel", "compiled date-selection panel host");
        AssertCollectionCount(GetProperty(datePanel, "Children"), expected: 2, "compiled date-selection panel children");

        object calendar = GetField(window, "CalendarSmoke");
        AssertType(calendar, "System.Windows.Controls.Calendar", "compiled Calendar");
        AssertEqual("Month", GetProperty(calendar, "DisplayMode").ToString(), "compiled Calendar display mode");
        AssertEqual("SingleDate", GetProperty(calendar, "SelectionMode").ToString(), "compiled Calendar selection mode");
        AssertEqual("Monday", GetProperty(calendar, "FirstDayOfWeek").ToString(), "compiled Calendar first day of week");
        AssertEqual(false, GetProperty(calendar, "IsTodayHighlighted"), "compiled Calendar today highlight");
        AssertDate(GetProperty(calendar, "DisplayDateStart"), 2026, 1, 1, "compiled Calendar display start");
        AssertDate(GetProperty(calendar, "DisplayDateEnd"), 2026, 12, 31, "compiled Calendar display end");
        AssertDate(GetProperty(calendar, "DisplayDate"), 2026, 6, 1, "compiled Calendar display date");
        AssertDate(GetProperty(calendar, "SelectedDate"), 2026, 6, 17, "compiled Calendar selected date");
        object selectedDates = GetProperty(calendar, "SelectedDates");
        AssertCollectionCount(selectedDates, expected: 1, "compiled Calendar selected dates");
        AssertDate(GetCollectionItem(selectedDates, 0), 2026, 6, 17, "compiled Calendar selected date collection item");

        SetProperty(calendar, "SelectedDate", new DateTime(2026, 6, 21));
        AssertDate(GetProperty(calendar, "SelectedDate"), 2026, 6, 21, "compiled Calendar updated selected date");
        AssertCollectionCount(selectedDates, expected: 1, "compiled Calendar updated selected dates");
        AssertDate(GetCollectionItem(selectedDates, 0), 2026, 6, 21, "compiled Calendar updated selected date collection item");

        object datePicker = GetField(window, "DatePickerSmoke");
        AssertType(datePicker, "System.Windows.Controls.DatePicker", "compiled DatePicker");
        AssertEqual(160.0, GetProperty(datePicker, "Width"), "compiled DatePicker width");
        AssertEqual("Monday", GetProperty(datePicker, "FirstDayOfWeek").ToString(), "compiled DatePicker first day of week");
        AssertEqual(false, GetProperty(datePicker, "IsTodayHighlighted"), "compiled DatePicker today highlight");
        AssertEqual("Short", GetProperty(datePicker, "SelectedDateFormat").ToString(), "compiled DatePicker selected date format");
        AssertEqual(false, GetProperty(datePicker, "IsDropDownOpen"), "compiled DatePicker initial drop-down state");
        AssertDate(GetProperty(datePicker, "DisplayDateStart"), 2026, 1, 1, "compiled DatePicker display start");
        AssertDate(GetProperty(datePicker, "DisplayDateEnd"), 2026, 12, 31, "compiled DatePicker display end");
        AssertDate(GetProperty(datePicker, "SelectedDate"), 2026, 6, 18, "compiled DatePicker selected date");

        SetProperty(datePicker, "SelectedDate", new DateTime(2026, 7, 4));
        AssertDate(GetProperty(datePicker, "SelectedDate"), 2026, 7, 4, "compiled DatePicker updated selected date");
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

    private static void ValidateToggleChoiceControls(object window)
    {
        object panel = GetField(window, "ToggleChoicePanel");
        AssertType(panel, "System.Windows.Controls.StackPanel", "compiled toggle/radio panel");
        AssertCollectionCount(GetProperty(panel, "Children"), expected: 3, "compiled toggle/radio panel children");

        object checkBox = GetField(window, "ToggleChoiceCheckBox");
        AssertType(checkBox, "System.Windows.Controls.CheckBox", "compiled ToggleButton CheckBox");
        AssertEqual("toggle choice", GetProperty(checkBox, "Content"), "compiled ToggleButton CheckBox content");
        AssertEqual(false, GetProperty(checkBox, "IsChecked"), "compiled ToggleButton CheckBox initial checked state");
        AssertEqual(0, GetProperty(window, "ToggleChoiceCheckedCount"), "compiled ToggleButton initial Checked count");
        AssertEqual(0, GetProperty(window, "ToggleChoiceUncheckedCount"), "compiled ToggleButton initial Unchecked count");

        Invoke(checkBox, "OnClick");
        AssertEqual(true, GetProperty(checkBox, "IsChecked"), "compiled ToggleButton CheckBox checked state");
        AssertEqual(1, GetProperty(window, "ToggleChoiceCheckedCount"), "compiled ToggleButton Checked count");
        AssertEqual("ToggleChoiceCheckBox", GetProperty(window, "LastToggleChoiceCheckedSenderName"), "compiled ToggleButton Checked sender");
        AssertEqual("Checked", GetProperty(window, "LastToggleChoiceCheckedRoutedEventName"), "compiled ToggleButton Checked routed event");

        Invoke(checkBox, "OnClick");
        AssertEqual(false, GetProperty(checkBox, "IsChecked"), "compiled ToggleButton CheckBox unchecked state");
        AssertEqual(1, GetProperty(window, "ToggleChoiceUncheckedCount"), "compiled ToggleButton Unchecked count");
        AssertEqual("ToggleChoiceCheckBox", GetProperty(window, "LastToggleChoiceUncheckedSenderName"), "compiled ToggleButton Unchecked sender");
        AssertEqual("Unchecked", GetProperty(window, "LastToggleChoiceUncheckedRoutedEventName"), "compiled ToggleButton Unchecked routed event");

        object alpha = GetField(window, "RadioChoiceAlpha");
        object beta = GetField(window, "RadioChoiceBeta");
        AssertType(alpha, "System.Windows.Controls.RadioButton", "compiled alpha RadioButton");
        AssertType(beta, "System.Windows.Controls.RadioButton", "compiled beta RadioButton");
        AssertEqual("choice alpha", GetProperty(alpha, "Content"), "compiled alpha RadioButton content");
        AssertEqual("choice beta", GetProperty(beta, "Content"), "compiled beta RadioButton content");
        AssertEqual("SmokeChoiceGroup", GetProperty(alpha, "GroupName"), "compiled alpha RadioButton group");
        AssertEqual("SmokeChoiceGroup", GetProperty(beta, "GroupName"), "compiled beta RadioButton group");
        AssertEqual(false, GetProperty(alpha, "IsChecked"), "compiled alpha RadioButton initial checked state");
        AssertEqual(false, GetProperty(beta, "IsChecked"), "compiled beta RadioButton initial checked state");
        AssertEqual(0, GetProperty(window, "ChoiceRadioCheckedCount"), "compiled RadioButton initial Checked count");
        AssertEqual(0, GetProperty(window, "ChoiceRadioUncheckedCount"), "compiled RadioButton initial Unchecked count");

        Invoke(alpha, "OnClick");
        AssertEqual(true, GetProperty(alpha, "IsChecked"), "compiled alpha RadioButton checked state");
        AssertEqual(false, GetProperty(beta, "IsChecked"), "compiled beta RadioButton unchecked after alpha click");
        AssertEqual(1, GetProperty(window, "ChoiceRadioCheckedCount"), "compiled RadioButton alpha Checked count");
        AssertEqual("RadioChoiceAlpha", GetProperty(window, "LastChoiceRadioCheckedSenderName"), "compiled RadioButton alpha Checked sender");
        AssertEqual("Checked", GetProperty(window, "LastChoiceRadioCheckedRoutedEventName"), "compiled RadioButton alpha Checked routed event");

        Invoke(beta, "OnClick");
        AssertEqual(false, GetProperty(alpha, "IsChecked"), "compiled alpha RadioButton unchecked by group manager");
        AssertEqual(true, GetProperty(beta, "IsChecked"), "compiled beta RadioButton checked state");
        AssertEqual(2, GetProperty(window, "ChoiceRadioCheckedCount"), "compiled RadioButton beta Checked count");
        AssertEqual(1, GetProperty(window, "ChoiceRadioUncheckedCount"), "compiled RadioButton alpha Unchecked count");
        AssertEqual("RadioChoiceBeta", GetProperty(window, "LastChoiceRadioCheckedSenderName"), "compiled RadioButton beta Checked sender");
        AssertEqual("RadioChoiceAlpha", GetProperty(window, "LastChoiceRadioUncheckedSenderName"), "compiled RadioButton alpha Unchecked sender");
        AssertEqual("Unchecked", GetProperty(window, "LastChoiceRadioUncheckedRoutedEventName"), "compiled RadioButton alpha Unchecked routed event");
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

    private static void ValidateInputBinding(object window)
    {
        object inputBindings = GetProperty(window, "InputBindings");
        AssertCollectionCount(inputBindings, expected: 1, "compiled Window input bindings");

        object keyBinding = GetCollectionItem(inputBindings, 0);
        AssertType(keyBinding, "System.Windows.Input.KeyBinding", "compiled KeyBinding");
        AssertEqual("F6", GetProperty(keyBinding, "Key").ToString(), "compiled KeyBinding key");
        AssertEqual("Control", GetProperty(keyBinding, "Modifiers").ToString(), "compiled KeyBinding modifiers");
        AssertEqual("input binding payload", GetProperty(keyBinding, "CommandParameter"), "compiled KeyBinding command parameter");

        object keyGesture = GetProperty(keyBinding, "Gesture");
        AssertType(keyGesture, "System.Windows.Input.KeyGesture", "compiled KeyGesture");
        AssertEqual("F6", GetProperty(keyGesture, "Key").ToString(), "compiled KeyGesture key");
        AssertEqual("Control", GetProperty(keyGesture, "Modifiers").ToString(), "compiled KeyGesture modifiers");

        object inputBox = GetField(window, "InputBox");
        object command = GetProperty(keyBinding, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled KeyBinding routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled KeyBinding routed command name");
        AssertEqual(1, GetProperty(window, "RoutedCommandExecutionCount"), "input binding routed command initial execution count");

        object canExecute = InvokeTwoArgumentCommand(command, "CanExecute", GetProperty(keyBinding, "CommandParameter"), inputBox);
        AssertEqual(true, canExecute, "compiled KeyBinding command CanExecute result");
        InvokeTwoArgumentCommand(command, "Execute", GetProperty(keyBinding, "CommandParameter"), inputBox);

        AssertEqual(2, GetProperty(window, "RoutedCommandExecutionCount"), "compiled KeyBinding command execution count");
        AssertEqual("input binding payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled KeyBinding command executed parameter");
    }

    private static void ValidateMenuItems(object window)
    {
        object menu = GetField(window, "SmokeMenu");
        AssertType(menu, "System.Windows.Controls.Menu", "compiled Menu");
        AssertCollectionCount(GetProperty(menu, "Items"), expected: 1, "compiled Menu items");

        object fileMenuItem = GetField(window, "FileMenuItem");
        AssertType(fileMenuItem, "System.Windows.Controls.MenuItem", "compiled parent MenuItem");
        AssertEqual("_File", GetProperty(fileMenuItem, "Header"), "compiled parent MenuItem header");
        object fileMenuItems = GetProperty(fileMenuItem, "Items");
        AssertCollectionCount(fileMenuItems, expected: 3, "compiled parent MenuItem children");

        object commandItem = GetField(window, "MenuCommandItem");
        AssertType(commandItem, "System.Windows.Controls.MenuItem", "compiled command MenuItem");
        AssertEqual("Run _Command", GetProperty(commandItem, "Header"), "compiled command MenuItem header");
        AssertSame(menu, GetProperty(commandItem, "CommandTarget"), "compiled command MenuItem target");
        AssertEqual("menu command payload", GetProperty(commandItem, "CommandParameter"), "compiled command MenuItem parameter");
        object command = GetProperty(commandItem, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled command MenuItem routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled command MenuItem routed command name");
        AssertSame(commandItem, GetCollectionItem(fileMenuItems, 0), "compiled command MenuItem collection position");

        object separator = GetCollectionItem(fileMenuItems, 1);
        AssertType(separator, "System.Windows.Controls.Separator", "compiled Menu separator");

        object clickItem = GetField(window, "MenuClickItem");
        AssertType(clickItem, "System.Windows.Controls.MenuItem", "compiled click MenuItem");
        AssertEqual("_Click", GetProperty(clickItem, "Header"), "compiled click MenuItem header");
        AssertSame(clickItem, GetCollectionItem(fileMenuItems, 2), "compiled click MenuItem collection position");
        AssertEqual(0, GetProperty(window, "MenuClickCount"), "compiled MenuItem initial click count");

        RaiseMenuItemClick(clickItem);

        AssertEqual(1, GetProperty(window, "MenuClickCount"), "compiled MenuItem Click handler count");
        AssertEqual("MenuClickItem", GetProperty(window, "LastMenuClickSenderName"), "compiled MenuItem Click sender name");
        AssertEqual("Click", GetProperty(window, "LastMenuClickRoutedEventName"), "compiled MenuItem Click routed event name");
        AssertEqual(2, GetProperty(window, "RoutedCommandExecutionCount"), "compiled command MenuItem initial routed command count");

        object commandCanExecute = InvokeTwoArgumentCommand(
            command,
            "CanExecute",
            GetProperty(commandItem, "CommandParameter"),
            GetProperty(commandItem, "CommandTarget"));
        AssertEqual(true, commandCanExecute, "compiled command MenuItem CanExecute result");
        InvokeTwoArgumentCommand(
            command,
            "Execute",
            GetProperty(commandItem, "CommandParameter"),
            GetProperty(commandItem, "CommandTarget"));

        AssertEqual(3, GetProperty(window, "RoutedCommandExecutionCount"), "compiled command MenuItem routed command count");
        AssertEqual("menu command payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled command MenuItem routed command parameter");
    }

    private static void ValidateContextMenuAndToolTip(object window)
    {
        object contextButton = GetField(window, "ContextMenuButton");
        AssertType(contextButton, "System.Windows.Controls.Button", "compiled ContextMenu owner Button");
        AssertEqual("context menu target", GetProperty(contextButton, "Content"), "compiled ContextMenu owner Button content");

        object contextMenu = GetProperty(contextButton, "ContextMenu");
        AssertType(contextMenu, "System.Windows.Controls.ContextMenu", "compiled ContextMenu");
        AssertEqual("ContextButtonMenu", GetProperty(contextMenu, "Name"), "compiled ContextMenu name");
        object contextMenuItems = GetProperty(contextMenu, "Items");
        AssertCollectionCount(contextMenuItems, expected: 3, "compiled ContextMenu items");

        object commandItem = GetCollectionItem(contextMenuItems, 0);
        AssertType(commandItem, "System.Windows.Controls.MenuItem", "compiled ContextMenu command item");
        AssertEqual("ContextCommandItem", GetProperty(commandItem, "Name"), "compiled ContextMenu command item name");
        AssertEqual("Run Context _Command", GetProperty(commandItem, "Header"), "compiled ContextMenu command item header");
        AssertEqual("context menu command payload", GetProperty(commandItem, "CommandParameter"), "compiled ContextMenu command item parameter");
        object command = GetProperty(commandItem, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled ContextMenu routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled ContextMenu routed command name");

        object separator = GetCollectionItem(contextMenuItems, 1);
        AssertType(separator, "System.Windows.Controls.Separator", "compiled ContextMenu separator");

        object clickItem = GetCollectionItem(contextMenuItems, 2);
        AssertType(clickItem, "System.Windows.Controls.MenuItem", "compiled ContextMenu click item");
        AssertEqual("ContextClickItem", GetProperty(clickItem, "Name"), "compiled ContextMenu click item name");
        AssertEqual("Context _Click", GetProperty(clickItem, "Header"), "compiled ContextMenu click item header");
        AssertEqual(0, GetProperty(window, "ContextMenuClickCount"), "compiled ContextMenu initial click count");

        RaiseMenuItemClick(clickItem);

        AssertEqual(1, GetProperty(window, "ContextMenuClickCount"), "compiled ContextMenu Click handler count");
        AssertEqual("ContextClickItem", GetProperty(window, "LastContextMenuClickSenderName"), "compiled ContextMenu Click sender name");
        AssertEqual("Click", GetProperty(window, "LastContextMenuClickRoutedEventName"), "compiled ContextMenu Click routed event name");
        AssertEqual(3, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ContextMenu initial routed command count");

        object commandCanExecute = InvokeTwoArgumentCommand(
            command,
            "CanExecute",
            GetProperty(commandItem, "CommandParameter"),
            contextButton);
        AssertEqual(true, commandCanExecute, "compiled ContextMenu CanExecute result");
        InvokeTwoArgumentCommand(
            command,
            "Execute",
            GetProperty(commandItem, "CommandParameter"),
            contextButton);

        AssertEqual(4, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ContextMenu routed command count");
        AssertEqual("context menu command payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled ContextMenu routed command parameter");

        object toolTip = GetProperty(contextButton, "ToolTip");
        AssertType(toolTip, "System.Windows.Controls.ToolTip", "compiled ToolTip");
        AssertEqual("ContextButtonToolTip", GetProperty(toolTip, "Name"), "compiled ToolTip name");
        AssertEqual("Right", GetProperty(toolTip, "Placement").ToString(), "compiled ToolTip placement");
        object toolTipContent = GetProperty(toolTip, "Content");
        AssertType(toolTipContent, "System.Windows.Controls.TextBlock", "compiled ToolTip content");
        AssertEqual("ContextButtonToolTipText", GetProperty(toolTipContent, "Name"), "compiled ToolTip content name");
        AssertEqual("compiled tooltip text", GetProperty(toolTipContent, "Tag"), "compiled ToolTip content tag");
        AssertEqual("compiled ToolTip content", GetProperty(toolTipContent, "Text"), "compiled ToolTip content text");
    }

    private static void ValidateToolBarAndStatusBar(object window)
    {
        object toolBarTray = GetField(window, "SmokeToolBarTray");
        AssertType(toolBarTray, "System.Windows.Controls.ToolBarTray", "compiled ToolBarTray");
        object toolBars = GetProperty(toolBarTray, "ToolBars");
        AssertCollectionCount(toolBars, expected: 1, "compiled ToolBarTray toolbars");

        object toolBar = GetField(window, "SmokeToolBar");
        AssertType(toolBar, "System.Windows.Controls.ToolBar", "compiled ToolBar");
        AssertSame(toolBar, GetCollectionItem(toolBars, 0), "compiled ToolBarTray child toolbar");
        AssertEqual("Smoke tools", GetProperty(toolBar, "Header"), "compiled ToolBar header");
        object toolBarItems = GetProperty(toolBar, "Items");
        AssertCollectionCount(toolBarItems, expected: 3, "compiled ToolBar items");

        object commandButton = GetField(window, "ToolBarCommandButton");
        AssertType(commandButton, "System.Windows.Controls.Button", "compiled ToolBar command Button");
        AssertSame(commandButton, GetCollectionItem(toolBarItems, 0), "compiled ToolBar command item");
        AssertEqual("Run toolbar", GetProperty(commandButton, "Content"), "compiled ToolBar command Button content");
        AssertEqual("toolbar command payload", GetProperty(commandButton, "CommandParameter"), "compiled ToolBar command parameter");
        object command = GetProperty(commandButton, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled ToolBar routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled ToolBar routed command name");
        AssertEqual(4, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ToolBar initial routed command count");

        object commandCanExecute = InvokeTwoArgumentCommand(
            command,
            "CanExecute",
            GetProperty(commandButton, "CommandParameter"),
            toolBar);
        AssertEqual(true, commandCanExecute, "compiled ToolBar CanExecute result");
        InvokeTwoArgumentCommand(
            command,
            "Execute",
            GetProperty(commandButton, "CommandParameter"),
            toolBar);

        AssertEqual(5, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ToolBar routed command count");
        AssertEqual("toolbar command payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled ToolBar routed command parameter");

        object toolBarSeparator = GetField(window, "ToolBarSeparator");
        AssertType(toolBarSeparator, "System.Windows.Controls.Separator", "compiled ToolBar separator");
        AssertSame(toolBarSeparator, GetCollectionItem(toolBarItems, 1), "compiled ToolBar separator item");

        object toolBarToggle = GetField(window, "ToolBarToggle");
        AssertType(toolBarToggle, "System.Windows.Controls.Primitives.ToggleButton", "compiled ToolBar ToggleButton");
        AssertSame(toolBarToggle, GetCollectionItem(toolBarItems, 2), "compiled ToolBar toggle item");
        AssertEqual("Toggle toolbar", GetProperty(toolBarToggle, "Content"), "compiled ToolBar ToggleButton content");
        AssertEqual(true, GetProperty(toolBarToggle, "IsChecked"), "compiled ToolBar ToggleButton checked state");

        object statusBar = GetField(window, "SmokeStatusBar");
        AssertType(statusBar, "System.Windows.Controls.Primitives.StatusBar", "compiled StatusBar");
        object statusItems = GetProperty(statusBar, "Items");
        AssertCollectionCount(statusItems, expected: 3, "compiled StatusBar items");

        object readyItem = GetField(window, "StatusReadyItem");
        AssertType(readyItem, "System.Windows.Controls.Primitives.StatusBarItem", "compiled StatusBarItem");
        AssertSame(readyItem, GetCollectionItem(statusItems, 0), "compiled StatusBar ready item");
        AssertEqual("Ready", GetProperty(readyItem, "Content"), "compiled StatusBarItem content");

        object statusSeparator = GetCollectionItem(statusItems, 1);
        AssertType(statusSeparator, "System.Windows.Controls.Separator", "compiled StatusBar separator");

        object statusText = GetField(window, "StatusTextBlock");
        AssertType(statusText, "System.Windows.Controls.TextBlock", "compiled StatusBar TextBlock");
        AssertSame(statusText, GetCollectionItem(statusItems, 2), "compiled StatusBar TextBlock item");
        AssertEqual("status text", GetProperty(statusText, "Tag"), "compiled StatusBar TextBlock tag");
        AssertEqual("detail from implicit template", GetProperty(statusText, "Text"), "compiled StatusBar TextBlock binding");
    }

    private static void ValidateRangeControls(object window)
    {
        object dataContext = GetProperty(window, "DataContext");

        object slider = GetField(window, "RangeValueSlider");
        AssertType(slider, "System.Windows.Controls.Slider", "compiled Slider");
        AssertEqual(0.0, GetProperty(slider, "Minimum"), "compiled Slider minimum");
        AssertEqual(100.0, GetProperty(slider, "Maximum"), "compiled Slider maximum");
        AssertEqual(25.0, GetProperty(slider, "TickFrequency"), "compiled Slider tick frequency");
        AssertEqual(false, GetProperty(slider, "IsSnapToTickEnabled"), "compiled Slider snap-to-tick state");
        AssertEqual(42.0, GetProperty(slider, "Value"), "compiled Slider initial value");
        AssertBindingPath(slider, "ValueProperty", "RangeValue", "compiled Slider Value binding path");

        object progress = GetField(window, "RangeValueProgress");
        AssertType(progress, "System.Windows.Controls.ProgressBar", "compiled ProgressBar");
        AssertEqual(0.0, GetProperty(progress, "Minimum"), "compiled ProgressBar minimum");
        AssertEqual(100.0, GetProperty(progress, "Maximum"), "compiled ProgressBar maximum");
        AssertEqual(12.0, GetProperty(progress, "Height"), "compiled ProgressBar height");
        AssertEqual(42.0, GetProperty(progress, "Value"), "compiled ProgressBar initial value");
        AssertBindingPath(progress, "ValueProperty", "RangeValue", "compiled ProgressBar Value binding path");

        SetProperty(slider, "Value", 64.0);

        AssertEqual(64.0, GetProperty(dataContext, "RangeValue"), "compiled Slider two-way value source update");
        AssertEqual(64.0, GetProperty(progress, "Value"), "compiled ProgressBar value after source update");
    }

    private static void RaiseMenuItemClick(object menuItem)
    {
        FieldInfo clickEventField = menuItem.GetType().GetField(
            "ClickEvent",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(menuItem.GetType().FullName, "ClickEvent");
        object clickEvent = clickEventField.GetValue(null)
            ?? throw new InvalidOperationException("Expected MenuItem.ClickEvent to be initialized.");
        Type routedEventArgsType = clickEvent.GetType().Assembly.GetType(
            "System.Windows.RoutedEventArgs",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.RoutedEventArgs");
        object routedEventArgs = Activator.CreateInstance(routedEventArgsType, clickEvent, menuItem)
            ?? throw new InvalidOperationException("Failed to create MenuItem Click RoutedEventArgs.");

        Invoke(menuItem, "RaiseEvent", routedEventArgs);
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
        ValidateTemplateVisualStateManager(templateBorder);

        SetDictionaryValue(resources, "AccentBrush", replacementAccentBrush);
        AssertSame(replacementAccentBrush, GetProperty(templateBorder, "Background"), "compiled ControlTemplate dynamic resource update");

        SetProperty(templatedButton, "IsEnabled", false);
        AssertEqual(false, GetProperty(templatedButton, "IsEnabled"), "compiled ControlTemplate trigger source state");
        AssertEqual(0.42, GetProperty(templateBorder, "Opacity"), "compiled ControlTemplate trigger disabled opacity");
    }

    private static void ValidateTemplateVisualStateManager(object templateBorder)
    {
        Type visualStateManagerType = templateBorder.GetType().Assembly.GetType(
            "System.Windows.VisualStateManager",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.VisualStateManager");

        object groups = InvokeStatic(visualStateManagerType, "GetVisualStateGroups", templateBorder);
        AssertCollectionCount(groups, expected: 1, "compiled VisualStateManager group collection");

        object commonStates = GetCollectionItem(groups, 0);
        AssertType(commonStates, "System.Windows.VisualStateGroup", "compiled VisualStateGroup");
        AssertEqual("CommonStates", GetProperty(commonStates, "Name"), "compiled VisualStateGroup name");

        object states = GetProperty(commonStates, "States");
        AssertCollectionCount(states, expected: 2, "compiled VisualState entries");
        object normalState = GetCollectionItem(states, 0);
        object pressedState = GetCollectionItem(states, 1);
        AssertType(normalState, "System.Windows.VisualState", "compiled Normal VisualState");
        AssertType(pressedState, "System.Windows.VisualState", "compiled Pressed VisualState");
        AssertEqual("Normal", GetProperty(normalState, "Name"), "compiled Normal VisualState name");
        AssertEqual("Pressed", GetProperty(pressedState, "Name"), "compiled Pressed VisualState name");

        object pressedStoryboard = GetProperty(pressedState, "Storyboard");
        AssertType(pressedStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled Pressed VisualState storyboard");
        object pressedAnimations = GetProperty(pressedStoryboard, "Children");
        AssertCollectionCount(pressedAnimations, expected: 1, "compiled Pressed VisualState storyboard animations");
        object pressedAnimation = GetCollectionItem(pressedAnimations, 0);
        AssertType(pressedAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled Pressed VisualState animation");
        AssertEqual(0.73, GetProperty(pressedAnimation, "To"), "compiled Pressed VisualState animation target value");
        AssertEqual("00:00:00", GetProperty(pressedAnimation, "Duration").ToString(), "compiled Pressed VisualState animation duration");
    }

    private static void ValidatePostShowTemplateVisualStateManager(object window, Action flushRender)
    {
        object templatedButton = GetField(window, "TemplatedButton");
        object template = GetProperty(templatedButton, "Template");
        object templateBorder = Invoke(template, "FindName", "TemplateBorder", templatedButton);
        Type visualStateManagerType = templateBorder.GetType().Assembly.GetType(
            "System.Windows.VisualStateManager",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.VisualStateManager");

        AssertEqual(true, InvokeStatic(visualStateManagerType, "GoToElementState", templateBorder, "Pressed", false), "compiled VisualStateManager Pressed transition");
        flushRender();

        AssertEqual(0.73, GetProperty(templateBorder, "Opacity"), "compiled VisualStateManager Pressed opacity");
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
        object itemContainerStyle = GetProperty(itemsList, "ItemContainerStyle");
        AssertType(itemContainerStyle, "System.Windows.Style", "compiled ListBox item container style");
        AssertEqual("System.Windows.Controls.ListBoxItem", GetProperty(itemContainerStyle, "TargetType").ToString(), "compiled ListBox item container style target");
        object itemContainerSetters = GetProperty(itemContainerStyle, "Setters");
        AssertCollectionCount(itemContainerSetters, expected: 1, "compiled ItemContainerStyle setters");
        object itemContainerSetter = GetCollectionItem(itemContainerSetters, 0);
        AssertType(itemContainerSetter, "System.Windows.Setter", "compiled ItemContainerStyle setter");
        AssertEqual("Tag", GetProperty(GetProperty(itemContainerSetter, "Property"), "Name"), "compiled ItemContainerStyle setter property");
        AssertEqual("container trigger inactive", GetProperty(itemContainerSetter, "Value"), "compiled ItemContainerStyle default setter value");
        object itemContainerStyleTriggers = GetProperty(itemContainerStyle, "Triggers");
        AssertCollectionCount(itemContainerStyleTriggers, expected: 1, "compiled ItemContainerStyle triggers");
        object itemContainerStyleTrigger = GetCollectionItem(itemContainerStyleTriggers, 0);
        AssertType(itemContainerStyleTrigger, "System.Windows.DataTrigger", "compiled ItemContainerStyle DataTrigger");
        AssertBindingObjectPath(GetProperty(itemContainerStyleTrigger, "Binding"), "Name", "compiled ItemContainerStyle DataTrigger binding path");
        AssertEqual("item beta", GetProperty(itemContainerStyleTrigger, "Value"), "compiled ItemContainerStyle DataTrigger value");
        object itemContainerStyleTriggerSetters = GetProperty(itemContainerStyleTrigger, "Setters");
        AssertCollectionCount(itemContainerStyleTriggerSetters, expected: 1, "compiled ItemContainerStyle DataTrigger setters");
        object itemContainerStyleTriggerSetter = GetCollectionItem(itemContainerStyleTriggerSetters, 0);
        AssertType(itemContainerStyleTriggerSetter, "System.Windows.Setter", "compiled ItemContainerStyle DataTrigger setter");
        AssertEqual("Tag", GetProperty(GetProperty(itemContainerStyleTriggerSetter, "Property"), "Name"), "compiled ItemContainerStyle DataTrigger setter property");
        AssertEqual("container trigger active", GetProperty(itemContainerStyleTriggerSetter, "Value"), "compiled ItemContainerStyle DataTrigger setter value");

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

        object alphaTemplate = Invoke(window, "TryFindResource", "AlphaItemTemplate");
        AssertType(alphaTemplate, "System.Windows.DataTemplate", "compiled DataTemplateSelector alpha template resource");
        object alphaTemplateRoot = Invoke(alphaTemplate, "LoadContent");
        AssertType(alphaTemplateRoot, "System.Windows.Controls.TextBlock", "compiled DataTemplateSelector alpha template root");
        AssertEqual("SelectorTemplateTextBlock", GetProperty(alphaTemplateRoot, "Name"), "compiled DataTemplateSelector alpha template named root");
        AssertEqual("selector alpha template", GetProperty(alphaTemplateRoot, "Tag"), "compiled DataTemplateSelector alpha template tag");
        AssertBindingPath(alphaTemplateRoot, "TextProperty", "Name", "compiled DataTemplateSelector alpha binding path");

        object defaultTemplate = Invoke(window, "TryFindResource", "DefaultItemTemplate");
        AssertType(defaultTemplate, "System.Windows.DataTemplate", "compiled DataTemplateSelector default template resource");
        object defaultTemplateRoot = Invoke(defaultTemplate, "LoadContent");
        AssertType(defaultTemplateRoot, "System.Windows.Controls.TextBlock", "compiled DataTemplateSelector default template root");
        AssertEqual("SelectorTemplateTextBlock", GetProperty(defaultTemplateRoot, "Name"), "compiled DataTemplateSelector default template named root");
        AssertEqual("selector default template", GetProperty(defaultTemplateRoot, "Tag"), "compiled DataTemplateSelector default template tag");
        AssertBindingPath(defaultTemplateRoot, "TextProperty", "Name", "compiled DataTemplateSelector default binding path");

        object selector = Invoke(window, "TryFindResource", "SmokeItemTemplateSelector");
        AssertType(selector, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItemTemplateSelector", "compiled DataTemplateSelector resource");
        AssertSame(alphaTemplate, GetProperty(selector, "AlphaTemplate"), "compiled DataTemplateSelector alpha template property");
        AssertSame(defaultTemplate, GetProperty(selector, "DefaultTemplate"), "compiled DataTemplateSelector default template property");

        object selectorItemsList = GetField(window, "SelectorItemsList");
        AssertType(selectorItemsList, "System.Windows.Controls.ListBox", "compiled selector ListBox");
        AssertSame(sourceItems, GetProperty(selectorItemsList, "ItemsSource"), "compiled DataTemplateSelector ListBox ItemsSource binding");
        AssertSame(selector, GetProperty(selectorItemsList, "ItemTemplateSelector"), "compiled ListBox ItemTemplateSelector binding");
        AssertCollectionCount(GetProperty(selectorItemsList, "Items"), expected: 2, "compiled DataTemplateSelector generated items");

        object alphaContainerStyle = Invoke(window, "TryFindResource", "AlphaItemContainerSelectorStyle");
        AssertType(alphaContainerStyle, "System.Windows.Style", "compiled ItemContainerStyleSelector alpha style resource");
        AssertEqual("System.Windows.Controls.ListBoxItem", GetProperty(alphaContainerStyle, "TargetType").ToString(), "compiled ItemContainerStyleSelector alpha style target");
        object defaultContainerStyle = Invoke(window, "TryFindResource", "DefaultItemContainerSelectorStyle");
        AssertType(defaultContainerStyle, "System.Windows.Style", "compiled ItemContainerStyleSelector default style resource");
        AssertEqual("System.Windows.Controls.ListBoxItem", GetProperty(defaultContainerStyle, "TargetType").ToString(), "compiled ItemContainerStyleSelector default style target");
        object containerStyleSelector = Invoke(window, "TryFindResource", "SmokeItemContainerStyleSelector");
        AssertType(containerStyleSelector, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItemContainerStyleSelector", "compiled ItemContainerStyleSelector resource");
        AssertSame(alphaContainerStyle, GetProperty(containerStyleSelector, "AlphaStyle"), "compiled ItemContainerStyleSelector alpha style property");
        AssertSame(defaultContainerStyle, GetProperty(containerStyleSelector, "DefaultStyle"), "compiled ItemContainerStyleSelector default style property");

        object styleSelectorItemsList = GetField(window, "StyleSelectorItemsList");
        AssertType(styleSelectorItemsList, "System.Windows.Controls.ListBox", "compiled style selector ListBox");
        AssertSame(sourceItems, GetProperty(styleSelectorItemsList, "ItemsSource"), "compiled ItemContainerStyleSelector ListBox ItemsSource binding");
        AssertSame(containerStyleSelector, GetProperty(styleSelectorItemsList, "ItemContainerStyleSelector"), "compiled ListBox ItemContainerStyleSelector binding");
        AssertCollectionCount(GetProperty(styleSelectorItemsList, "Items"), expected: 2, "compiled ItemContainerStyleSelector generated items");

        object displayMemberItemsList = GetField(window, "DisplayMemberItemsList");
        AssertType(displayMemberItemsList, "System.Windows.Controls.ListBox", "compiled DisplayMemberPath ListBox");
        AssertSame(sourceItems, GetProperty(displayMemberItemsList, "ItemsSource"), "compiled DisplayMemberPath ListBox ItemsSource binding");
        AssertEqual("Name", GetProperty(displayMemberItemsList, "DisplayMemberPath"), "compiled ListBox DisplayMemberPath");
        AssertEqual("Category", GetProperty(displayMemberItemsList, "SelectedValuePath"), "compiled ListBox SelectedValuePath");
        AssertBindingPath(displayMemberItemsList, "SelectedValueProperty", "SelectedCategory", "compiled ListBox SelectedValue binding path");
        AssertEqual("secondary group", GetProperty(dataContext, "SelectedCategory"), "compiled ListBox initial selected category source");
        AssertEqual("secondary group", GetProperty(displayMemberItemsList, "SelectedValue"), "compiled ListBox initial selected value");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(displayMemberItemsList, "SelectedItem"), "compiled ListBox selected item by value path");
        SetProperty(displayMemberItemsList, "SelectedValue", "primary group");
        AssertEqual("primary group", GetProperty(dataContext, "SelectedCategory"), "compiled ListBox two-way selected value source update");
        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(displayMemberItemsList, "SelectedItem"), "compiled ListBox selected item after selected value update");

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

        object filteredItemsViewSource = Invoke(window, "TryFindResource", "FilteredItemsView");
        AssertType(filteredItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled filtered CollectionViewSource resource");
        object filteredItemsList = GetField(window, "FilteredItemsList");
        AssertType(filteredItemsList, "System.Windows.Controls.ListBox", "compiled filtered ListBox");
        AssertSame(GetProperty(filteredItemsViewSource, "View"), GetProperty(filteredItemsList, "ItemsSource"), "compiled ListBox filtered CollectionViewSource binding");
        object filteredItems = GetProperty(filteredItemsList, "Items");
        AssertCollectionCount(filteredItems, expected: 1, "compiled filtered ListBox generated items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(filteredItems, 0), "Name"), "compiled CollectionViewSource filtered item");
        if (Convert.ToInt32(GetProperty(window, "FilteredItemsFilterCount")) <= 0)
        {
            throw new InvalidOperationException("Expected compiled CollectionViewSource Filter handler to run.");
        }

        object groupedItemsViewSource = Invoke(window, "TryFindResource", "GroupedItemsView");
        AssertType(groupedItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled grouped CollectionViewSource resource");
        object groupDescriptions = GetProperty(groupedItemsViewSource, "GroupDescriptions");
        AssertCollectionCount(groupDescriptions, expected: 1, "compiled CollectionViewSource group descriptions");
        object groupDescription = GetCollectionItem(groupDescriptions, 0);
        AssertType(groupDescription, "System.Windows.Data.PropertyGroupDescription", "compiled CollectionViewSource group description");
        AssertEqual("Category", GetProperty(groupDescription, "PropertyName"), "compiled CollectionViewSource group property");

        object groupedItemsList = GetField(window, "GroupedItemsList");
        AssertType(groupedItemsList, "System.Windows.Controls.ListBox", "compiled grouped ListBox");
        object groupedItemsView = GetProperty(groupedItemsViewSource, "View");
        AssertSame(groupedItemsView, GetProperty(groupedItemsList, "ItemsSource"), "compiled ListBox grouped CollectionViewSource binding");
        object groupStyles = GetProperty(groupedItemsList, "GroupStyle");
        AssertCollectionCount(groupStyles, expected: 1, "compiled ListBox GroupStyle entries");
        object groupStyle = GetCollectionItem(groupStyles, 0);
        AssertType(groupStyle, "System.Windows.Controls.GroupStyle", "compiled ListBox GroupStyle");
        object groupHeaderTemplate = GetProperty(groupStyle, "HeaderTemplate");
        AssertType(groupHeaderTemplate, "System.Windows.DataTemplate", "compiled GroupStyle HeaderTemplate");
        object groupHeaderTemplateRoot = Invoke(groupHeaderTemplate, "LoadContent");
        AssertType(groupHeaderTemplateRoot, "System.Windows.Controls.TextBlock", "compiled GroupStyle HeaderTemplate root");
        AssertEqual("GroupHeaderTextBlock", GetProperty(groupHeaderTemplateRoot, "Name"), "compiled GroupStyle HeaderTemplate named root");
        AssertEqual("group header template", GetProperty(groupHeaderTemplateRoot, "Tag"), "compiled GroupStyle HeaderTemplate root tag");
        AssertBindingPath(groupHeaderTemplateRoot, "TextProperty", "Name", "compiled GroupStyle HeaderTemplate binding path");
        object groups = GetProperty(groupedItemsView, "Groups");
        AssertCollectionCount(groups, expected: 2, "compiled CollectionViewSource initial groups");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 0), "primary group", expectedItemCount: 1, "initial primary");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 1), "secondary group", expectedItemCount: 1, "initial secondary");

        object thirdItem = Create(window.GetType().Assembly, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItem", "item gamma");
        AddToCollection(sourceItems, thirdItem);
        AssertCollectionCount(GetProperty(itemsList, "Items"), expected: 3, "compiled ListBox collection-change items");
        AssertCollectionCount(sortedItems, expected: 3, "compiled sorted ListBox collection-change items");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource collection-change first item");
        AssertCollectionCount(filteredItems, expected: 1, "compiled filtered CollectionViewSource collection-change items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(filteredItems, 0), "Name"), "compiled filtered CollectionViewSource collection-change item");
        groups = GetProperty(groupedItemsView, "Groups");
        AssertCollectionCount(groups, expected: 2, "compiled CollectionViewSource collection-change groups");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 0), "primary group", expectedItemCount: 2, "collection-change primary");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 1), "secondary group", expectedItemCount: 1, "collection-change secondary");
    }

    private static void ValidateCollectionViewGroup(
        object group,
        string expectedName,
        int expectedItemCount,
        string description)
    {
        AssertEqual(expectedName, GetProperty(group, "Name"), $"compiled CollectionViewSource {description} group name");
        AssertEqual(expectedItemCount, GetProperty(group, "ItemCount"), $"compiled CollectionViewSource {description} group item count");
        AssertCollectionCount(GetProperty(group, "Items"), expected: expectedItemCount, $"compiled CollectionViewSource {description} group items");
    }

    private static void ValidateComboBox(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");

        object comboBox = GetField(window, "ItemsComboBox");
        AssertType(comboBox, "System.Windows.Controls.ComboBox", "compiled ComboBox");
        AssertSame(sourceItems, GetProperty(comboBox, "ItemsSource"), "compiled ComboBox ItemsSource binding");
        AssertCollectionCount(GetProperty(comboBox, "Items"), expected: 3, "compiled ComboBox collection-change items");
        AssertEqual("Name", GetProperty(comboBox, "DisplayMemberPath"), "compiled ComboBox DisplayMemberPath");
        AssertEqual("Category", GetProperty(comboBox, "SelectedValuePath"), "compiled ComboBox SelectedValuePath");
        AssertBindingPath(comboBox, "SelectedValueProperty", "ComboSelectedCategory", "compiled ComboBox SelectedValue binding path");
        AssertEqual("secondary group", GetProperty(dataContext, "ComboSelectedCategory"), "compiled ComboBox initial selected category source");
        AssertEqual("secondary group", GetProperty(comboBox, "SelectedValue"), "compiled ComboBox initial selected value");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(comboBox, "SelectedItem"), "compiled ComboBox selected item by value path");
        AssertEqual(1, GetProperty(comboBox, "SelectedIndex"), "compiled ComboBox initial selected index");

        SetProperty(comboBox, "SelectedValue", "primary group");

        AssertEqual("primary group", GetProperty(dataContext, "ComboSelectedCategory"), "compiled ComboBox two-way selected value source update");
        AssertEqual("primary group", GetProperty(comboBox, "SelectedValue"), "compiled ComboBox updated selected value");
        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(comboBox, "SelectedItem"), "compiled ComboBox selected item after selected value update");
        AssertEqual(0, GetProperty(comboBox, "SelectedIndex"), "compiled ComboBox updated selected index");
    }

    private static void ValidateListViewGridView(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");

        object listView = GetField(window, "GridItemsListView");
        AssertType(listView, "System.Windows.Controls.ListView", "compiled GridView ListView");
        AssertSame(sourceItems, GetProperty(listView, "ItemsSource"), "compiled GridView ListView ItemsSource binding");
        AssertCollectionCount(GetProperty(listView, "Items"), expected: 3, "compiled GridView ListView collection-change items");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(listView, "SelectedItem"), "compiled GridView ListView selected item");
        AssertEqual(1, GetProperty(listView, "SelectedIndex"), "compiled GridView ListView selected index");

        object gridView = GetProperty(listView, "View");
        AssertType(gridView, "System.Windows.Controls.GridView", "compiled GridView view");
        AssertEqual(false, GetProperty(gridView, "AllowsColumnReorder"), "compiled GridView column reorder setting");
        object columns = GetProperty(gridView, "Columns");
        AssertCollectionCount(columns, expected: 2, "compiled GridView columns");

        object nameColumn = GetCollectionItem(columns, 0);
        AssertType(nameColumn, "System.Windows.Controls.GridViewColumn", "compiled GridView name column");
        AssertEqual("Name", GetProperty(nameColumn, "Header"), "compiled GridView name column header");
        AssertEqual(120.0, GetProperty(nameColumn, "Width"), "compiled GridView name column width");
        AssertBindingObjectPath(GetProperty(nameColumn, "DisplayMemberBinding"), "Name", "compiled GridView name DisplayMemberBinding path");

        object categoryColumn = GetCollectionItem(columns, 1);
        AssertType(categoryColumn, "System.Windows.Controls.GridViewColumn", "compiled GridView category column");
        AssertEqual("Category", GetProperty(categoryColumn, "Header"), "compiled GridView category column header");
        AssertEqual(140.0, GetProperty(categoryColumn, "Width"), "compiled GridView category column width");
        AssertBindingObjectPath(GetProperty(categoryColumn, "DisplayMemberBinding"), "Category", "compiled GridView category DisplayMemberBinding path");

        SetProperty(listView, "SelectedIndex", 0);

        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(listView, "SelectedItem"), "compiled GridView ListView selected item after index update");
        AssertEqual(0, GetProperty(listView, "SelectedIndex"), "compiled GridView ListView selected index after update");
    }

    private static void ValidateImplicitDataTemplate(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object detail = GetProperty(dataContext, "Detail");
        AssertType(detail, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDetail", "compiled implicit DataTemplate detail model");
        AssertEqual("detail from implicit template", GetProperty(detail, "Title"), "compiled implicit DataTemplate detail title");

        object implicitTemplateHost = GetField(window, "ImplicitTemplateHost");
        AssertType(implicitTemplateHost, "System.Windows.Controls.ContentControl", "compiled implicit DataTemplate host");
        AssertSame(detail, GetProperty(implicitTemplateHost, "Content"), "compiled implicit DataTemplate host content binding");
    }

    private static void ValidateContentTemplateSelector(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object detail = GetProperty(dataContext, "Detail");
        object selectedTemplate = Invoke(window, "TryFindResource", "SelectedDetailTemplate");
        AssertType(selectedTemplate, "System.Windows.DataTemplate", "compiled ContentTemplateSelector selected template resource");
        object selectedTemplateRoot = Invoke(selectedTemplate, "LoadContent");
        AssertType(selectedTemplateRoot, "System.Windows.Controls.TextBlock", "compiled ContentTemplateSelector selected template root");
        AssertEqual("SelectedDetailTextBlock", GetProperty(selectedTemplateRoot, "Name"), "compiled ContentTemplateSelector selected template named root");
        AssertEqual("content template selector selected", GetProperty(selectedTemplateRoot, "Tag"), "compiled ContentTemplateSelector selected template tag");
        AssertBindingPath(selectedTemplateRoot, "TextProperty", "Title", "compiled ContentTemplateSelector selected binding path");

        object fallbackTemplate = Invoke(window, "TryFindResource", "FallbackDetailTemplate");
        AssertType(fallbackTemplate, "System.Windows.DataTemplate", "compiled ContentTemplateSelector fallback template resource");
        object fallbackTemplateRoot = Invoke(fallbackTemplate, "LoadContent");
        AssertType(fallbackTemplateRoot, "System.Windows.Controls.TextBlock", "compiled ContentTemplateSelector fallback template root");
        AssertEqual("SelectedDetailTextBlock", GetProperty(fallbackTemplateRoot, "Name"), "compiled ContentTemplateSelector fallback template named root");
        AssertEqual("content template selector fallback", GetProperty(fallbackTemplateRoot, "Tag"), "compiled ContentTemplateSelector fallback template tag");
        AssertBindingPath(fallbackTemplateRoot, "TextProperty", "Title", "compiled ContentTemplateSelector fallback binding path");

        object selector = Invoke(window, "TryFindResource", "SmokeDetailTemplateSelector");
        AssertType(selector, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDetailTemplateSelector", "compiled ContentTemplateSelector resource");
        AssertSame(selectedTemplate, GetProperty(selector, "SelectedTemplate"), "compiled ContentTemplateSelector selected template property");
        AssertSame(fallbackTemplate, GetProperty(selector, "FallbackTemplate"), "compiled ContentTemplateSelector fallback template property");

        object selectorTemplateHost = GetField(window, "SelectorTemplateHost");
        AssertType(selectorTemplateHost, "System.Windows.Controls.ContentControl", "compiled ContentTemplateSelector host");
        AssertSame(detail, GetProperty(selectorTemplateHost, "Content"), "compiled ContentTemplateSelector host content binding");
        AssertSame(selector, GetProperty(selectorTemplateHost, "ContentTemplateSelector"), "compiled ContentControl ContentTemplateSelector binding");
    }

    private static void ValidateHierarchicalDataTemplate(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceNodes = GetProperty(dataContext, "Nodes");
        AssertCollectionCount(sourceNodes, expected: 1, "view-model hierarchical nodes");
        object rootNode = GetCollectionItem(sourceNodes, 0);
        AssertType(rootNode, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeNode", "compiled hierarchical root model");
        AssertEqual("root node", GetProperty(rootNode, "Name"), "compiled hierarchical root model name");
        object rootChildren = GetProperty(rootNode, "Children");
        AssertCollectionCount(rootChildren, expected: 2, "compiled hierarchical root child models");
        AssertEqual("child alpha", GetProperty(GetCollectionItem(rootChildren, 0), "Name"), "compiled hierarchical first child model name");

        object nodeTemplate = Invoke(window, "TryFindResource", "SmokeNodeTemplate");
        AssertType(nodeTemplate, "System.Windows.HierarchicalDataTemplate", "compiled HierarchicalDataTemplate resource");
        AssertBindingObjectPath(GetProperty(nodeTemplate, "ItemsSource"), "Children", "compiled HierarchicalDataTemplate child ItemsSource path");
        object nodeTemplateRoot = Invoke(nodeTemplate, "LoadContent");
        AssertType(nodeTemplateRoot, "System.Windows.Controls.TextBlock", "compiled HierarchicalDataTemplate root");
        AssertEqual("NodeTextBlock", GetProperty(nodeTemplateRoot, "Name"), "compiled HierarchicalDataTemplate named root");
        AssertEqual("hierarchical template", GetProperty(nodeTemplateRoot, "Tag"), "compiled HierarchicalDataTemplate root tag");
        AssertBindingPath(nodeTemplateRoot, "TextProperty", "Name", "compiled HierarchicalDataTemplate text binding path");

        object nodeTree = GetField(window, "NodeTree");
        AssertType(nodeTree, "System.Windows.Controls.TreeView", "compiled hierarchical TreeView");
        AssertSame(sourceNodes, GetProperty(nodeTree, "ItemsSource"), "compiled TreeView ItemsSource binding");
        AssertSame(nodeTemplate, GetProperty(nodeTree, "ItemTemplate"), "compiled TreeView item template");
        AssertCollectionCount(GetProperty(nodeTree, "Items"), expected: 1, "compiled TreeView generated root items");
    }

    private static void ValidateTabControl(object window)
    {
        object tabControl = GetField(window, "SmokeTabControl");
        AssertType(tabControl, "System.Windows.Controls.TabControl", "compiled TabControl");
        AssertEqual(1, GetProperty(tabControl, "SelectedIndex"), "compiled TabControl selected index");

        object items = GetProperty(tabControl, "Items");
        AssertCollectionCount(items, expected: 2, "compiled TabControl items");

        object alphaTab = GetCollectionItem(items, 0);
        AssertType(alphaTab, "System.Windows.Controls.TabItem", "compiled TabControl alpha tab");
        AssertEqual("alpha tab", GetProperty(alphaTab, "Header"), "compiled TabControl alpha header");
        object alphaContent = GetProperty(alphaTab, "Content");
        AssertType(alphaContent, "System.Windows.Controls.TextBlock", "compiled TabControl alpha content");
        AssertEqual("AlphaTabContent", GetProperty(alphaContent, "Name"), "compiled TabControl alpha content name");
        AssertEqual("alpha tab content", GetProperty(alphaContent, "Text"), "compiled TabControl alpha content text");
        AssertEqual("tab alpha content", GetProperty(alphaContent, "Tag"), "compiled TabControl alpha content tag");

        object betaTab = GetCollectionItem(items, 1);
        AssertType(betaTab, "System.Windows.Controls.TabItem", "compiled TabControl beta tab");
        AssertEqual("beta tab", GetProperty(betaTab, "Header"), "compiled TabControl beta header");
        object betaContent = GetProperty(betaTab, "Content");
        AssertType(betaContent, "System.Windows.Controls.TextBlock", "compiled TabControl beta content");
        AssertEqual("BetaTabContent", GetProperty(betaContent, "Name"), "compiled TabControl beta content name");
        AssertEqual("beta tab content", GetProperty(betaContent, "Text"), "compiled TabControl beta content text");
        AssertEqual("tab beta content", GetProperty(betaContent, "Tag"), "compiled TabControl beta content tag");

        AssertSame(betaTab, GetProperty(tabControl, "SelectedItem"), "compiled TabControl selected item");
        AssertSame(betaContent, GetProperty(tabControl, "SelectedContent"), "compiled TabControl selected content");
    }

    private static void ValidateSectionControls(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object detail = GetProperty(dataContext, "Detail");

        object expanderHeaderTemplate = Invoke(window, "TryFindResource", "ExpanderHeaderTemplate");
        AssertType(expanderHeaderTemplate, "System.Windows.DataTemplate", "compiled Expander HeaderTemplate resource");
        object expanderHeaderRoot = Invoke(expanderHeaderTemplate, "LoadContent");
        AssertType(expanderHeaderRoot, "System.Windows.Controls.TextBlock", "compiled Expander HeaderTemplate root");
        AssertEqual("ExpanderHeaderTextBlock", GetProperty(expanderHeaderRoot, "Name"), "compiled Expander HeaderTemplate named root");
        AssertEqual("expander header template", GetProperty(expanderHeaderRoot, "Tag"), "compiled Expander HeaderTemplate root tag");
        AssertBindingPath(expanderHeaderRoot, "TextProperty", "Title", "compiled Expander HeaderTemplate binding path");

        object groupBoxHeaderTemplate = Invoke(window, "TryFindResource", "GroupBoxHeaderTemplate");
        AssertType(groupBoxHeaderTemplate, "System.Windows.DataTemplate", "compiled GroupBox HeaderTemplate resource");
        object groupBoxHeaderRoot = Invoke(groupBoxHeaderTemplate, "LoadContent");
        AssertType(groupBoxHeaderRoot, "System.Windows.Controls.TextBlock", "compiled GroupBox HeaderTemplate root");
        AssertEqual("GroupBoxHeaderTextBlock", GetProperty(groupBoxHeaderRoot, "Name"), "compiled GroupBox HeaderTemplate named root");
        AssertEqual("group box header template", GetProperty(groupBoxHeaderRoot, "Tag"), "compiled GroupBox HeaderTemplate root tag");
        AssertBindingPath(groupBoxHeaderRoot, "TextProperty", "Title", "compiled GroupBox HeaderTemplate binding path");

        object expander = GetField(window, "SmokeExpander");
        AssertType(expander, "System.Windows.Controls.Expander", "compiled Expander");
        AssertSame(detail, GetProperty(expander, "Header"), "compiled Expander header binding");
        AssertSame(expanderHeaderTemplate, GetProperty(expander, "HeaderTemplate"), "compiled Expander HeaderTemplate binding");
        AssertEqual(true, GetProperty(expander, "IsExpanded"), "compiled Expander expanded state");
        object expanderContent = GetProperty(expander, "Content");
        AssertType(expanderContent, "System.Windows.Controls.TextBlock", "compiled Expander content");
        AssertEqual("ExpanderContentText", GetProperty(expanderContent, "Name"), "compiled Expander content name");
        AssertEqual("expander content", GetProperty(expanderContent, "Tag"), "compiled Expander content tag");
        AssertBindingPath(expanderContent, "TextProperty", "Greeting", "compiled Expander content binding path");

        object groupBox = GetField(window, "SmokeGroupBox");
        AssertType(groupBox, "System.Windows.Controls.GroupBox", "compiled GroupBox");
        AssertSame(detail, GetProperty(groupBox, "Header"), "compiled GroupBox header binding");
        AssertSame(groupBoxHeaderTemplate, GetProperty(groupBox, "HeaderTemplate"), "compiled GroupBox HeaderTemplate binding");
        object groupContent = GetProperty(groupBox, "Content");
        AssertType(groupContent, "System.Windows.Controls.TextBlock", "compiled GroupBox content");
        AssertEqual("GroupBoxContentText", GetProperty(groupContent, "Name"), "compiled GroupBox content name");
        AssertEqual("group box content", GetProperty(groupContent, "Tag"), "compiled GroupBox content tag");
        AssertBindingPath(groupContent, "TextProperty", "ButtonText", "compiled GroupBox content binding path");
    }

    private static void ValidateAdornerDecorator(object window)
    {
        object decorator = GetField(window, "SmokeAdornerDecorator");
        AssertType(decorator, "System.Windows.Documents.AdornerDecorator", "compiled AdornerDecorator");

        object adornedButton = GetField(window, "AdornedButton");
        AssertType(adornedButton, "System.Windows.Controls.Button", "compiled adorned Button");
        AssertSame(adornedButton, GetProperty(decorator, "Child"), "compiled AdornerDecorator child");
        AssertEqual("adorned button", GetProperty(adornedButton, "Content"), "compiled adorned Button content");
        AssertEqual("adorned button", GetProperty(adornedButton, "Tag"), "compiled adorned Button tag");
    }

    private static void ValidatePostShowAdornerLayer(Assembly presentationFramework, Assembly compilerHarness, object window)
    {
        object adornedButton = GetField(window, "AdornedButton");
        Type adornerLayerType = GetRequiredType(presentationFramework, "System.Windows.Documents.AdornerLayer");
        object adornerLayer = InvokeStatic(adornerLayerType, "GetAdornerLayer", adornedButton);
        AssertType(adornerLayer, "System.Windows.Documents.AdornerLayer", "compiled AdornerLayer");

        object adorner = Create(compilerHarness, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeAdorner", adornedButton);
        AssertType(adorner, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeAdorner", "compiled SmokeAdorner");
        AssertSame(adornedButton, GetProperty(adorner, "AdornedElement"), "compiled SmokeAdorner adorned element");
        AssertEqual(false, GetProperty(adorner, "IsHitTestVisible"), "compiled SmokeAdorner hit testing");

        Invoke(adornerLayer, "Add", adorner);
        object adorners = Invoke(adornerLayer, "GetAdorners", adornedButton);
        AssertCollectionCount(adorners, expected: 1, "compiled AdornerLayer adorners");
        AssertSame(adorner, GetCollectionItem(adorners, 0), "compiled AdornerLayer added adorner");

        Invoke(adornerLayer, "Remove", adorner);
    }

    private static void ValidateAccessKeyFocusScope(Assembly presentationCore, object window)
    {
        object focusScope = GetField(window, "AccessKeyFocusScope");
        AssertType(focusScope, "System.Windows.Controls.StackPanel", "compiled access-key focus scope");

        object accessLabel = GetField(window, "AccessTargetLabel");
        AssertType(accessLabel, "System.Windows.Controls.Label", "compiled access-key Label");
        AssertEqual("_Access target", GetProperty(accessLabel, "Content"), "compiled access-key Label content");

        object accessTarget = GetField(window, "AccessTargetBox");
        AssertType(accessTarget, "System.Windows.Controls.TextBox", "compiled access-key target TextBox");
        AssertEqual("access target", GetProperty(accessTarget, "Text"), "compiled access-key target text");

        object accessText = GetField(window, "StandaloneAccessText");
        AssertType(accessText, "System.Windows.Controls.AccessText", "compiled standalone AccessText");
        AssertEqual("_Standalone access text", GetProperty(accessText, "Text"), "compiled standalone AccessText text");

        Type focusManagerType = GetRequiredType(presentationCore, "System.Windows.Input.FocusManager");
        AssertEqual(true, InvokeStatic(focusManagerType, "GetIsFocusScope", focusScope), "compiled FocusManager focus scope");
    }

    private static void ValidatePostShowAccessKeyFocusScope(Assembly presentationCore, object window)
    {
        Invoke(window, "UpdateLayout");

        object focusScope = GetField(window, "AccessKeyFocusScope");
        object accessLabel = GetField(window, "AccessTargetLabel");
        object accessTarget = GetField(window, "AccessTargetBox");
        AssertSame(accessTarget, GetProperty(accessLabel, "Target"), "compiled access-key Label target");

        Type focusManagerType = GetRequiredType(presentationCore, "System.Windows.Input.FocusManager");
        AssertSame(accessTarget, InvokeStatic(focusManagerType, "GetFocusedElement", focusScope), "compiled FocusManager focused element");

        Type presentationSourceType = GetRequiredType(presentationCore, "System.Windows.PresentationSource");
        object source = InvokeStatic(presentationSourceType, "FromVisual", window);
        Type accessKeyManagerType = GetRequiredType(presentationCore, "System.Windows.Input.AccessKeyManager");

        AssertEqual(true, InvokeStatic(accessKeyManagerType, "IsKeyRegistered", source, "A"), "compiled Label access key registered");
        InvokeStatic(accessKeyManagerType, "ProcessKey", source, "A", false);

        Type keyboardType = GetRequiredType(presentationCore, "System.Windows.Input.Keyboard");
        AssertSame(accessTarget, GetStaticProperty(keyboardType, "FocusedElement"), "compiled Label access key focused target");
        InvokeStatic(keyboardType, "ClearFocus");
    }

    private static void ValidateNavigationFrame(object window)
    {
        object frame = GetField(window, "SourceNavigationFrame");
        AssertType(frame, "System.Windows.Controls.Frame", "compiled source Frame");
        AssertEqual("Hidden", GetProperty(frame, "NavigationUIVisibility").ToString(), "compiled Frame navigation UI visibility");
        AssertContains("SmokePage.xaml", GetProperty(frame, "Source")?.ToString() ?? string.Empty, "compiled Frame source");
    }

    private static void ValidatePostShowNavigationFrame(object window, Action flushDispatcherOperations)
    {
        object frame = GetField(window, "SourceNavigationFrame");
        Invoke(frame, "UpdateLayout");

        object page = GetProperty(frame, "Content");
        AssertType(page, "ProGPU.Wpf.RealXamlCompilerHarness.SmokePage", "compiled source Page content");
        AssertEqual("compiled source page", GetProperty(page, "Title"), "compiled source Page title");
        AssertEqual(0, GetProperty(page, "PageClickCount"), "compiled source Page initial click count");

        object pagePanel = Invoke(page, "FindName", "SourceNavigationPagePanel");
        AssertType(pagePanel, "System.Windows.Controls.StackPanel", "compiled Page content panel");
        AssertSame(pagePanel, GetProperty(page, "Content"), "compiled Page content");
        AssertCollectionCount(GetProperty(pagePanel, "Children"), expected: 2, "compiled Page content panel children");

        object pageText = Invoke(page, "FindName", "SourceNavigationPageText");
        AssertType(pageText, "System.Windows.Controls.TextBlock", "compiled Page content text");
        AssertSame(pageText, GetCollectionItem(GetProperty(pagePanel, "Children"), 0), "compiled Page content text child");
        AssertEqual("source page content", GetProperty(pageText, "Tag"), "compiled Page content text tag");
        AssertEqual("compiled source page content", GetProperty(pageText, "Text"), "compiled Page content text");

        object pageButton = Invoke(page, "FindName", "SourceNavigationPageButton");
        AssertType(pageButton, "System.Windows.Controls.Button", "compiled Page content button");
        AssertSame(pageButton, GetCollectionItem(GetProperty(pagePanel, "Children"), 1), "compiled Page content button child");
        AssertEqual("source page button", GetProperty(pageButton, "Tag"), "compiled Page content button tag");
        AssertEqual("compiled page button", GetProperty(pageButton, "Content"), "compiled Page content button content");

        Invoke(pageButton, "OnClick");
        AssertEqual(1, GetProperty(page, "PageClickCount"), "compiled source Page click handler count");
        AssertEqual("SourceNavigationPageButton", GetProperty(page, "LastPageClickSenderName"), "compiled source Page click sender");
        AssertEqual("Click", GetProperty(page, "LastPageClickRoutedEventName"), "compiled source Page click routed event");

        ValidateFrameJournalNavigation(frame, flushDispatcherOperations);
    }

    private static void ValidateFrameJournalNavigation(object frame, Action flushDispatcherOperations)
    {
        SetProperty(frame, "Source", new Uri("SmokeSecondPage.xaml", UriKind.Relative));
        flushDispatcherOperations();
        Invoke(frame, "UpdateLayout");

        object secondPage = GetProperty(frame, "Content");
        AssertType(secondPage, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage", "compiled second Page content");
        AssertEqual("compiled second page", GetProperty(secondPage, "Title"), "compiled second Page title");
        object secondPagePanel = Invoke(secondPage, "FindName", "SourceNavigationSecondPagePanel");
        AssertType(secondPagePanel, "System.Windows.Controls.StackPanel", "compiled second Page content panel");
        AssertSame(secondPagePanel, GetProperty(secondPage, "Content"), "compiled second Page content");
        AssertCollectionCount(GetProperty(secondPagePanel, "Children"), expected: 1, "compiled second Page content panel children");
        object secondPageText = Invoke(secondPage, "FindName", "SourceNavigationSecondPageText");
        AssertType(secondPageText, "System.Windows.Controls.TextBlock", "compiled second Page content text");
        AssertSame(secondPageText, GetCollectionItem(GetProperty(secondPagePanel, "Children"), 0), "compiled second Page content text child");
        AssertEqual("second page content", GetProperty(secondPageText, "Tag"), "compiled second Page content text tag");
        AssertEqual("compiled second page content", GetProperty(secondPageText, "Text"), "compiled second Page content text");

        AssertEqual(true, GetProperty(frame, "CanGoBack"), "compiled Frame journal can go back");
        AssertEqual(false, GetProperty(frame, "CanGoForward"), "compiled Frame journal cannot go forward before back");

        Invoke(frame, "GoBack");
        flushDispatcherOperations();
        Invoke(frame, "UpdateLayout");
        object firstPageAgain = GetProperty(frame, "Content");
        AssertType(firstPageAgain, "ProGPU.Wpf.RealXamlCompilerHarness.SmokePage", "compiled Frame journal back content");
        AssertEqual("compiled source page", GetProperty(firstPageAgain, "Title"), "compiled Frame journal back title");
        AssertEqual(false, GetProperty(frame, "CanGoBack"), "compiled Frame journal cannot go back after returning");
        AssertEqual(true, GetProperty(frame, "CanGoForward"), "compiled Frame journal can go forward");

        Invoke(frame, "GoForward");
        flushDispatcherOperations();
        Invoke(frame, "UpdateLayout");
        object secondPageAgain = GetProperty(frame, "Content");
        AssertType(secondPageAgain, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage", "compiled Frame journal forward content");
        AssertEqual("compiled second page", GetProperty(secondPageAgain, "Title"), "compiled Frame journal forward title");
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

        var recorder = new ActivationRecorder(presentationFramework, presentationCore, compilerHarness, application, activationServiceType);
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

    private static object? TryGetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static object? TryGetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static (double X, double Y) GetElementCenterInWindow(Assembly presentationCore, object element, object window)
    {
        double width = Convert.ToDouble(GetProperty(element, "ActualWidth"));
        double height = Convert.ToDouble(GetProperty(element, "ActualHeight"));
        object renderSize = GetProperty(element, "RenderSize");
        if (width <= 0)
        {
            width = Convert.ToDouble(GetProperty(renderSize, "Width"));
        }

        if (height <= 0)
        {
            height = Convert.ToDouble(GetProperty(renderSize, "Height"));
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"Expected '{element.GetType().FullName}' to have a non-empty arranged size.");
        }

        Type pointType = renderSize.GetType().Assembly.GetType("System.Windows.Point", throwOnError: true)
            ?? throw new TypeLoadException("Could not load 'System.Windows.Point'.");
        object center = Activator.CreateInstance(pointType, width / 2d, height / 2d)
            ?? throw new InvalidOperationException("Failed to create a WPF Point for portable mouse input.");
        object windowPoint = Invoke(element, "TranslatePoint", center, window);
        object transformToDevice = GetTransformToDevice(presentationCore, window);
        (double x, double y) = TransformPoint(transformToDevice, windowPoint);

        return (x, y);
    }

    private static object GetTransformToDevice(Assembly presentationCore, object visual)
    {
        Type presentationSourceType = GetRequiredType(presentationCore, "System.Windows.PresentationSource");
        object source = InvokeStatic(presentationSourceType, "FromVisual", visual);
        object compositionTarget = GetProperty(source, "CompositionTarget");
        return GetProperty(compositionTarget, "TransformToDevice");
    }

    private static (double X, double Y) TransformPoint(object matrix, object point)
    {
        double x = Convert.ToDouble(GetProperty(point, "X"));
        double y = Convert.ToDouble(GetProperty(point, "Y"));
        double m11 = Convert.ToDouble(GetProperty(matrix, "M11"));
        double m12 = Convert.ToDouble(GetProperty(matrix, "M12"));
        double m21 = Convert.ToDouble(GetProperty(matrix, "M21"));
        double m22 = Convert.ToDouble(GetProperty(matrix, "M22"));
        double offsetX = Convert.ToDouble(GetProperty(matrix, "OffsetX"));
        double offsetY = Convert.ToDouble(GetProperty(matrix, "OffsetY"));

        return (
            (x * m11) + (y * m21) + offsetX,
            (x * m12) + (y * m22) + offsetY);
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

    private static object GetMultiBindingExpression(object dependencyObject, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = dependencyObject.GetType().GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(dependencyObject.GetType().FullName, dependencyPropertyFieldName);
        Type bindingOperationsType = dependencyObject.GetType().Assembly.GetType(
            "System.Windows.Data.BindingOperations",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Data.BindingOperations");
        MethodInfo getMultiBindingExpression = bindingOperationsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "GetMultiBindingExpression", StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 2)
            ?? throw new MissingMethodException(bindingOperationsType.FullName, "GetMultiBindingExpression");

        object? bindingExpression = getMultiBindingExpression.Invoke(null, new[] { dependencyObject, dependencyProperty.GetValue(null) });
        if (bindingExpression == null)
        {
            throw new InvalidOperationException(
                $"Expected '{dependencyObject.GetType().FullName}.{dependencyPropertyFieldName}' to have a multi binding expression.");
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

    private static void AssertNotSame(object expectedDifferent, object actual, string description)
    {
        if (ReferenceEquals(expectedDifferent, actual))
        {
            throw new InvalidOperationException($"Expected {description} to reference different objects.");
        }
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static void AssertDate(object? actual, int expectedYear, int expectedMonth, int expectedDay, string description)
    {
        if (actual is not DateTime actualDate)
        {
            throw new InvalidOperationException($"Expected {description} to be a DateTime, got '{actual}'.");
        }

        if (actualDate.Year != expectedYear || actualDate.Month != expectedMonth || actualDate.Day != expectedDay)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedYear:D4}-{expectedMonth:D2}-{expectedDay:D2}', got '{actualDate:yyyy-MM-dd}'.");
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

    private sealed class ActivationRecorder : IDisposable
    {
        private readonly Assembly _presentationFramework;
        private readonly Assembly _presentationCore;
        private readonly Assembly _compilerHarness;
        private readonly object _application;
        private readonly Type _activationServiceType;
        private readonly IDisposable? _mediaContextRenderRegistration;
        private object? _activation;
        private bool _isDisposed;
        private bool _isFlushingWpfDispatcher;

        public ActivationRecorder(
            Assembly presentationFramework,
            Assembly presentationCore,
            Assembly compilerHarness,
            object application,
            Type activationServiceType)
        {
            _presentationFramework = presentationFramework;
            _presentationCore = presentationCore;
            _compilerHarness = compilerHarness;
            _application = application;
            _activationServiceType = activationServiceType;
            _mediaContextRenderRegistration = RegisterMediaContextRenderService();
        }

        public int ActivateCount { get; private set; }

        public int ShowCount { get; private set; }

        public int RunCount { get; private set; }

        public int RenderRequestCount { get; private set; }

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
            ValidateMainWindow(_presentationCore, window, _application);

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
            AssertSameActivation(activation);
            ShowCount++;
            var typedActivation = (RecordingActivation)activation;
            typedActivation.IsVisible = true;
            FlushDispatcherOperations(typedActivation.Window, "Loaded", "Render");
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
            ValidatePostShowLoadedEvent(typedActivation.Window);
            ValidatePostShowClickStoryboardEventTrigger(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowTemplateVisualStateManager(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowItemTemplateTriggerActivation(_presentationCore, typedActivation.Window);
            ValidatePostShowGroupStyleHeader(_presentationCore, typedActivation.Window);
            ValidatePostShowItemTemplateSelector(_presentationCore, typedActivation.Window);
            ValidatePostShowItemContainerStyleSelector(_presentationCore, typedActivation.Window);
            ValidatePostShowImplicitDataTemplate(_presentationCore, typedActivation.Window);
            ValidatePostShowContentTemplateSelector(_presentationCore, typedActivation.Window);
            ValidatePostShowHierarchicalDataTemplate(_presentationCore, typedActivation.Window);
            ValidatePostShowTabControl(_presentationCore, typedActivation.Window);
            ValidatePostShowSectionControls(_presentationCore, typedActivation.Window);
            ValidatePostShowAdornerLayer(_presentationFramework, _compilerHarness, typedActivation.Window);
            ValidatePostShowAccessKeyFocusScope(_presentationCore, typedActivation.Window);
            ValidatePostShowNavigationFrame(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowScrollingControls(typedActivation.Window);
            ValidatePortableInputBindingActivation(typedActivation.Window);
            ValidatePortableTextInputActivation(typedActivation.Window);
            ValidatePortableMouseClickActivation(typedActivation.Window);
            ValidatePortableMouseWheelActivation(typedActivation.Window);
        }

        public void Dispose(object activation)
        {
            AssertSameActivation(activation);
            DisposeCount++;
            var typedActivation = (RecordingActivation)activation;
            if (!typedActivation.IsDisposed)
            {
                typedActivation.DisposePresentationSource();
                typedActivation.IsDisposed = true;
            }
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
            AssertEqual(true, RenderRequestCount > 0, "portable MediaContext render request count");
            AssertEqual(1, CloseCount, "startup window close count");
            AssertEqual(1, DisposeCount, "startup window dispose count");

            if (_activation is not RecordingActivation activation)
            {
                throw new InvalidOperationException("Application.Run did not create a recording activation.");
            }

            AssertEqual(true, activation.IsClosed, "recorded activation close state");
            AssertEqual(true, activation.IsDisposed, "recorded activation dispose state");
            ValidatePostShowBindingFeatures(activation.Window);
            ValidateLoadedEventHandlerState(activation.Window);
            ValidatePostShowItemTemplateTriggerActivation(_presentationCore, activation.Window);
            ValidatePostShowGroupStyleHeader(_presentationCore, activation.Window);
            ValidatePostShowItemTemplateSelector(_presentationCore, activation.Window);
            ValidatePostShowItemContainerStyleSelector(_presentationCore, activation.Window);
            ValidatePostShowImplicitDataTemplate(_presentationCore, activation.Window);
            ValidatePostShowContentTemplateSelector(_presentationCore, activation.Window);
            ValidatePostShowHierarchicalDataTemplate(_presentationCore, activation.Window);
            ValidateTabControl(activation.Window);
            ValidateSectionControls(activation.Window);
            AssertEqual("input binding payload", GetProperty(activation.Window, "LastRoutedCommandParameter"), "portable input KeyBinding persisted command parameter");
            AssertEqual("portable x", GetProperty(GetField(activation.Window, "InputBox"), "Text"), "portable text input persisted TextBox text");
            AssertAtLeast(2, GetProperty(activation.Window, "XamlClickCount"), "portable mouse routed Click persisted count");
            AssertEqual("EventButton", GetProperty(activation.Window, "LastXamlClickSenderName"), "portable mouse routed Click persisted sender name");
            AssertEqual("Click", GetProperty(activation.Window, "LastXamlClickRoutedEventName"), "portable mouse routed Click persisted event name");
            AssertAtLeast(1, GetProperty(activation.Window, "XamlGotMouseCaptureCount"), "portable mouse GotMouseCapture persisted count");
            AssertAtLeast(1, GetProperty(activation.Window, "XamlLostMouseCaptureCount"), "portable mouse LostMouseCapture persisted count");
            AssertAtLeast(1, GetProperty(activation.Window, "XamlMouseWheelCount"), "portable mouse wheel persisted count");
            AssertEqual(120, GetProperty(activation.Window, "LastXamlMouseWheelDelta"), "portable mouse wheel persisted delta");
            AssertEqual("EventButton", GetProperty(activation.Window, "LastXamlMouseWheelSenderName"), "portable mouse wheel persisted sender name");
            AssertEqual("MouseWheel", GetProperty(activation.Window, "LastXamlMouseWheelRoutedEventName"), "portable mouse wheel persisted event name");
        }

        private void AssertSameActivation(object activation)
        {
            if (!ReferenceEquals(_activation, activation))
            {
                throw new InvalidOperationException("Portable activation callback received an unknown activation object.");
            }
        }

        private void FlushDispatcherOperations(object window, params string[] markerPriorityNames)
        {
            if (_isFlushingWpfDispatcher)
            {
                return;
            }

            _isFlushingWpfDispatcher = true;
            try
            {
                FlushDispatcherOperationsCore(window, markerPriorityNames);
            }
            finally
            {
                _isFlushingWpfDispatcher = false;
            }
        }

        private void FlushDispatcherOperationsCore(object window, params string[] markerPriorityNames)
        {
            MethodInfo flushMethod = _activationServiceType.GetMethod(
                "FlushDispatcherOperations",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(_activationServiceType.FullName, "FlushDispatcherOperations");
            Type dispatcherPriorityType = flushMethod.GetParameters()[1].ParameterType;

            foreach (string markerPriorityName in markerPriorityNames)
            {
                object markerPriority = Enum.Parse(dispatcherPriorityType, markerPriorityName);
                flushMethod.Invoke(null, new[] { window, markerPriority });
            }
        }

        private IDisposable? RegisterMediaContextRenderService()
        {
            Type serviceType = GetRequiredType(_presentationCore, PortableMediaContextRenderServiceTypeName);

            MethodInfo? register = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<TimeSpan>) },
                modifiers: null);
            if (register != null)
            {
                return (IDisposable?)register.Invoke(null, new object[] { (Action<TimeSpan>)RequestRenderFromMediaContext });
            }

            register = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action) },
                modifiers: null);
            if (register == null)
            {
                throw new MissingMethodException(serviceType.FullName, "Register");
            }

            return (IDisposable?)register.Invoke(null, new object[] { (Action)RequestRenderFromMediaContext });
        }

        private void RequestRenderFromMediaContext()
        {
            RequestRenderFromMediaContext(TimeSpan.Zero);
        }

        private void RequestRenderFromMediaContext(TimeSpan delay)
        {
            if (_isDisposed || _activation is not RecordingActivation)
            {
                return;
            }

            RenderRequestCount++;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _mediaContextRenderRegistration?.Dispose();
            _isDisposed = true;
        }

        private object CreatePortablePresentationSource(object window)
        {
            Type sourceType = GetRequiredType(_presentationCore, PortablePresentationSourceTypeName);
            object source = Activator.CreateInstance(
                sourceType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: Array.Empty<object>(),
                culture: null)
                ?? throw new InvalidOperationException($"Failed to create '{PortablePresentationSourceTypeName}'.");
            SetProperty(source, "RootVisual", window);
            return source;
        }

        private void ValidatePortableInputBindingActivation(object window)
        {
            object inputBox = GetField(window, "InputBox");
            Type keyboardType = GetRequiredType(_presentationCore, "System.Windows.Input.Keyboard");
            object focused = InvokeStatic(keyboardType, "Focus", inputBox);
            AssertSame(inputBox, focused, "portable Application.Run input KeyBinding focused target");

            int initialExecutionCount = Convert.ToInt32(GetProperty(window, "RoutedCommandExecutionCount"));
            object keyDown = CreatePortableInputEvent("KeyDown", "F6", scanCode: 0, modifiersName: "Control");
            Invoke(window, "HandlePortableInput", keyDown);

            AssertEqual(true, GetProperty(keyDown, "Handled"), "portable Application.Run input KeyBinding handled state");
            AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable Application.Run input KeyBinding command execution count");
            AssertEqual("input binding payload", GetProperty(window, "LastRoutedCommandParameter"), "portable Application.Run input KeyBinding command parameter");

            object keyUp = CreatePortableInputEvent("KeyUp", "F6", scanCode: 0, modifiersName: "None");
            Invoke(window, "HandlePortableInput", keyUp);
            AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable Application.Run input KeyBinding ignores key up");

            InvokeStatic(keyboardType, "ClearFocus");
            AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run input KeyBinding clear focus");
        }

        private void ValidatePortableTextInputActivation(object window)
        {
            object inputBox = GetField(window, "InputBox");
            Type keyboardType = GetRequiredType(_presentationCore, "System.Windows.Input.Keyboard");
            SetProperty(inputBox, "Text", "portable ");
            Invoke(inputBox, "Select", "portable ".Length, 0);
            object focused = InvokeStatic(keyboardType, "Focus", inputBox);
            AssertSame(inputBox, focused, "portable Application.Run text input focused target");

            object textInput = CreatePortableInputEvent("TextInput", key: null, scanCode: 0, character: 'x', modifiersName: "None");
            Invoke(window, "HandlePortableInput", textInput);

            AssertEqual(true, GetProperty(textInput, "Handled"), "portable Application.Run text input handled state");
            AssertEqual("portable x", GetProperty(inputBox, "Text"), "portable Application.Run text input TextBox text");
            AssertEqual("portable x".Length, GetProperty(inputBox, "SelectionStart"), "portable Application.Run text input caret index");
            AssertEqual(0, GetProperty(inputBox, "SelectionLength"), "portable Application.Run text input selection length");

            InvokeStatic(keyboardType, "ClearFocus");
            AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run text input clear focus");
        }

        private void ValidatePortableMouseClickActivation(object window)
        {
            object eventButton = GetField(window, "EventButton");
            Invoke(window, "UpdateLayout");
            Invoke(eventButton, "UpdateLayout");
            (double x, double y) = GetElementCenterInWindow(_presentationCore, eventButton, window);

            int initialClickCount = Convert.ToInt32(GetProperty(window, "XamlClickCount"));
            int initialGotCaptureCount = Convert.ToInt32(GetProperty(window, "XamlGotMouseCaptureCount"));
            int initialLostCaptureCount = Convert.ToInt32(GetProperty(window, "XamlLostMouseCaptureCount"));
            Type mouseType = GetRequiredType(_presentationCore, "System.Windows.Input.Mouse");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseMove", x: x, y: y));
            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseDown", x: x, y: y, buttonName: "Left"));
            object capturedAfterDown = TryGetStaticProperty(mouseType, "Captured")
                ?? throw new InvalidOperationException("Expected portable Application.Run mouse capture after mouse down.");
            AssertSame(eventButton, capturedAfterDown, "portable Application.Run mouse captured element after down");
            AssertEqual(true, GetProperty(eventButton, "IsMouseCaptured"), "portable Application.Run mouse ButtonBase IsMouseCaptured after down");
            AssertEqual(true, GetProperty(eventButton, "IsPressed"), "portable Application.Run mouse ButtonBase IsPressed after down");
            AssertEqual(initialGotCaptureCount + 1, GetProperty(window, "XamlGotMouseCaptureCount"), "portable Application.Run mouse GotMouseCapture count");
            AssertEqual("EventButton", GetProperty(window, "LastXamlGotMouseCaptureSenderName"), "portable Application.Run mouse GotMouseCapture sender name");
            AssertEqual("GotMouseCapture", GetProperty(window, "LastXamlGotMouseCaptureRoutedEventName"), "portable Application.Run mouse GotMouseCapture event name");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseUp", x: x, y: y, buttonName: "Left"));
            AssertEqual(null, TryGetStaticProperty(mouseType, "Captured"), "portable Application.Run mouse captured element after up");
            AssertEqual(false, GetProperty(eventButton, "IsMouseCaptured"), "portable Application.Run mouse ButtonBase IsMouseCaptured after up");
            AssertEqual(false, GetProperty(eventButton, "IsPressed"), "portable Application.Run mouse ButtonBase IsPressed after up");
            AssertEqual(initialLostCaptureCount + 1, GetProperty(window, "XamlLostMouseCaptureCount"), "portable Application.Run mouse LostMouseCapture count");
            AssertEqual("EventButton", GetProperty(window, "LastXamlLostMouseCaptureSenderName"), "portable Application.Run mouse LostMouseCapture sender name");
            AssertEqual("LostMouseCapture", GetProperty(window, "LastXamlLostMouseCaptureRoutedEventName"), "portable Application.Run mouse LostMouseCapture event name");

            AssertEqual(initialClickCount + 1, GetProperty(window, "XamlClickCount"), "portable Application.Run mouse routed Click count");
            AssertEqual("EventButton", GetProperty(window, "LastXamlClickSenderName"), "portable Application.Run mouse routed Click sender name");
            AssertEqual("Click", GetProperty(window, "LastXamlClickRoutedEventName"), "portable Application.Run mouse routed Click event name");
        }

        private void ValidatePortableMouseWheelActivation(object window)
        {
            object eventButton = GetField(window, "EventButton");
            Invoke(window, "UpdateLayout");
            Invoke(eventButton, "UpdateLayout");
            (double x, double y) = GetElementCenterInWindow(_presentationCore, eventButton, window);

            int initialWheelCount = Convert.ToInt32(GetProperty(window, "XamlMouseWheelCount"));
            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseWheel", x: x, y: y, deltaY: 1));

            AssertEqual(initialWheelCount + 1, GetProperty(window, "XamlMouseWheelCount"), "portable Application.Run mouse wheel routed event count");
            AssertEqual(120, GetProperty(window, "LastXamlMouseWheelDelta"), "portable Application.Run mouse wheel routed event delta");
            AssertEqual("EventButton", GetProperty(window, "LastXamlMouseWheelSenderName"), "portable Application.Run mouse wheel sender name");
            AssertEqual("MouseWheel", GetProperty(window, "LastXamlMouseWheelRoutedEventName"), "portable Application.Run mouse wheel routed event name");
        }

        private object CreatePortableInputEvent(
            string kindName,
            string? key = null,
            int scanCode = 0,
            string modifiersName = "None",
            char? character = null,
            double x = 0,
            double y = 0,
            double deltaX = 0,
            double deltaY = 0,
            string buttonName = "None")
        {
            Assembly presentationFramework = _activationServiceType.Assembly;
            Type argsType = GetRequiredType(presentationFramework, "System.Windows.PortableInputEventArgs");
            Type kindType = GetRequiredType(presentationFramework, "System.Windows.PortableInputEventKind");
            Type buttonType = GetRequiredType(presentationFramework, "System.Windows.PortableMouseButton");
            Type modifiersType = GetRequiredType(presentationFramework, "System.Windows.PortableInputModifiers");

            return Activator.CreateInstance(
                argsType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object?[]
                {
                    Enum.Parse(kindType, kindName),
                    key,
                    scanCode,
                    character,
                    x,
                    y,
                    deltaX,
                    deltaY,
                    Enum.Parse(buttonType, buttonName),
                    Enum.Parse(modifiersType, modifiersName)
                },
                culture: null)
                ?? throw new InvalidOperationException($"Failed to create '{argsType.FullName}'.");
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
        public RecordingActivation(object window, object presentationSource)
        {
            Window = window;
            PresentationSource = presentationSource;
        }

        public object Window { get; }

        public object PresentationSource { get; }

        public bool IsVisible { get; set; }

        public bool IsClosed { get; set; }

        public bool IsDisposed { get; set; }

        public string Title { get; set; } = string.Empty;

        public double Width { get; set; }

        public double Height { get; set; }

        public object? WindowState { get; set; }

        public void DisposePresentationSource()
        {
            if (PresentationSource is IDisposable disposable)
            {
                disposable.Dispose();
                return;
            }

            MethodInfo? dispose = PresentationSource.GetType().GetMethod(
                nameof(IDisposable.Dispose),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            dispose?.Invoke(PresentationSource, Array.Empty<object>());
        }
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
