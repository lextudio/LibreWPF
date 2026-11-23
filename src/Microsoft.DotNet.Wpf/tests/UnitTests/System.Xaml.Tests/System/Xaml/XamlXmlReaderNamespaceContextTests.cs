// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Xunit;

namespace System.Xaml.Tests;

/// <summary>
/// <see cref="XamlXmlReader"/> must take the <see cref="XmlReader"/> it is given as it finds it: the reader's position, its line
/// information and above all its namespace context. System.Windows.Markup.XamlReader.Load(Stream, ParserContext) and Parse(text,
/// ParserContext) hand over a reader whose namespace manager holds ParserContext.XmlnsDictionary (ParserContext.ToXmlParserContext),
/// and libraries build XAML in code that way: TomsToolbox creates the data templates of every view model from a string with no xmlns
/// at all and the four prefixes only in the context.
///
/// A change that consumed the given reader and rebuilt it from its text (an attempt at "Simpler XAML" at run time did, in
/// XamlXmlReader.Initialize) lost those prefixes: every such template failed with "Prefix 'x' does not map to a namespace", which
/// took the ILSpy add-in (and with it the packaged-app verification of the release) down. These tests pin the behaviour of a
/// reader built from a document with explicit namespaces and from one whose namespaces come from its context.
/// </summary>
public class XamlXmlReaderNamespaceContextTests
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Local = "clr-namespace:Sample.Local;assembly=Sample";

    // Old style: every namespace declared on the root.
    private const string OldStyle = $$"""
        <Root xmlns="{{Presentation}}" xmlns:x="{{Xaml}}" xmlns:local="{{Local}}" x:Name="r" Tag="{x:Type local:Item}">
          <Child x:Key="k" />
        </Root>
        """;

    // The shape TomsToolbox.Wpf.Composition.DataTemplateManager.CreateTemplate builds: no xmlns in the text, a markup extension that
    // needs the `x` prefix on the root's first attribute, other prefixes only from the context.
    private const string TemplateFromContext =
        "<DataTemplate DataType=\"{x:Type viewModel:MyVm}\"><toms:ComposableContentControl Content=\"{Binding}\" /></DataTemplate>";

    /// <summary>
    /// An XmlReader built the way System.Windows.Markup.XamlReader.Load(Stream, ParserContext) builds it: a stream, default reader
    /// settings, and an XmlParserContext whose namespace manager holds the ParserContext's XmlnsDictionary.
    /// </summary>
    private static XmlReader ReaderWithContext(string xml, params (string Prefix, string Uri)[] namespaces)
    {
        var manager = new XmlNamespaceManager(new NameTable());
        foreach (var (prefix, uri) in namespaces)
        {
            manager.AddNamespace(prefix, uri);
        }

        var context = new XmlParserContext(null, manager, null, XmlSpace.None) { BaseURI = null };
        return XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(xml)), null, context);
    }

    private static XmlReader PlainReader(string xml) => XmlReader.Create(new StringReader(xml));

    /// <summary>The whole node stream, rendered so two runs can be compared. XamlReader.Load builds its reader with line info on.</summary>
    private static List<string> Nodes(XmlReader xml)
    {
        using var reader = new XamlXmlReader(xml, new XamlXmlReaderSettings { ProvideLineInfo = true });
        var nodes = new List<string>();
        while (reader.Read())
        {
            nodes.Add(reader.NodeType switch
            {
                XamlNodeType.NamespaceDeclaration => $"ns {reader.Namespace.Prefix}={reader.Namespace.Namespace}",
                XamlNodeType.StartObject => $"object {reader.Type.PreferredXamlNamespace}:{reader.Type.Name}",
                XamlNodeType.StartMember => $"member {reader.Member.Name}",
                XamlNodeType.Value => $"value {reader.Value}",
                _ => reader.NodeType.ToString(),
            });
        }

        return nodes;
    }

    [Fact]
    public void ATemplateWhosePrefixesComeOnlyFromTheReadersContext_Reads()
    {
        // The exact failure of the ILSpy add-in: "Prefix 'x' does not map to a namespace", line 1, position 21.
        var nodes = Nodes(ReaderWithContext(TemplateFromContext, ("", Presentation), ("x", Xaml),
            ("toms", "clr-namespace:Toms;assembly=Toms"), ("viewModel", "clr-namespace:Vm;assembly=Vm")));

        Assert.Contains($"object {Presentation}:DataTemplate", nodes);
        Assert.Contains("object clr-namespace:Toms;assembly=Toms:ComposableContentControl", nodes);
    }

    [Fact]
    public void ContextNamespacesAreTheOnesTheDocumentIsReadWith_NotOnesAddedBeside()
    {
        // The reader already has a default namespace in scope: the root belongs to it.
        using var reader = new XamlXmlReader(ReaderWithContext("<Root />", ("", Local)));
        string? root = null;
        while (root is null && reader.Read())
        {
            if (reader.NodeType == XamlNodeType.StartObject)
            {
                root = reader.Type.PreferredXamlNamespace;
            }
        }

        Assert.Equal(Local, root);
    }

    [Fact]
    public void ContextAndRootDeclarationsTogetherBothApply()
    {
        // The root declares the standard namespaces itself; `local` comes only from the context.
        var text = $$"""<Root xmlns="{{Presentation}}" xmlns:x="{{Xaml}}" Tag="{x:Type local:Item}"><local:Child /></Root>""";

        var nodes = Nodes(ReaderWithContext(text, ("local", Local)));

        Assert.Contains($"object {Local}:Child", nodes);
    }

    [Fact]
    public void ADocumentThatDeclaresItsNamespacesReadsTheSameWithOrWithoutAnAmbientContext()
    {
        var plain = Nodes(PlainReader(OldStyle));
        var withContext = Nodes(ReaderWithContext(OldStyle));

        Assert.Equal(plain, withContext);
        Assert.Contains($"object {Presentation}:Root", plain);
        Assert.Contains($"ns x={Xaml}", plain);
        Assert.Contains($"ns local={Local}", plain);
    }

    [Fact]
    public void TheGivenReaderIsNotConsumedAndRebuilt()
    {
        // Rebuilding a reader from ReadOuterXml loses its namespace context and its position information.
        var counting = new CountingReader(new StringReader(OldStyle));

        _ = Nodes(counting);

        Assert.Equal(0, counting.ReadOuterXmlCalls);
    }

    [Fact]
    public void ErrorsReportTheLineNumbersOfTheOriginalDocument()
    {
        var text = $$"""
            <Root xmlns="{{Presentation}}"
                  xmlns:x="{{Xaml}}">
              <Child Tag="{x:Type" />
            </Root>
            """;

        var ex = Assert.ThrowsAny<XamlParseException>(() => Nodes(PlainReader(text)));

        Assert.Equal(3, ex.LineNumber);
    }

    [Fact]
    public void ADocumentWithPartialDeclarationsIsLeftAsItIs()
    {
        // Whatever the author declared is what the reader sees: nothing is added to a root that declares some namespaces.
        var nodes = Nodes(PlainReader("<Root xmlns:local=\"" + Local + "\"><local:Child /></Root>"));

        Assert.Contains("object :Root", nodes);
        Assert.Contains($"object {Local}:Child", nodes);
    }

    [Fact]
    public void ADocumentWithNoDeclarationsIsAnErrorAsInStockWpf()
    {
        // Only the XAML source generator (at build time) understands a root without xmlns. The run-time reader is stock WPF's: an
        // undeclared prefix is an error, reported against the document.
        var ex = Assert.ThrowsAny<Exception>(() => Nodes(PlainReader("<Root x:Name=\"r\"><Child /></Root>")));

        Assert.True(ex is XmlException || ex is XamlParseException, ex.GetType().Name);
    }

    /// <summary>Counts ReadOuterXml, the call that consumes a reader so it can be rebuilt from its text.</summary>
    private sealed class CountingReader(TextReader text) : XmlTextReader(text)
    {
        public int ReadOuterXmlCalls { get; private set; }

        public override string ReadOuterXml()
        {
            ReadOuterXmlCalls++;
            return base.ReadOuterXml();
        }
    }
}
