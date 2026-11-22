// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.IO;
using System.Text;
using System.Xaml.MS.Impl;
using System.Xaml.Schema;
using System.Xml;
using System.Xml.Linq;
using MS.Internal.Xaml;
using MS.Internal.Xaml.Context;
using MS.Internal.Xaml.Parser;

namespace System.Xaml
{
    public class XamlXmlReader : XamlReader, IXamlLineInfo
    {
        private XamlParserContext _context;
        private IEnumerator<XamlNode> _nodeStream;

        private XamlNode _current;
        private LineInfo _currentLineInfo;
        private XamlNode _endOfStreamNode;

        private XamlXmlReaderSettings _mergedSettings;

        public XamlXmlReader(XmlReader xmlReader)
        {
            ArgumentNullException.ThrowIfNull(xmlReader);

            Initialize(xmlReader, null, null);
        }

        public XamlXmlReader(XmlReader xmlReader, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(xmlReader);

            Initialize(xmlReader, null, settings);
        }

        public XamlXmlReader(XmlReader xmlReader, XamlSchemaContext schemaContext)
        {
            ArgumentNullException.ThrowIfNull(schemaContext);
            ArgumentNullException.ThrowIfNull(xmlReader);

            Initialize(xmlReader, schemaContext, null);
        }

        public XamlXmlReader(XmlReader xmlReader, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(schemaContext);
            ArgumentNullException.ThrowIfNull(xmlReader);

            Initialize(xmlReader, schemaContext, settings);
        }

        public XamlXmlReader(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            Initialize(CreateXmlReader(fileName, null), null, null);
        }

        public XamlXmlReader(string fileName, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(fileName);

            Initialize(CreateXmlReader(fileName, settings), null, settings);
        }

        public XamlXmlReader(string fileName, XamlSchemaContext schemaContext)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            ArgumentNullException.ThrowIfNull(schemaContext);

            Initialize(CreateXmlReader(fileName, null), schemaContext, null);
        }

        public XamlXmlReader(string fileName, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            ArgumentNullException.ThrowIfNull(schemaContext);

            Initialize(CreateXmlReader(fileName, settings), schemaContext, settings);
        }

        private XmlReader CreateXmlReader(string fileName, XamlXmlReaderSettings settings)
        {
            bool closeInput = (settings is null) ? true : settings.CloseInput;
            return XmlReader.Create(fileName, new XmlReaderSettings { CloseInput = closeInput, DtdProcessing = DtdProcessing.Prohibit });
        }

        public XamlXmlReader(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            Initialize(CreateXmlReader(stream, null), null, null);
        }

        public XamlXmlReader(Stream stream, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(stream);
            Initialize(CreateXmlReader(stream, settings), null, settings);
        }

        public XamlXmlReader(Stream stream, XamlSchemaContext schemaContext)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(schemaContext);

