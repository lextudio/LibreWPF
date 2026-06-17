using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;

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
            Console.WriteLine("Real WPF XAML runtime smoke succeeded.");
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
        object? activation = null;
        object? window = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(compilerHarness, AppTypeName);
            Invoke(application, "InitializeComponent");
            ValidateApplication(application);

            window = Create(compilerHarness, MainWindowTypeName);
            ValidateMainWindow(window, application);

            ShowPortableActivation(
                presentationFramework,
                window,
                out activationServiceType,
                out activation);
            ValidatePostShowBindingFeatures(window);
            ValidatePostShowLoadedEvent(window);
            ValidatePostShowClickStoryboardEventTrigger(
                window,
                () => FlushDispatcherOperations(activationServiceType, window, "Render"));
            ValidatePostShowTemplateVisualStateManager(
                window,
                () => FlushDispatcherOperations(activationServiceType, window, "Render"));
            ValidatePostShowItemTemplateTriggerActivation(presentationCore, window);
            ValidatePostShowGroupStyleHeader(presentationCore, window);
            ValidatePostShowItemTemplateSelector(presentationCore, window);
            ValidatePostShowItemContainerStyleSelector(presentationCore, window);
            ValidatePostShowImplicitDataTemplate(presentationCore, window);
            ValidatePostShowContentTemplateSelector(presentationCore, window);
            ValidatePostShowHierarchicalDataTemplate(presentationCore, window);
            ValidatePortableKeyboardFocus(presentationCore, window);
            ValidatePortableInputBindingActivation(presentationCore, activation, window);
            ValidatePortableTextInputActivation(presentationCore, activation, window);
            ValidatePortableMouseClickActivation(presentationCore, activation, window);
            ValidatePortableMouseWheelActivation(presentationCore, activation, window);
        }
        finally
        {
            if (activation != null)
            {
                if (window != null)
                {
                    TryInvoke(window, "Close");
                }

                TryInvoke(activation, "Dispose");
            }

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

    private static void ValidateMainWindow(object window, object application)
    {
        AssertType(window, "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow", "main window");
        AssertEqual("ProGPU WPF XAML smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(420.0, GetProperty(window, "Width"), "window width");
        AssertEqual(260.0, GetProperty(window, "Height"), "window height");

        object content = GetProperty(window, "Content");
        AssertType(content, "System.Windows.Controls.StackPanel", "window content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expected: 39, "stack panel children");

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
        ValidateXmlDataProvider(window);
        ValidateStoryboardEventTrigger(window);
        ValidateMarkupExtension(window);
        ValidateMergedResourceDictionary(window, application);
        ValidateUnsharedResource(window, application);
        ValidateNestedUserControl(window);
        ValidateReadOnlyGridCollectionsAndAttachedProperties(window);
        ValidateImplicitMergedStyle(window, application);
        ValidateXamlEventHandler(window);
        ValidateStyleEventSetter(window);
        ValidateRoutedCommand(window);
        ValidateInputBinding(window);
        ValidateStyleAndDataTrigger(window, application);
        ValidateTemplateAndDynamicResource(window, application);
        ValidateItemsBindingAndTemplate(window);
        ValidateImplicitDataTemplate(window);
        ValidateContentTemplateSelector(window);
        ValidateHierarchicalDataTemplate(window);
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

    private static void ValidatePortableKeyboardFocus(Assembly presentationCore, object window)
    {
        object inputBox = GetField(window, "InputBox");
        if (!Equals(true, GetProperty(inputBox, "IsVisible")))
        {
            object content = GetProperty(window, "Content");
            throw new InvalidOperationException(
                "Expected compiled TextBox visible after portable show. " +
                $"WindowTemplate={DescribeOptionalProperty(window, "Template")}; " +
                $"WindowStyle={DescribeOptionalProperty(window, "Style")}; " +
                $"WindowThemeStyle={DescribeOptionalProperty(window, "ThemeStyle")}; " +
                $"WindowVisualChildren={DescribeOptionalProperty(window, "VisualChildrenCount")}; " +
                $"WindowVisibility={DescribeOptionalProperty(window, "Visibility")}; " +
                $"WindowIsVisible={DescribeOptionalProperty(window, "IsVisible")}; " +
                $"WindowSource={DescribePresentationSource(presentationCore, window)}; " +
                $"ContentType={content.GetType().FullName}; " +
                $"ContentIsVisible={DescribeOptionalProperty(content, "IsVisible")}; " +
                $"ContentParent={DescribeOptionalProperty(content, "Parent")}; " +
                $"ContentVisualParent={DescribeVisualParent(presentationCore, content)}; " +
                $"ContentSource={DescribePresentationSource(presentationCore, content)}; " +
                $"InputParent={DescribeOptionalProperty(inputBox, "Parent")}; " +
                $"InputVisualParent={DescribeVisualParent(presentationCore, inputBox)}; " +
                $"InputSource={DescribePresentationSource(presentationCore, inputBox)}; " +
                $"InputTemplatedParent={DescribeOptionalProperty(inputBox, "TemplatedParent")}.");
        }

        Type keyboardType = GetRequiredType(presentationCore, "System.Windows.Input.Keyboard");
        object focused = InvokeStatic(keyboardType, "Focus", inputBox);
        if (!ReferenceEquals(inputBox, focused))
        {
            throw new InvalidOperationException(
                "Keyboard.Focus did not return the compiled TextBox. " +
                $"Focused={focused.GetType().FullName}; " +
                $"IsVisible={GetProperty(inputBox, "IsVisible")}; " +
                $"Focusable={GetProperty(inputBox, "Focusable")}; " +
                $"IsEnabled={GetProperty(inputBox, "IsEnabled")}; " +
                $"WindowIsVisible={GetProperty(window, "IsVisible")}.");
        }

        AssertSame(inputBox, focused, "compiled TextBox Keyboard.Focus return value");
        AssertSame(inputBox, GetStaticProperty(keyboardType, "FocusedElement"), "compiled TextBox Keyboard focused element");
        AssertEqual(true, GetProperty(inputBox, "IsKeyboardFocused"), "compiled TextBox keyboard focus state");

        InvokeStatic(keyboardType, "ClearFocus");
        AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Keyboard clear focus");
    }

    private static void ValidatePortableInputBindingActivation(Assembly presentationCore, object activation, object window)
    {
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException(
                $"Expected a ProGPU portable activation for input routing, got '{activation.GetType().FullName}'.");
        }

        object inputBox = GetField(window, "InputBox");
        Type keyboardType = GetRequiredType(presentationCore, "System.Windows.Input.Keyboard");
        object focused = InvokeStatic(keyboardType, "Focus", inputBox);
        AssertSame(inputBox, focused, "portable input KeyBinding focused target");

        int initialExecutionCount = Convert.ToInt32(GetProperty(window, "RoutedCommandExecutionCount"));
        var keyDown = new WpfInputEventArgs(
            WpfInputEventKind.KeyDown,
            key: "F6",
            scanCode: 0,
            modifiers: WpfInputModifiers.Control);
        RaiseHostInput(portableActivation.Host, keyDown);

        AssertEqual(true, keyDown.Handled, "portable input KeyBinding handled state");
        AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable input KeyBinding command execution count");
        AssertEqual("input binding payload", GetProperty(window, "LastRoutedCommandParameter"), "portable input KeyBinding command parameter");

        var keyUp = new WpfInputEventArgs(
            WpfInputEventKind.KeyUp,
            key: "F6",
            scanCode: 0,
            modifiers: WpfInputModifiers.None);
        RaiseHostInput(portableActivation.Host, keyUp);
        AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable input KeyBinding ignores key up");

        InvokeStatic(keyboardType, "ClearFocus");
        AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable input KeyBinding clear focus");
    }

    private static void ValidatePortableTextInputActivation(Assembly presentationCore, object activation, object window)
    {
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException(
                $"Expected a ProGPU portable activation for text input routing, got '{activation.GetType().FullName}'.");
        }

        object inputBox = GetField(window, "InputBox");
        Type keyboardType = GetRequiredType(presentationCore, "System.Windows.Input.Keyboard");
        SetProperty(inputBox, "Text", "portable ");
        Invoke(inputBox, "Select", "portable ".Length, 0);
        object focused = InvokeStatic(keyboardType, "Focus", inputBox);
        AssertSame(inputBox, focused, "portable text input focused target");

        var textInput = new WpfInputEventArgs(
            WpfInputEventKind.TextInput,
            character: 'x');
        RaiseHostInput(portableActivation.Host, textInput);

        AssertEqual(true, textInput.Handled, "portable text input handled state");
        AssertEqual("portable x", GetProperty(inputBox, "Text"), "portable text input TextBox text");
        AssertEqual("portable x".Length, GetProperty(inputBox, "SelectionStart"), "portable text input caret index");
        AssertEqual(0, GetProperty(inputBox, "SelectionLength"), "portable text input selection length");

        InvokeStatic(keyboardType, "ClearFocus");
        AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable text input clear focus");
    }

    private static void ValidatePortableMouseClickActivation(Assembly presentationCore, object activation, object window)
    {
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException(
                $"Expected a ProGPU portable activation for mouse input routing, got '{activation.GetType().FullName}'.");
        }

        object eventButton = GetField(window, "EventButton");
        Invoke(window, "UpdateLayout");
        Invoke(eventButton, "UpdateLayout");
        (double x, double y) = GetElementCenterInWindow(presentationCore, eventButton, window);
        object? directHit = InvokeNullable(window, "InputHitTest", GetElementCenterPointInWindow(eventButton, window));

        int initialClickCount = Convert.ToInt32(GetProperty(window, "XamlClickCount"));
        var mouseMove = new WpfInputEventArgs(
            WpfInputEventKind.MouseMove,
            x: x,
            y: y);
        RaiseHostInput(
            portableActivation.Host,
            mouseMove);
        Type mouseType = GetRequiredType(presentationCore, "System.Windows.Input.Mouse");
        object? directlyOverAfterMove = TryGetStaticProperty(mouseType, "DirectlyOver");
        if (directlyOverAfterMove == null)
        {
            throw new InvalidOperationException(
                $"Expected portable mouse move to update Mouse.DirectlyOver. " +
                $"MoveHandled={mouseMove.Handled}, Input=({x}, {y}), InputHitTest={DescribeInputElement(directHit)}.");
        }

        int initialGotCaptureCount = Convert.ToInt32(GetProperty(window, "XamlGotMouseCaptureCount"));
        int initialLostCaptureCount = Convert.ToInt32(GetProperty(window, "XamlLostMouseCaptureCount"));
        RaiseHostInput(
            portableActivation.Host,
            new WpfInputEventArgs(
                WpfInputEventKind.MouseDown,
                x: x,
                y: y,
                button: WpfMouseButton.Left));
        object capturedAfterDown = TryGetStaticProperty(mouseType, "Captured")
            ?? throw new InvalidOperationException("Expected portable mouse capture after mouse down.");
        AssertSame(eventButton, capturedAfterDown, "portable mouse captured element after down");
        AssertEqual(true, GetProperty(eventButton, "IsMouseCaptured"), "portable mouse ButtonBase IsMouseCaptured after down");
        AssertEqual(true, GetProperty(eventButton, "IsPressed"), "portable mouse ButtonBase IsPressed after down");
        AssertEqual(initialGotCaptureCount + 1, GetProperty(window, "XamlGotMouseCaptureCount"), "portable mouse GotMouseCapture count");
        AssertEqual("EventButton", GetProperty(window, "LastXamlGotMouseCaptureSenderName"), "portable mouse GotMouseCapture sender name");
        AssertEqual("GotMouseCapture", GetProperty(window, "LastXamlGotMouseCaptureRoutedEventName"), "portable mouse GotMouseCapture event name");

        RaiseHostInput(
            portableActivation.Host,
            new WpfInputEventArgs(
                WpfInputEventKind.MouseUp,
                x: x,
                y: y,
                button: WpfMouseButton.Left));
        AssertEqual(null, TryGetStaticProperty(mouseType, "Captured"), "portable mouse captured element after up");
        AssertEqual(false, GetProperty(eventButton, "IsMouseCaptured"), "portable mouse ButtonBase IsMouseCaptured after up");
        AssertEqual(false, GetProperty(eventButton, "IsPressed"), "portable mouse ButtonBase IsPressed after up");
        AssertEqual(initialLostCaptureCount + 1, GetProperty(window, "XamlLostMouseCaptureCount"), "portable mouse LostMouseCapture count");
        AssertEqual("EventButton", GetProperty(window, "LastXamlLostMouseCaptureSenderName"), "portable mouse LostMouseCapture sender name");
        AssertEqual("LostMouseCapture", GetProperty(window, "LastXamlLostMouseCaptureRoutedEventName"), "portable mouse LostMouseCapture event name");

        AssertPortableMouseClick(
            presentationCore,
            window,
            eventButton,
            directHit,
            initialClickCount + 1,
            x,
            y,
            "portable mouse routed Click count");
        AssertEqual("EventButton", GetProperty(window, "LastXamlClickSenderName"), "portable mouse routed Click sender name");
        AssertEqual("Click", GetProperty(window, "LastXamlClickRoutedEventName"), "portable mouse routed Click event name");
    }

    private static void ValidatePortableMouseWheelActivation(Assembly presentationCore, object activation, object window)
    {
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException(
                $"Expected a ProGPU portable activation for mouse wheel routing, got '{activation.GetType().FullName}'.");
        }

        object eventButton = GetField(window, "EventButton");
        Invoke(window, "UpdateLayout");
        Invoke(eventButton, "UpdateLayout");
        (double x, double y) = GetElementCenterInWindow(presentationCore, eventButton, window);

        int initialWheelCount = Convert.ToInt32(GetProperty(window, "XamlMouseWheelCount"));
        RaiseHostInput(
            portableActivation.Host,
            new WpfInputEventArgs(
                WpfInputEventKind.MouseWheel,
                x: x,
                y: y,
                deltaY: 1));

        AssertEqual(initialWheelCount + 1, GetProperty(window, "XamlMouseWheelCount"), "portable mouse wheel routed event count");
        AssertEqual(120, GetProperty(window, "LastXamlMouseWheelDelta"), "portable mouse wheel routed event delta");
        AssertEqual("EventButton", GetProperty(window, "LastXamlMouseWheelSenderName"), "portable mouse wheel sender name");
        AssertEqual("MouseWheel", GetProperty(window, "LastXamlMouseWheelRoutedEventName"), "portable mouse wheel routed event name");
    }

    private static void AssertPortableMouseClick(
        Assembly presentationCore,
        object window,
        object eventButton,
        object? directHit,
        int expectedClickCount,
        double x,
        double y,
        string description)
    {
        object actualClickCount = GetProperty(window, "XamlClickCount");
        if (Equals(expectedClickCount, actualClickCount))
        {
            return;
        }

        Type mouseType = GetRequiredType(presentationCore, "System.Windows.Input.Mouse");
        object? directlyOver = TryGetStaticProperty(mouseType, "DirectlyOver");
        object? captured = TryGetStaticProperty(mouseType, "Captured");
        throw new InvalidOperationException(
            $"Expected {description} to be '{expectedClickCount}', got '{actualClickCount}'. " +
            $"Input=({x}, {y}), DirectlyOver={DescribeInputElement(directlyOver)}, " +
            $"InputHitTest={DescribeInputElement(directHit)}, " +
            $"Captured={DescribeInputElement(captured)}, " +
            $"Button.IsMouseOver={GetProperty(eventButton, "IsMouseOver")}, " +
            $"Button.IsMouseCaptured={GetProperty(eventButton, "IsMouseCaptured")}, " +
            $"Button.IsPressed={GetProperty(eventButton, "IsPressed")}.");
    }

    private static string DescribeInputElement(object? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        string name = DescribeOptionalProperty(element, "Name");
        return $"{element.GetType().FullName}(Name={name})";
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

    private static void ShowPortableActivation(
        Assembly presentationFramework,
        object window,
        out Type activationServiceType,
        out object activation)
    {
        if (!WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
                presentationFramework,
                hostFactory: w => new ProGpuWpfWindowHost(WpfPortableWindowActivation.CreateHostOptions(w))))
        {
            throw new InvalidOperationException("Failed to register ProGPU portable activation with real PresentationFramework.");
        }

        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");

        Invoke(window, "Show");
        Invoke(window, "UpdateLayout");
        activation = GetProperty(window, "PortableWindowActivation");
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException($"Expected a ProGPU activation, got {activation.GetType().FullName}.");
        }

        AssertSame(window, portableActivation.Window, "activation window");
        AssertSame(window, portableActivation.RootVisual, "activation root visual");
        AssertEqual("Visible", GetProperty(window, "Visibility").ToString(), "portable window visibility");
        AssertEqual(true, GetProperty(window, "IsVisible"), "portable window visible state");
        AssertEqual(true, portableActivation.Host.IsVisible, "host visible state");
        AssertEqual("ProGPU WPF XAML smoke", portableActivation.Host.Title, "host title");
        AssertEqual(420, portableActivation.Host.Width, "host width");
        AssertEqual(260, portableActivation.Host.Height, "host height");
    }

    private static void FlushDispatcherOperations(Type activationServiceType, object window, params string[] markerPriorityNames)
    {
        MethodInfo flushMethod = activationServiceType.GetMethod(
            "FlushDispatcherOperations",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "FlushDispatcherOperations");
        Type dispatcherPriorityType = flushMethod.GetParameters()[1].ParameterType;

        foreach (string markerPriorityName in markerPriorityNames)
        {
            object markerPriority = Enum.Parse(dispatcherPriorityType, markerPriorityName);
            flushMethod.Invoke(null, new[] { window, markerPriority });
        }
    }

    private static void RaiseHostInput(ProGpuWpfWindowHost host, WpfInputEventArgs input)
    {
        MethodInfo inputMethod = typeof(ProGpuWpfWindowHost).GetMethod(
            "OnPlatformInputReceived",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ProGpuWpfWindowHost).FullName, "OnPlatformInputReceived");

        inputMethod.Invoke(host, new object?[] { null, input });
    }

    private static (double X, double Y) GetElementCenterInWindow(Assembly presentationCore, object element, object window)
    {
        object windowPoint = GetElementCenterPointInWindow(element, window);
        object transformToDevice = GetTransformToDevice(presentationCore, window);
        (double x, double y) = TransformPoint(transformToDevice, windowPoint);

        return (x, y);
    }

    private static object GetElementCenterPointInWindow(object element, object window)
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
        return Invoke(element, "TranslatePoint", center, window);
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
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property.GetValue(instance)
                    ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
            }
        }

        throw new MissingMemberException(instance.GetType().FullName, propertyName);
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
    }

    private static string DescribeOptionalProperty(object instance, string propertyName)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property == null)
            {
                continue;
            }

            object? value = property.GetValue(instance);
            return value == null ? "<null>" : value.ToString() ?? value.GetType().FullName ?? "<value>";
        }

        return "<missing>";
    }

    private static string DescribePresentationSource(Assembly presentationCore, object visual)
    {
        Type presentationSourceType = GetRequiredType(presentationCore, "System.Windows.PresentationSource");
        MethodInfo method = presentationSourceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, "FromVisual", StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(visual.GetType());
            })
            ?? throw new MissingMethodException(presentationSourceType.FullName, "FromVisual");

        object? source = method.Invoke(null, new[] { visual });
        return source == null ? "<null>" : source.GetType().FullName ?? source.ToString() ?? "<source>";
    }

    private static string DescribeVisualParent(Assembly presentationCore, object visual)
    {
        Type visualTreeHelperType = GetRequiredType(presentationCore, "System.Windows.Media.VisualTreeHelper");
        MethodInfo method = visualTreeHelperType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, "GetParent", StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(visual.GetType());
            })
            ?? throw new MissingMethodException(visualTreeHelperType.FullName, "GetParent");

        object? parent = method.Invoke(null, new[] { visual });
        return parent == null ? "<null>" : parent.GetType().FullName ?? parent.ToString() ?? "<parent>";
    }

    private static object? FindVisualDescendantByName(Assembly presentationCore, object root, string name)
    {
        if (string.Equals(DescribeOptionalProperty(root, "Name"), name, StringComparison.Ordinal))
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

    private static object? TryGetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
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
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo? method = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    ParameterInfo[] candidateParameters = candidate.GetParameters();
                    return candidateParameters.Length == parameters.Length;
                });

            if (method != null)
            {
                return method.Invoke(instance, parameters) ?? new object();
            }
        }

        throw new MissingMethodException(instance.GetType().FullName, methodName);
    }

    private static object? InvokeNullable(object instance, string methodName, params object?[] parameters)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo? method = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    ParameterInfo[] candidateParameters = candidate.GetParameters();
                    return candidateParameters.Length == parameters.Length;
                });

            if (method != null)
            {
                return method.Invoke(instance, parameters);
            }
        }

        throw new MissingMethodException(instance.GetType().FullName, methodName);
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
