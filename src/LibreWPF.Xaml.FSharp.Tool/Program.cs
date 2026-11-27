using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace LibreWPF.Xaml.FSharp;

// F# has no source generators: this tool runs the XAML generator over a C# compilation made of the project's references (no C# source of the project
// is needed: the x:Class type comes out of the generator) and prints the C# it generates as F#.
//
//   LibreWPF.Xaml.FSharp.Tool --out <dir> --references <file with one assembly path per line> [--assembly <name>] [--project-dir <dir>] [--properties <file>] [--bootstrap] <file.xaml>...
//   --bootstrap: the project is an executable with the SDK's F# bootstrap; generated classes call ProGpu.Wpf.Sdk.PortableBootstrap.ensure first.
//   --properties: one name=value per line, forwarded as build_property.name to the generator (the SDK writes the XSG feature and theme properties).
//
// One <full class name>.g.fs per x:Class (a type name two files share is an error); no XAML file is fine (only the support file); errors and warnings are written as MSBuild canonical lines. Exit code 1 when there is an error.
internal static class Program
{
    private static int Main(string[] args)
    {
        string? output = null;
        string? references = null;
        var bootstrap = false;
        var keepCSharp = false;
        var assemblyName = "XsgFSharp";
        string? projectDirectory = null;
        string? propertiesFile = null;
        var xaml = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] is "--out" or "--references" or "--assembly" or "--project-dir" or "--properties") && i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"{args[i]} needs a value");
                return 2;
            }

            switch (args[i])
            {
                case "--out": output = args[++i]; break;
                case "--references": references = args[++i]; break;
                case "--bootstrap": bootstrap = true; break;
                case "--keep-csharp": keepCSharp = true; break;
                case "--assembly": assemblyName = args[++i]; break;
                case "--project-dir": projectDirectory = args[++i]; break;
                case "--properties": propertiesFile = args[++i]; break;
                default: xaml.Add(args[i]); break;
            }
        }

        if (output is null || references is null)
        {
            Console.Error.WriteLine("usage: LibreWPF.Xaml.FSharp.Tool --out <dir> --references <file> [<file.xaml>...]");
            return 2;
        }

        Directory.CreateDirectory(output);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (propertiesFile is not null)
        {
            if (!File.Exists(propertiesFile))
            {
                Console.Error.WriteLine($"the properties file {propertiesFile} does not exist");
                return 2;
            }

            foreach (var line in File.ReadAllLines(propertiesFile))
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                {
                    properties[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }
            }
        }

        var metadata = File.ReadAllLines(references).Where(l => l.Trim().Length > 0).Select(l => (MetadataReference)MetadataReference.CreateFromFile(l.Trim())).ToList();
        var compilation = CSharpCompilation.Create(assemblyName, Array.Empty<SyntaxTree>(), metadata, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { new XamlSourceGenerator().AsSourceGenerator() },
            additionalTexts: xaml.Select(f => (AdditionalText)new FileText(Path.GetFullPath(f))).ToArray(),
            parseOptions: null,
            optionsProvider: new Options(projectDirectory, properties));
        var result = driver.RunGenerators(compilation).GetRunResult();

        var failed = false;
        foreach (var diagnostic in result.Diagnostics)
        {
            failed |= diagnostic.Severity == DiagnosticSeverity.Error;
            Console.Error.WriteLine($"{diagnostic.Id}: {(diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning")} {diagnostic.GetMessage()}");
        }

        // Nothing is written before every file has been translated: a failed run leaves no partial output for the compiler to pick up.
        var files = new List<(string Name, string Text)> { (XsgSupport.FileName, XsgSupport.Text.ReplaceLineEndings("\n")) };
        var order = new List<string> { XsgSupport.FileName };
        var dictionaries = new List<string>();
        var classes = new List<string>();
        foreach (var tree in result.GeneratedTrees)
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(tree.FilePath)) + ".g.fs";
            if (keepCSharp)
            {
                files.Add((name + ".cs.txt", tree.GetText().ToString()));
            }

            if (files.Any(f => f.Name == name))
            {
                Console.Error.WriteLine($"LWXSG1003: error {name}: two XAML files produce the same type name; give the x:Class types distinct names");
                failed = true;
                continue;
            }

            if (!FSharpPrinter.TryPrint(tree.GetText().ToString(), out var fsharp, out var error, bootstrap))
            {
                Console.Error.WriteLine($"LWXSG1002: error {name}: {error}");
                failed = true;
                continue;
            }

            files.Add((name, fsharp));
            // F# is order sensitive: loose dictionaries (constructed by the classes that merge them) come first.
            (name.StartsWith("__XsgResourceDictionary_", StringComparison.Ordinal) ? dictionaries : classes).Add(name);
        }

        dictionaries.Sort(StringComparer.Ordinal);
        classes.Sort(StringComparer.Ordinal);
        order.AddRange(dictionaries);
        order.AddRange(classes);
        if (failed)
        {
            return 1;
        }

        files.Add(("compile.txt", string.Join("\n", order) + "\n"));
        foreach (var (name, text) in files)
        {
            File.WriteAllText(Path.Combine(output, name), text);
        }

        return 0;
    }

    private sealed class FileText(string path) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(File.ReadAllText(path));
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        private readonly Values _global;

        public Options(string? projectDirectory, IReadOnlyDictionary<string, string> properties)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in properties)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    values["build_property." + name.Trim()] = value;
                }
            }

            values["build_property.LibreWpfXsgOutputLanguage"] = "FSharp";
            values["build_property.UseLibreWpfXamlSourceGenerator"] = "true";

            if (projectDirectory is not null)
            {
                values["build_property.MSBuildProjectDirectory"] = projectDirectory;
            }

            _global = new Values(values);
        }

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;
    }

    private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
