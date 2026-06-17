using System.Reflection;
using System.Runtime.Loader;
using System.Collections;
using System.Windows.Media.ProGPU;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuRenderCommand = global::ProGPU.Scene.RenderCommand;
using ProGpuRenderCommandType = global::ProGPU.Scene.RenderCommandType;
using ProGpuVisual = global::ProGPU.Scene.Visual;

internal static class Program
{
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";

    [STAThread]
    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationFrameworkPath = FindRealAssembly(repoRoot, "PresentationFramework");
            string presentationCorePath = FindRealAssembly(repoRoot, "PresentationCore");
            RunHarness(repoRoot, presentationFrameworkPath, presentationCorePath);
            Console.WriteLine("Real PresentationFramework code-only smoke succeeded.");
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
        string presentationCorePath)
    {
        var loadContext = new WpfAssemblyLoadContext(repoRoot, presentationFrameworkPath, presentationCorePath);
        Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyPath(presentationCorePath);
        Assembly windowsBase = loadContext.LoadFromAssemblyName(new AssemblyName("WindowsBase"));

        object? application = null;
        object? activation = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(presentationFramework, "System.Windows.Application");
            object window = Create(presentationFramework, "System.Windows.Window");
            SetProperty(window, "Title", "ProGPU WPF smoke");
            SetProperty(window, "Width", 320.0);
            SetProperty(window, "Height", 200.0);

            object stackPanel = Create(presentationFramework, "System.Windows.Controls.StackPanel");
            object textBox = Create(presentationFramework, "System.Windows.Controls.TextBox");
            SetProperty(textBox, "Text", "text input smoke");
            object richTextBox = Create(presentationFramework, "System.Windows.Controls.RichTextBox");
            object flowDocument = CreateFlowDocument(presentationFramework);
            SetProperty(richTextBox, "Document", flowDocument);

            AddToCollection(GetProperty(stackPanel, "Children"), textBox);
            AddToCollection(GetProperty(stackPanel, "Children"), richTextBox);
            SetProperty(window, "Content", stackPanel);

            object resources = CreateResourceDictionary(presentationFramework);
            SetProperty(application, "Resources", resources);
            SetProperty(window, "Resources", resources);

            AssertEqual("ProGPU WPF smoke", GetProperty(window, "Title"), "window title");
            AssertEqual(320.0, GetProperty(window, "Width"), "window width");
            AssertEqual(200.0, GetProperty(window, "Height"), "window height");
            AssertEqual(stackPanel, GetProperty(window, "Content"), "window content");
            AssertCollectionCount(GetProperty(stackPanel, "Children"), expected: 2, "stack panel children");
            AssertCollectionCount(GetProperty(resources, "Keys"), expected: 2, "resource dictionary keys");
            AssertCollectionCount(GetProperty(flowDocument, "Blocks"), expected: 1, "flow document blocks");

            RegisterPortableActivation(presentationFramework, window, out activationServiceType, out activation);

            using var target = ProGpuWpfCompositionTarget.CreateHeadless();
            var frame = target.BeginDrawingFrame(96, 64);
            if (!WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(
                    presentationCore,
                    frame,
                    imageSourceAdapter: null,
                    out IDisposable? registration) ||
                registration == null)
            {
                throw new InvalidOperationException("Failed to register ProGPU object sink factory against real PresentationCore.");
            }

            using (registration)
            {
                DrawRealDrawingVisual(presentationCore, windowsBase);
            }

            VerifyRetainedDrawingVisualBranch(target);
        }
        finally
        {
            if (activation != null)
            {
                Invoke(activation, "Dispose");
            }

            activationServiceType?.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);

            if (application != null)
            {
                Invoke(application, "Shutdown");
            }

            loadContext.Unload();
        }
    }

    private static object CreateFlowDocument(Assembly presentationFramework)
    {
        object run = Create(presentationFramework, "System.Windows.Documents.Run", "rich text smoke");
        object paragraph = Create(presentationFramework, "System.Windows.Documents.Paragraph", run);
        object flowDocument = Create(presentationFramework, "System.Windows.Documents.FlowDocument");
        AddToCollection(GetProperty(flowDocument, "Blocks"), paragraph);
        return flowDocument;
    }

    private static object CreateResourceDictionary(Assembly presentationFramework)
    {
        Type textBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.TextBox");
        Type buttonType = GetRequiredType(presentationFramework, "System.Windows.Controls.Button");

        object resources = Create(presentationFramework, "System.Windows.ResourceDictionary");
        object textBoxStyle = Create(presentationFramework, "System.Windows.Style", textBoxType);
        object buttonTemplate = Create(presentationFramework, "System.Windows.Controls.ControlTemplate", buttonType);

        AddToDictionary(resources, textBoxType, textBoxStyle);
        AddToDictionary(resources, buttonType, buttonTemplate);
        return resources;
    }

    private static void RegisterPortableActivation(
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

        MethodInfo tryActivate = activationServiceType.GetMethod(
            "TryActivate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "TryActivate");
        object?[] parameters = { window, null };
        if (!Equals(true, tryActivate.Invoke(null, parameters)) || parameters[1] == null)
        {
            throw new InvalidOperationException("Real PresentationFramework did not create a portable ProGPU activation.");
        }

        activation = parameters[1]!;
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException($"Expected a ProGPU activation, got {activation.GetType().FullName}.");
        }

        AssertEqual(window, portableActivation.Window, "activation window");
        AssertEqual(window, portableActivation.RootVisual, "activation root visual");
        AssertEqual("ProGPU WPF smoke", portableActivation.Host.Title, "host title");
        AssertEqual(320, portableActivation.Host.Width, "host width");
        AssertEqual(200, portableActivation.Host.Height, "host height");

        activationServiceType.GetMethod(
            "SetTitle",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(null, new[] { activation, "ProGPU WPF smoke updated" });
        activationServiceType.GetMethod(
            "SetClientSize",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { activation, 480.0, 240.0 });

        AssertEqual("ProGPU WPF smoke updated", portableActivation.Host.Title, "updated host title");
        AssertEqual(480, portableActivation.Host.Width, "updated host width");
        AssertEqual(240, portableActivation.Host.Height, "updated host height");
    }

    private static void DrawRealDrawingVisual(Assembly presentationCore, Assembly windowsBase)
    {
        object drawingVisual = Create(presentationCore, "System.Windows.Media.DrawingVisual");
        object drawingContext = Invoke(drawingVisual, "RenderOpen");

        Type brushType = GetRequiredType(presentationCore, "System.Windows.Media.Brush");
        Type penType = GetRequiredType(presentationCore, "System.Windows.Media.Pen");
        Type drawingType = GetRequiredType(presentationCore, "System.Windows.Media.Drawing");
        Type formattedTextType = GetRequiredType(presentationCore, "System.Windows.Media.FormattedText");
        Type glyphRunType = GetRequiredType(presentationCore, "System.Windows.Media.GlyphRun");
        Type geometryType = GetRequiredType(presentationCore, "System.Windows.Media.Geometry");
        Type transformType = GetRequiredType(presentationCore, "System.Windows.Media.Transform");
        Type pointType = GetRequiredType(windowsBase, "System.Windows.Point");
        Type rectType = GetRequiredType(windowsBase, "System.Windows.Rect");

        Type colorsType = GetRequiredType(presentationCore, "System.Windows.Media.Colors");
        object redBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Red"));
        object greenBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Green"));
        object blueBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Blue"));
        object purpleBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Purple"));
        object bluePen = Create(presentationCore, "System.Windows.Media.Pen", blueBrush, 2.0);

        object rect = Activator.CreateInstance(rectType, 4.0, 5.0, 24.0, 12.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object guidelineRect = Activator.CreateInstance(rectType, 2.25, 3.25, 40.0, 50.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object lineStart = Activator.CreateInstance(pointType, 2.0, 3.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object lineEnd = Activator.CreateInstance(pointType, 40.0, 20.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object ellipseCenter = Activator.CreateInstance(pointType, 28.0, 24.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object geometryRect = Activator.CreateInstance(rectType, 10.0, 28.0, 18.0, 11.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object rectangleGeometry = Create(presentationCore, "System.Windows.Media.RectangleGeometry", geometryRect);
        object clipRect = Activator.CreateInstance(rectType, 1.0, 1.0, 42.0, 34.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object clipGeometry = Create(presentationCore, "System.Windows.Media.RectangleGeometry", clipRect);
        object drawingGeometryRect = Activator.CreateInstance(rectType, 46.0, 8.0, 14.0, 9.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object drawingGeometry = Create(presentationCore, "System.Windows.Media.RectangleGeometry", drawingGeometryRect);
        object geometryDrawing = Create(
            presentationCore,
            "System.Windows.Media.GeometryDrawing",
            purpleBrush,
            null,
            drawingGeometry);
        object transform = Create(presentationCore, "System.Windows.Media.TranslateTransform", 6.0, 7.0);
        object guidelineSet = CreateDynamicGuidelineSet(
            presentationCore,
            new[] { 2.25, 42.25 },
            new[] { 3.25, 53.25 });
        object glyphRun = CreateRealGlyphRun(presentationCore, windowsBase, pointType);
        object formattedText = CreateRealFormattedText(presentationCore, greenBrush);
        object textOrigin = Activator.CreateInstance(pointType, 18.0, 82.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");

        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            redBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "DrawLine",
            new[] { penType, pointType, pointType },
            bluePen,
            lineStart,
            lineEnd);
        InvokeDrawing(
            drawingContext,
            "DrawEllipse",
            new[] { brushType, penType, pointType, typeof(double), typeof(double) },
            greenBrush,
            null,
            ellipseCenter,
            9.0,
            5.0);
        InvokeDrawing(
            drawingContext,
            "DrawGeometry",
            new[] { brushType, penType, geometryType },
            purpleBrush,
            null,
            rectangleGeometry);
        InvokeDrawing(
            drawingContext,
            "PushOpacity",
            new[] { typeof(double) },
            0.5);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            greenBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "PushClip",
            new[] { geometryType },
            clipGeometry);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            blueBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "PushTransform",
            new[] { transformType },
            transform);
        InvokeDrawing(
            drawingContext,
            "DrawLine",
            new[] { penType, pointType, pointType },
            bluePen,
            lineStart,
            lineEnd);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "PushOpacityMask",
            new[] { brushType },
            redBrush);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            purpleBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "DrawRoundedRectangle",
            new[] { brushType, penType, rectType, typeof(double), typeof(double) },
            purpleBrush,
            null,
            rect,
            4.0,
            6.0);
        InvokeDrawing(
            drawingContext,
            "PushGuidelineSet",
            new[] { guidelineSet.GetType() },
            guidelineSet);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            redBrush,
            null,
            guidelineRect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "DrawDrawing",
            new[] { drawingType },
            geometryDrawing);
        InvokeDrawing(
            drawingContext,
            "DrawGlyphRun",
            new[] { brushType, glyphRunType },
            blueBrush,
            glyphRun);
        InvokeDrawing(
            drawingContext,
            "DrawText",
            new[] { formattedTextType, pointType },
            formattedText,
            textOrigin);
        Invoke(drawingContext, "Close");
    }

    private static void VerifyRetainedDrawingVisualBranch(ProGpuWpfCompositionTarget target)
    {
        if (target.RootVisual.Context.Commands.Count != 0)
        {
            throw new InvalidOperationException(
                $"Expected real DrawingVisual RenderOpen output to use the retained WPF owner branch, but the flat root received {target.RootVisual.Context.Commands.Count} commands.");
        }

        ProGpuContainerVisual retainedFrameRoot = GetSingleContainerChild(
            target.RetainedWpfVisualRoot,
            "retained WPF frame root");
        ProGpuVisual ownerBranch = GetSingleChild(
            retainedFrameRoot,
            "real framework drawing visual owner branch");
        IReadOnlyList<ProGpuRenderCommand> commands = GetRetainedCommands(ownerBranch);
        ProGpuRenderCommandType[] expectedCommandTypes =
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawLine,
            ProGpuRenderCommandType.DrawEllipse,
            ProGpuRenderCommandType.DrawPath,
            ProGpuRenderCommandType.PushOpacity,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PopOpacity,
            ProGpuRenderCommandType.PushGeometryClip,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PopGeometryClip,
            ProGpuRenderCommandType.DrawLine,
            ProGpuRenderCommandType.PushOpacityMask,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PopOpacityMask,
            ProGpuRenderCommandType.DrawRoundedRect,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawPath,
            ProGpuRenderCommandType.DrawGlyphRun,
            ProGpuRenderCommandType.DrawGlyphRun
        };
        if (commands.Count != expectedCommandTypes.Length)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCommandTypes.Length} retained drawing commands after real DrawingVisual dispatch, got {commands.Count} commands.");
        }

        for (var i = 0; i < expectedCommandTypes.Length; i++)
        {
            if (commands[i].Type != expectedCommandTypes[i])
            {
                throw new InvalidOperationException(
                    $"Expected retained DrawingVisual command {i} to be {expectedCommandTypes[i]}, got {commands[i].Type}.");
            }
        }

        AssertEqual(0.5f, commands[4].FontSize, "real DrawingVisual retained opacity value");
        AssertEqual(6f, commands[10].Transform.M41, "real DrawingVisual transformed line X offset");
        AssertEqual(7f, commands[10].Transform.M42, "real DrawingVisual transformed line Y offset");
        if (commands[11].Brush == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained opacity mask to carry a native brush.");
        }

        AssertEqual(4f, commands[14].RadiusX, "real DrawingVisual retained rounded rectangle radius X");
        AssertEqual(6f, commands[14].RadiusY, "real DrawingVisual retained rounded rectangle radius Y");
        AssertEqual(2f, commands[15].Rect.X, "real DrawingVisual retained guideline snapped rect X");
        AssertEqual(3f, commands[15].Rect.Y, "real DrawingVisual retained guideline snapped rect Y");
        AssertEqual(40f, commands[15].Rect.Width, "real DrawingVisual retained guideline snapped rect width");
        AssertEqual(50f, commands[15].Rect.Height, "real DrawingVisual retained guideline snapped rect height");
        if (commands[16].Brush == null || commands[16].Path == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained drawing resource path to carry a native brush and path.");
        }

        ushort[]? glyphIndices = commands[17].GlyphIndices;
        if (glyphIndices == null || glyphIndices.Length != 2 || commands[17].Brush == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained glyph run to carry native glyph indices and brush.");
        }

        AssertEqual(12f, commands[17].FontSize, "real DrawingVisual retained glyph run font size");
        AssertEqual(12f, commands[17].Position.X, "real DrawingVisual retained glyph run position X");
        AssertEqual(64f, commands[17].Position.Y, "real DrawingVisual retained glyph run position Y");

        ushort[]? formattedGlyphIndices = commands[18].GlyphIndices;
        if (formattedGlyphIndices == null || formattedGlyphIndices.Length == 0 || commands[18].Brush == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained formatted text to carry WPF-generated glyph indices and brush.");
        }

        AssertEqual(13f, commands[18].FontSize, "real DrawingVisual retained formatted text font size");
        AssertEqual(18f, commands[18].Position.X, "real DrawingVisual retained formatted text position X");
    }

    private static ProGpuContainerVisual GetSingleContainerChild(ProGpuContainerVisual parent, string description)
    {
        ProGpuVisual visual = GetSingleChild(parent, description);
        return visual as ProGpuContainerVisual
            ?? throw new InvalidOperationException($"Expected {description} to be a container visual, got {visual.GetType().FullName}.");
    }

    private static ProGpuVisual GetSingleChild(ProGpuContainerVisual parent, string description)
    {
        if (parent.Children.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {description}, got {parent.Children.Count} children.");
        }

        return parent.Children[0];
    }

    private static IReadOnlyList<ProGpuRenderCommand> GetRetainedCommands(ProGpuVisual visual)
    {
        PropertyInfo contextProperty = visual.GetType().GetProperty(
            "Context",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' does not expose a drawing context.");

        ProGpuDrawingContext context = contextProperty.GetValue(visual) as ProGpuDrawingContext
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' exposed an unexpected context value.");

        return context.Commands;
    }

    private static object Create(Assembly assembly, string typeName, params object?[] parameters)
    {
        Type type = GetRequiredType(assembly, typeName);
        return Activator.CreateInstance(type, parameters)
            ?? throw new InvalidOperationException($"Failed to create '{typeName}'.");
    }

    private static object CreateDynamicGuidelineSet(Assembly presentationCore, double[] guidelinesX, double[] guidelinesY)
    {
        Type guidelineSetType = GetRequiredType(presentationCore, "System.Windows.Media.GuidelineSet");
        ConstructorInfo constructor = guidelineSetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(double[]), typeof(double[]), typeof(bool) },
            modifiers: null)
            ?? throw new MissingMethodException(guidelineSetType.FullName, ".ctor(double[], double[], bool)");

        object guidelineSet = constructor.Invoke(new object[] { guidelinesX, guidelinesY, true });
        Invoke(guidelineSet, "Freeze");
        return guidelineSet;
    }

    private static object CreateRealGlyphRun(Assembly presentationCore, Assembly windowsBase, Type pointType)
    {
        Type glyphTypefaceType = GetRequiredType(presentationCore, "System.Windows.Media.GlyphTypeface");
        Type glyphRunType = GetRequiredType(presentationCore, "System.Windows.Media.GlyphRun");
        Type xmlLanguageType = GetRequiredType(presentationCore, "System.Windows.Markup.XmlLanguage");
        object glyphTypeface = CreateRealGlyphTypeface(glyphTypefaceType);
        object baselineOrigin = Activator.CreateInstance(pointType, 12.0, 64.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");

        ConstructorInfo constructor = glyphRunType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[]
            {
                glyphTypefaceType,
                typeof(int),
                typeof(bool),
                typeof(double),
                typeof(float),
                typeof(System.Collections.Generic.IList<ushort>),
                pointType,
                typeof(System.Collections.Generic.IList<double>),
                typeof(System.Collections.Generic.IList<>).MakeGenericType(pointType),
                typeof(System.Collections.Generic.IList<char>),
                typeof(string),
                typeof(System.Collections.Generic.IList<ushort>),
                typeof(System.Collections.Generic.IList<bool>),
                xmlLanguageType
            },
            modifiers: null)
            ?? throw new MissingMethodException(glyphRunType.FullName, ".ctor(GlyphTypeface, int, bool, double, float, ...)");

        return constructor.Invoke(new object?[]
        {
            glyphTypeface,
            0,
            false,
            12.0,
            1.0f,
            new ushort[] { 0, 0 },
            baselineOrigin,
            new[] { 7.0, 8.0 },
            null,
            new[] { 'A', 'B' },
            null,
            null,
            null,
            null
        });
    }

    private static object CreateRealFormattedText(Assembly presentationCore, object foregroundBrush)
    {
        Type brushType = GetRequiredType(presentationCore, "System.Windows.Media.Brush");
        Type fontFamilyType = GetRequiredType(presentationCore, "System.Windows.Media.FontFamily");
        Type formattedTextType = GetRequiredType(presentationCore, "System.Windows.Media.FormattedText");
        Type flowDirectionType = GetRequiredType(presentationCore, "System.Windows.FlowDirection");
        Type fontStretchType = GetRequiredType(presentationCore, "System.Windows.FontStretch");
        Type fontStretchesType = GetRequiredType(presentationCore, "System.Windows.FontStretches");
        Type fontStyleType = GetRequiredType(presentationCore, "System.Windows.FontStyle");
        Type fontStylesType = GetRequiredType(presentationCore, "System.Windows.FontStyles");
        Type fontWeightType = GetRequiredType(presentationCore, "System.Windows.FontWeight");
        Type fontWeightsType = GetRequiredType(presentationCore, "System.Windows.FontWeights");
        Type typefaceType = GetRequiredType(presentationCore, "System.Windows.Media.Typeface");
        object fontFamily = Activator.CreateInstance(fontFamilyType, "Arial")
            ?? throw new InvalidOperationException("Failed to create System.Windows.Media.FontFamily.");
        object typeface = typefaceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { fontFamilyType, fontStyleType, fontWeightType, fontStretchType },
            modifiers: null)?.Invoke(new[]
            {
                fontFamily,
                GetStaticProperty(fontStylesType, "Normal"),
                GetStaticProperty(fontWeightsType, "Normal"),
                GetStaticProperty(fontStretchesType, "Normal")
            })
            ?? throw new InvalidOperationException("Failed to create System.Windows.Media.Typeface.");
        object flowDirection = Enum.Parse(flowDirectionType, "LeftToRight");

        ConstructorInfo constructor = formattedTextType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[]
            {
                typeof(string),
                typeof(System.Globalization.CultureInfo),
                flowDirectionType,
                typefaceType,
                typeof(double),
                brushType,
                typeof(double)
            },
            modifiers: null)
            ?? throw new MissingMethodException(formattedTextType.FullName, ".ctor(string, CultureInfo, FlowDirection, Typeface, double, Brush, double)");

        return constructor.Invoke(new object[]
        {
            "Text",
            System.Globalization.CultureInfo.InvariantCulture,
            flowDirection,
            typeface,
            13.0,
            foregroundBrush,
            1.0
        });
    }

    private static object CreateRealGlyphTypeface(Type glyphTypefaceType)
    {
        Exception? lastFailure = null;
        foreach (string fontPath in EnumerateSystemFontFiles())
        {
            try
            {
                return Activator.CreateInstance(glyphTypefaceType, new Uri(fontPath, UriKind.Absolute))
                    ?? throw new InvalidOperationException($"Failed to create real GlyphTypeface for '{fontPath}'.");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or TargetInvocationException)
            {
                lastFailure = ex;
            }
        }

        throw new FileNotFoundException(
            "Could not locate a local TrueType/OpenType font file loadable by the real GlyphTypeface smoke.",
            lastFailure);
    }

    private static IEnumerable<string> EnumerateSystemFontFiles()
    {
        foreach (string directory in EnumerateFontDirectories())
        {
            foreach (string extension in new[] { "*.ttf", "*.otf" })
            {
                foreach (string file in SafeEnumerateFiles(directory, extension))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFontDirectories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return "/System/Library/Fonts/Supplemental";
        yield return "/System/Library/Fonts";
        yield return "/Library/Fonts";
        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".local", "share", "fonts");
            yield return Path.Combine(home, "Library", "Fonts");
        }

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            yield return Path.Combine(windows, "Fonts");
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (string file in files)
        {
            yield return file;
        }
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

    private static void SetProperty(object instance, string propertyName, object value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static object Invoke(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        return method.Invoke(instance, null) ?? new object();
    }

    private static void InvokeDrawing(object drawingContext, string methodName, Type[] parameterTypes, params object?[] parameters)
    {
        MethodInfo method = drawingContext.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null)
            ?? throw new MissingMethodException(drawingContext.GetType().FullName, methodName);
        method.Invoke(drawingContext, parameters);
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

    private static void AddToDictionary(object dictionary, object key, object value)
    {
        MethodInfo add = dictionary.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(object), typeof(object) },
            modifiers: null)
            ?? throw new MissingMethodException(dictionary.GetType().FullName, "Add");
        add.Invoke(dictionary, new[] { key, value });
    }

    private static void AssertCollectionCount(object collection, int expected, string description)
    {
        object count =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");
        AssertEqual(expected, count, description);
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static string FindRealAssembly(string repoRoot, string assemblyName)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName);
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Real {assemblyName} artifacts directory was not found: {artifactsRoot}");
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
            ?? throw new FileNotFoundException($"Could not locate a net11.0 real {assemblyName}.dll artifact.", artifactsRoot);
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
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(
            string repoRoot,
            string presentationFrameworkPath,
            string presentationCorePath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationFrameworkPath = presentationFrameworkPath;
            _presentationCorePath = presentationCorePath;
            _resolver = new AssemblyDependencyResolver(presentationFrameworkPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "PresentationFramework", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationFrameworkPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string outputAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName.Name}.dll");

            if (File.Exists(outputAssemblyPath))
            {
                return LoadFromAssemblyPath(outputAssemblyPath);
            }

            string artifactAssemblyPath = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName.Name ?? string.Empty,
                "Debug",
                "net11.0",
                $"{assemblyName.Name}.dll");

            if (File.Exists(artifactAssemblyPath))
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