            Initialize(CreateXmlReader(stream, null), schemaContext, null);
        }

        public XamlXmlReader(Stream stream, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(schemaContext);

            Initialize(CreateXmlReader(stream, settings), schemaContext, settings);
        }

        private XmlReader CreateXmlReader(Stream stream, XamlXmlReaderSettings settings)
        {
            bool closeInput = (settings is not null) && settings.CloseInput;
            return XmlReader.Create(stream, new XmlReaderSettings { CloseInput = closeInput, DtdProcessing = DtdProcessing.Prohibit });
        }

        public XamlXmlReader(TextReader textReader)
        {
            ArgumentNullException.ThrowIfNull(textReader);
            Initialize(CreateXmlReader(textReader, null), null, null);
        }

        public XamlXmlReader(TextReader textReader, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(textReader);
            Initialize(CreateXmlReader(textReader, settings), null, settings);
        }

        public XamlXmlReader(TextReader textReader, XamlSchemaContext schemaContext)
        {
            ArgumentNullException.ThrowIfNull(textReader);
            ArgumentNullException.ThrowIfNull(schemaContext);

            Initialize(CreateXmlReader(textReader, null), schemaContext, null);
        }

        public XamlXmlReader(TextReader textReader, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(textReader);
            ArgumentNullException.ThrowIfNull(schemaContext);

            Initialize(CreateXmlReader(textReader, settings), schemaContext, settings);
        }

        private XmlReader CreateXmlReader(TextReader textReader, XamlXmlReaderSettings settings)
        {
            bool closeInput = (settings is not null) && settings.CloseInput;
            return XmlReader.Create(textReader, new XmlReaderSettings { CloseInput = closeInput, DtdProcessing = DtdProcessing.Prohibit });
        }

        private void Initialize(XmlReader givenXmlReader, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        {
            // Simpler XAML / implicit namespaces: when a XAML document's root element
            // omits the standard xmlns declarations, inject them before parsing so
            // loose XAML (XamlReader.Load, ResourceDictionary.Source, ...) supports
            // the same shorthand as compiled XAML.
            givenXmlReader = EnsureImplicitXmlns(givenXmlReader);

            XmlReader myXmlReader;

            _mergedSettings = (settings is null) ? new XamlXmlReaderSettings() : new XamlXmlReaderSettings(settings);
            // Wrap the xmlreader with a XmlCompatReader instance to apply MarkupCompat rules.
            if (!_mergedSettings.SkipXmlCompatibilityProcessing)
            {
                XmlCompatibilityReader mcReader =
                        new XmlCompatibilityReader(givenXmlReader,
                                new IsXmlNamespaceSupportedCallback(IsXmlNamespaceSupported)
                        )
                        {
                            Normalization = true
                        };
                myXmlReader = mcReader;
            }
            else
            {   // Don't wrap the xmlreader with XmlCompatReader.
                // Useful for uses where users want to keep mc: content in the XamlNode stream.
                // Or have already processed the markup compat and want that extra perf.
                myXmlReader = givenXmlReader;
            }

            // Pick up the XmlReader settings to override the "settings" defaults.
            if (!string.IsNullOrEmpty(myXmlReader.BaseURI))
            {
                _mergedSettings.BaseUri = new Uri(myXmlReader.BaseURI);
            }

            if (myXmlReader.XmlSpace == XmlSpace.Preserve)
            {
                _mergedSettings.XmlSpacePreserve = true;
            }

            if (!string.IsNullOrEmpty(myXmlReader.XmlLang))
            {
                _mergedSettings.XmlLang = myXmlReader.XmlLang;
            }

            Dictionary<string, string> xmlnsDictionary = null;
            if (myXmlReader is IXmlNamespaceResolver myXmlReaderNS)
            {
                IDictionary<string, string> rootNamespaces = myXmlReaderNS.GetNamespacesInScope(XmlNamespaceScope.Local);
                if (rootNamespaces is not null)
                {
                    foreach (KeyValuePair<string, string> ns in rootNamespaces)
                    {
                        if (xmlnsDictionary is null)
                        {
                            xmlnsDictionary = new Dictionary<string, string>();
                        }

                        xmlnsDictionary[ns.Key] = ns.Value;
                    }
                }
            }

            if (schemaContext is null)
            {
                schemaContext = new XamlSchemaContext();
            }

            _endOfStreamNode = new XamlNode(XamlNode.InternalNodeType.EndOfStream);

            _context = new XamlParserContext(schemaContext, _mergedSettings.LocalAssembly)
            {
                AllowProtectedMembersOnRoot = _mergedSettings.AllowProtectedMembersOnRoot
            };
            _context.AddNamespacePrefix(KnownStrings.XmlPrefix, XamlLanguage.Xml1998Namespace);

            Func<string, string> namespaceResolver = myXmlReader.LookupNamespace;
            _context.XmlNamespaceResolver = namespaceResolver;

            XamlScanner xamlScanner = new XamlScanner(_context, myXmlReader, _mergedSettings);
            XamlPullParser parser = new XamlPullParser(_context, xamlScanner, _mergedSettings);
            _nodeStream = new NodeStreamSorter(_context, parser, _mergedSettings, xmlnsDictionary);
            _current = new XamlNode(XamlNode.InternalNodeType.StartOfStream);  // user must call Read() before using properties.
            _currentLineInfo = new LineInfo(0, 0);
        }

        //
        // Simpler XAML / implicit namespaces (runtime).
        //
        // When the root element of the given XAML document omits the standard
        // xmlns declarations, rewrite it so the default WPF presentation and
        // xaml namespaces are injected before parsing. The original reader is
        // consumed in the process.
        //
        private static XmlReader EnsureImplicitXmlns(XmlReader given)
        {
            if (given is null)
            {
                return null;
            }

            try
            {
                given.MoveToContent();
            }
            catch
            {
                return given;
            }

            // Check whether injection is needed WITHOUT consuming the reader.
            try
            {
                given.MoveToContent();
            }
            catch
            {
                return given;
            }

            // Check if default namespace is already declared.
            string defaultNs = string.Empty;
            try
            {
                defaultNs = given.LookupNamespace(string.Empty) ?? string.Empty;
            }
            catch
            {
                defaultNs = string.Empty;
            }

            // Check if root element has xmlns attribute explicitly.
            bool hasXmlns = false;
            try
            {
                if (given.MoveToFirstAttribute())
                {
                    bool continueLoop = true;
                    while (continueLoop)
                    {
                        if (given.Name == "xmlns" || given.Name.StartsWith("xmlns:", StringComparison.Ordinal))
                        {
                            hasXmlns = true;
                            break;
                        }

                        continueLoop = given.MoveToNextAttribute();
                    }

                    given.MoveToElement();
                }
            }
            catch
            {
                // ignore
            }

            string baseUri = given.BaseURI;

            string rootXml;
            try
            {
                if (string.IsNullOrEmpty(defaultNs) && !hasXmlns)
                {
                    // Need injection; reader already at root element after MoveToContent.
                    rootXml = given.ReadOuterXml();
                }
                else
                {
                    // No injection needed; consume to recreate a fresh reader (preserves state consistency).
                    rootXml = given.ReadOuterXml();
                }
            }
            catch
            {
                return given;
            }

            if (string.IsNullOrEmpty(rootXml))
            {
                return given;
            }

            // Determine whether the root start tag already declares a default xmlns.
            int tagEnd = FindTagEnd(rootXml, 0);
            if (tagEnd < 0)
            {
                return CreateReader(rootXml, baseUri);
            }

            string startTag = rootXml.Substring(0, tagEnd + 1);
            if (startTag.Contains("xmlns", StringComparison.Ordinal))
            {
                return CreateReader(rootXml, baseUri);
            }

            // Inject the default namespaces before the closing '>' of the start tag.
            int insertPos = tagEnd;
            if (insertPos > 0 && rootXml[insertPos - 1] == '/')
            {
                insertPos--;
            }

            const string injection = " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
#pragma warning disable CA1845
            string rewritten = rootXml.Substring(0, insertPos) + injection + rootXml.Substring(insertPos);
#pragma warning restore CA1845

            // Preserve any trailing content after the root element? ReadOuterXml returns only the
            // root element, so nothing else to append.
            return CreateReader(rewritten, baseUri);
        }

        private static int FindTagEnd(string text, int start)
        {
            int i = start;
            int len = text.Length;
            // Skip leading whitespace
            while (i < len && char.IsWhiteSpace(text[i])) i++;
            if (i >= len || text[i] != '<') return -1;

            // Skip comments / PI / doctype
            if (i + 3 < len && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
            {
                int end = text.IndexOf("-->", i, StringComparison.Ordinal);
                if (end < 0) return -1;
                return FindTagEnd(text, end + 3);
            }

            if (i + 1 < len && text[i + 1] == '?')
            {
                int end = text.IndexOf("?>", i, StringComparison.Ordinal);
                if (end < 0) return -1;
                return FindTagEnd(text, end + 2);
            }

            if (i + 1 < len && text[i + 1] == '!')
            {
                int end = text.IndexOf('>', i);
                if (end < 0) return -1;
                return FindTagEnd(text, end + 1);
            }

            int j = i;
            bool inDouble = false;
            bool inSingle = false;
            while (j < len)
            {
                char c = text[j];
                if (inDouble)
                {
                    if (c == '"') inDouble = false;
                }
                else if (inSingle)
                {
                    if (c == '\'') inSingle = false;
                }
                else
                {
                    if (c == '"') inDouble = true;
                    else if (c == '\'') inSingle = true;
                    else if (c == '>') break;
                }

                j++;
            }

            if (j >= len) return -1;
            return j;
        }

        private static XmlReader CreateReader(string xml, string baseUri)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                CloseInput = false
            };

            if (string.IsNullOrEmpty(baseUri))
            {
                return XmlReader.Create(new StringReader(xml), settings);
            }

            XmlParserContext ctx = new XmlParserContext(
                null, null, "", "", "", "", baseUri, null, XmlSpace.None);
            return XmlReader.Create(new StringReader(xml), settings, ctx);
        }

        #region XamlReader Members

        public override bool Read()
        {
            ThrowIfDisposed();
            do
            {
                if (_nodeStream.MoveNext())
                {
                    _current = _nodeStream.Current;
                    if (_current.NodeType == XamlNodeType.None)
                    {
                        if (_current.LineInfo is not null)
                        {
                            _currentLineInfo = _current.LineInfo;
                        }
                        else if (_current.IsEof)
                        {
                            break;
                        }
                        else
                        {
                            Debug.Assert(_current.IsEof, "Xaml Parser returned an illegal internal XamlNode");
                        }
                    }
                }
                else
                {
                    _current = _endOfStreamNode;
                    break;
                }
            }
            while (_current.NodeType == XamlNodeType.None);
            return !IsEof;
        }

        public override XamlNodeType NodeType
        {
            get { return _current.NodeType; }
        }

        public override bool IsEof
        {
            get { return _current.IsEof; }
        }

        public override NamespaceDeclaration Namespace
        {
            get { return _current.NamespaceDeclaration; }
        }

        public override XamlType Type
        {
            get { return _current.XamlType; }
        }

        public override object Value
        {
            get { return _current.Value; }
        }

        public override XamlMember Member
        {
            get { return _current.Member; }
        }

        public override XamlSchemaContext SchemaContext
        {
            get { return _context.SchemaContext; }
        }

        #endregion

        #region IXamlLineInfo Members

        public bool HasLineInfo
        {
            get { return _mergedSettings.ProvideLineInfo; }
        }

        public int LineNumber
        {
            get { return _currentLineInfo.LineNumber; }
        }

        public int LinePosition
        {
            get { return _currentLineInfo.LinePosition; }
        }

        #endregion

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, typeof(XamlXmlReader));
        }

        // Return true if the passed namespace is known, meaning that it maps
        // to a set of assemblies and clr namespaces
        internal bool IsXmlNamespaceSupported(string xmlNamespace, out string newXmlNamespace)
        {
            // 4 cases: refer to Framework\XamlParser.IsXmlNamespaceSupported
            // startins with clr-namespace:
            // Namespace is known
            // http://schemas.microsoft.com/winfx/2006/xaml/presentation/options
            // We're inside of a XmlDataIsland

            // First, substitute in the LocalAssembly if needed
            if (_mergedSettings.LocalAssembly is not null)
            {
                string clrNs, assemblyName;
                if (ClrNamespaceUriParser.TryParseUri(xmlNamespace, out clrNs, out assemblyName) &&
                    string.IsNullOrEmpty(assemblyName))
                {
                    assemblyName = _mergedSettings.LocalAssembly.FullName;
                    newXmlNamespace = ClrNamespaceUriParser.GetUri(clrNs, assemblyName);
                    return true;
                }
            }

            bool result = _context.SchemaContext.TryGetCompatibleXamlNamespace(xmlNamespace, out newXmlNamespace);
            if (newXmlNamespace is null)
            {
                newXmlNamespace = string.Empty;
            }

            // we need to treat all namespaces inside of XmlDataIslands as Supported.
            // we need to tree Freeze as known, if it is around... don't hardcode.
            // else if (xmlNamespace == XamlReaderHelper.PresentationOptionsNamespaceURI)
            // {
            //    // PresentationOptions is expected to be marked as 'ignorable' in most Xaml
            //    // so that other Xaml parsers don't have to interpret it, but this parser
            //    // does handle it to support it's Freeze attribute.
            //    return true;
            // }
            return result;
        }
    }
}
