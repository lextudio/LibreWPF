// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using ProGpuSfntFontFace = ProGPU.Text.SfntFontFace;

namespace MS.Internal.Text.TextInterface
{
    internal enum FactoryType
    {
        Shared,
        Isolated
    }

    internal enum FontWeight
    {
        Thin = 100,
        ExtraLight = 200,
        UltraLight = 200,
        Light = 300,
        Normal = 400,
        Regular = 400,
        Medium = 500,
        DemiBold = 600,
        SemiBOLD = 600,
        Bold = 700,
        ExtraBold = 800,
        UltraBold = 800,
        Black = 900,
        Heavy = 900,
        ExtraBlack = 950,
        UltraBlack = 950
    }

    internal enum FontStyle
    {
        Normal = 0,
        Oblique = 1,
        Italic = 2
    }

    internal enum FontStretch
    {
        Undefined = 0,
        UltraCondensed = 1,
        ExtraCondensed = 2,
        Condensed = 3,
        SemiCondensed = 4,
        Normal = 5,
        Medium = 5,
        SemiExpanded = 6,
        Expanded = 7,
        ExtraExpanded = 8,
        UltraExpanded = 9
    }

    [Flags]
    internal enum FontSimulations
    {
        None = 0x0000,
        Bold = 0x0001,
        Oblique = 0x0002
    }

    internal enum FontFaceType
    {
        CFF,
        TrueType,
        TrueTypeCollection,
        Type1,
        Vector,
        Bitmap,
        Unknown
    }

    internal enum OpenTypeTableTag
    {
        TTO_GSUB = 0x47535542,
        TTO_GPOS = 0x47504F53,
        TTO_GDEF = 0x47444546
    }

    internal enum InformationalStringID
    {
        CopyrightNotice,
        VersionStrings,
        Trademark,
        Manufacturer,
        Designer,
        DesignerURL,
        Description,
        FontVendorURL,
        LicenseDescription,
        SampleText,
        Win32SubFamilyNames,
        WIN32FamilyNames,
        PreferredSubFamilyNames,
        PreferredFamilyNames
    }

    internal enum DWriteFontFeatureTag
    {
        AlternateAnnotationForms,
        AlternateHalfWidth,
        AlternativeFractions,
        CapitalSpacing,
        CaseSensitiveForms,
        ContextualAlternates,
        ContextualLigatures,
        ContextualSwash,
        DiscretionaryLigatures,
        ExpertForms,
        Fractions,
        FullWidth,
        HalfWidth,
        HistoricalForms,
        HistoricalLigatures,
        HojoKanjiForms,
        JIS04Forms,
        JIS78Forms,
        JIS83Forms,
        JIS90Forms,
        Kerning,
        LiningFigures,
        MathematicalGreek,
        NLCKanjiForms,
        OldStyleFigures,
        Ordinals,
        PetiteCapitals,
        PetiteCapitalsFromCapitals,
        ProportionalAlternateWidth,
        ProportionalFigures,
        ProportionalWidths,
        QuarterWidths,
        RubyNotationForms,
        ScientificInferiors,
        SimplifiedForms,
        SlashedZero,
        SmallCapitals,
        SmallCapitalsFromCapitals,
        StandardLigatures,
        StylisticAlternates,
        StylisticSet1,
        StylisticSet2,
        StylisticSet3,
        StylisticSet4,
        StylisticSet5,
        StylisticSet6,
        StylisticSet7,
        StylisticSet8,
        StylisticSet9,
        StylisticSet10,
        StylisticSet11,
        StylisticSet12,
        StylisticSet13,
        StylisticSet14,
        StylisticSet15,
        StylisticSet16,
        StylisticSet17,
        StylisticSet18,
        StylisticSet19,
        StylisticSet20,
        Subscript,
        Superscript,
        Swash,
        TabularFigures,
        ThirdWidths,
        Titling,
        TraditionalForms,
        TraditionalNameForms,
        Unicase
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DWriteFontFeature
    {
        internal DWriteFontFeatureTag nameTag;
        internal uint parameter;

        internal DWriteFontFeature(DWriteFontFeatureTag nameTag, uint parameter)
        {
            this.nameTag = nameTag;
            this.parameter = parameter;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphOffset
    {
        internal int du;
        internal int dv;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphMetrics
    {
        internal int LeftSideBearing;
        internal uint AdvanceWidth;
        internal int RightSideBearing;
        internal int TopSideBearing;
        internal uint AdvanceHeight;
        internal int BottomSideBearing;
        internal int VerticalOriginY;
    }

    internal sealed class FontMetrics
    {
        internal ushort DesignUnitsPerEm = 1;
        internal ushort Ascent;
        internal ushort Descent;
        internal short LineGap;
        internal ushort CapHeight;
        internal ushort XHeight;
        internal short UnderlinePosition;
        internal ushort UnderlineThickness;
        internal short StrikethroughPosition;
        internal ushort StrikethroughThickness;

        internal double Baseline => DesignUnitsPerEm == 0 ? 0 : (Ascent + LineGap * 0.5) / DesignUnitsPerEm;

        internal double LineSpacing => DesignUnitsPerEm == 0 ? 0 : (double)(Ascent + Descent + LineGap) / DesignUnitsPerEm;
    }

    internal interface IClassification
    {
        void GetCharAttribute(
            int unicodeScalar,
            out bool isCombining,
            out bool needsCaretInfo,
            out bool isIndic,
            out bool isDigit,
            out bool isLatin,
            out bool isStrong);
    }

    internal interface IFontSource
    {
        bool IsFile { get; }
        bool IsComposite { get; }
        Uri Uri { get; }
        bool IsAppSpecific { get; }
        string GetUriString();
        string ToStringUpperInvariant();
        DateTime GetLastWriteTimeUtc();
        UnmanagedMemoryStream GetUnmanagedStream();
        void TestFileOpenable();
        Stream GetStream();
    }

    internal interface IFontSourceFactory
    {
        IFontSource Create(string uriString);
    }

    internal static class LocalizedErrorMsgs
    {
        internal static string EnumeratorNotStarted { get; set; }
        internal static string EnumeratorReachedEnd { get; set; }
    }

    internal sealed class LocalizedStrings : Dictionary<CultureInfo, string>
    {
        internal uint StringsCount => checked((uint)Count);

        internal bool FindLocaleName(string localeName, out uint index)
        {
            uint current = 0;
            foreach (CultureInfo cultureInfo in Keys)
            {
                if (string.Equals(cultureInfo.Name, localeName, StringComparison.OrdinalIgnoreCase))
                {
                    index = current;
                    return true;
                }

                current++;
            }

            index = uint.MaxValue;
            return false;
        }

        internal string GetLocaleName(uint index)
        {
            return GetAt(index).Key.Name;
        }

        internal string GetString(uint index)
        {
            return GetAt(index).Value;
        }

        private KeyValuePair<CultureInfo, string> GetAt(uint index)
        {
            uint current = 0;
            foreach (KeyValuePair<CultureInfo, string> pair in this)
            {
                if (current == index)
                {
                    return pair;
                }

                current++;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    internal sealed unsafe class Factory
    {
        private readonly IFontSourceCollectionFactory _fontSourceCollectionFactory;
        private readonly IFontSourceFactory _fontSourceFactory;
        private FontCollection _systemFontCollection;
        private readonly object _systemFontCollectionLock = new object();

        private Factory(IFontSourceCollectionFactory fontSourceCollectionFactory, IFontSourceFactory fontSourceFactory)
        {
            _fontSourceCollectionFactory = fontSourceCollectionFactory;
            _fontSourceFactory = fontSourceFactory;
        }

        internal Native.IDWriteFactory* DWriteFactory => null;

        internal static Factory Create(
            FactoryType factoryType,
            IFontSourceCollectionFactory fontSourceCollectionFactory,
            IFontSourceFactory fontSourceFactory)
        {
            return new Factory(fontSourceCollectionFactory, fontSourceFactory);
        }

        internal FontFile CreateFontFile(Uri filePathUri)
        {
            CreateFontSource(filePathUri)?.TestFileOpenable();
            return new FontFile(filePathUri);
        }

        internal FontFace CreateFontFace(Uri filePathUri, uint faceIndex)
        {
            return CreateFontFace(filePathUri, faceIndex, FontSimulations.None);
        }

        internal FontFace CreateFontFace(Uri filePathUri, uint faceIndex, FontSimulations fontSimulationFlags)
        {
            PortableFontData fontData = PortableFontData.LoadFace(filePathUri, CreateFontSource(filePathUri), faceIndex);
            Font font = Font.CreateStandalone(fontData, fontSimulationFlags);
            return font.GetFontFace();
        }

        internal FontCollection GetSystemFontCollection()
        {
            if (_systemFontCollection == null)
            {
                lock (_systemFontCollectionLock)
                {
                    if (_systemFontCollection == null)
                    {
                        _systemFontCollection = FontCollection.FromUris(GetSystemFontUris(), _fontSourceFactory);
                    }
                }
            }

            return _systemFontCollection;
        }

        internal FontCollection GetFontCollection(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            IFontSourceCollection fontSources = _fontSourceCollectionFactory?.Create(uri.AbsoluteUri);
            if (fontSources != null)
            {
                return FontCollection.FromFontSources(fontSources);
            }

            return FontCollection.FromUris(new[] { uri }, _fontSourceFactory);
        }

        internal TextAnalyzer CreateTextAnalyzer()
        {
            return new TextAnalyzer();
        }

        internal static bool IsLocalUri(Uri uri)
        {
            return uri.IsFile && uri.IsLoopback && !uri.IsUnc;
        }

        private IFontSource CreateFontSource(Uri uri)
        {
            return _fontSourceFactory?.Create(uri.AbsoluteUri);
        }

        private static IEnumerable<Uri> GetSystemFontUris()
        {
            foreach (string directory in GetSystemFontDirectories())
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (PortableFontData.IsSupportedFontPath(file))
                    {
                        yield return new Uri(file, UriKind.Absolute);
                    }
                }
            }
        }

        private static IEnumerable<string> GetSystemFontDirectories()
        {
            if (OperatingSystem.IsMacOS())
            {
                yield return "/System/Library/Fonts";
                yield return "/System/Library/Fonts/Supplemental";
                yield return "/Library/Fonts";
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Fonts");
            }
            else if (OperatingSystem.IsLinux())
            {
                yield return "/usr/share/fonts";
                yield return "/usr/local/share/fonts";
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts");
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share",
                    "fonts");
            }
            else if (OperatingSystem.IsWindows())
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            }
        }
    }

    internal sealed class FontCollection
    {
        internal static readonly FontCollection Empty = new FontCollection(Array.Empty<FontFamily>());

        private readonly IReadOnlyList<FontFamily> _families;

        internal FontCollection(IReadOnlyList<FontFamily> families)
        {
            _families = families;
        }

        internal static FontCollection FromUris(IEnumerable<Uri> uris, IFontSourceFactory fontSourceFactory)
        {
            List<PortableFontData> fonts = new List<PortableFontData>();

            foreach (Uri uri in uris)
            {
                IFontSource fontSource = fontSourceFactory?.Create(uri.AbsoluteUri);
                AddFontsFromSource(fontSource, uri, fonts);
            }

            return FromFontData(fonts);
        }

        internal static FontCollection FromFontSources(IEnumerable<IFontSource> fontSources)
        {
            List<PortableFontData> fonts = new List<PortableFontData>();

            foreach (IFontSource fontSource in fontSources)
            {
                if (fontSource.IsComposite)
                {
                    continue;
                }

                AddFontsFromSource(fontSource, fontSource.Uri, fonts);
            }

            return FromFontData(fonts);
        }

        private static void AddFontsFromSource(IFontSource fontSource, Uri uri, List<PortableFontData> fonts)
        {
            try
            {
                fonts.AddRange(PortableFontData.LoadFaces(uri, fontSource));
            }
            catch (Exception ex) when (ex is FileFormatException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
            }
        }

        private static FontCollection FromFontData(IEnumerable<PortableFontData> fontData)
        {
            Dictionary<string, List<Font>> familyMap = new Dictionary<string, List<Font>>(StringComparer.OrdinalIgnoreCase);

            foreach (PortableFontData data in fontData)
            {
                string familyName = data.FamilyName;
                if (!familyMap.TryGetValue(familyName, out List<Font> familyFonts))
                {
                    familyFonts = new List<Font>();
                    familyMap.Add(familyName, familyFonts);
                }

                familyFonts.Add(new Font(data, FontSimulations.None));
            }

            List<FontFamily> families = new List<FontFamily>(familyMap.Count);
            foreach (KeyValuePair<string, List<Font>> pair in familyMap)
            {
                LocalizedStrings familyNames = pair.Value.Count > 0
                    ? pair.Value[0].FontData.GetNameStrings(PortableFontData.NameIdPreferredFamily, PortableFontData.NameIdFamily, pair.Key)
                    : PortableFontData.CreateInvariantStrings(pair.Key);

                FontFamily family = new FontFamily(pair.Key, familyNames, pair.Value);
                foreach (Font font in pair.Value)
                {
                    font.SetFamily(family);
                }

                families.Add(family);
            }

            families.Sort((left, right) => string.Compare(left.OrdinalName, right.OrdinalName, StringComparison.OrdinalIgnoreCase));
            return families.Count == 0 ? Empty : new FontCollection(families);
        }

        internal uint FamilyCount => checked((uint)_families.Count);

        internal FontFamily this[uint familyIndex] => _families[checked((int)familyIndex)];

        internal FontFamily this[string familyName]
        {
            get
            {
                return FindFamilyName(familyName, out uint index) ? this[index] : null;
            }
        }

        internal bool FindFamilyName(string familyName, out uint index)
        {
            for (int i = 0; i < _families.Count; i++)
            {
                if (string.Equals(_families[i].OrdinalName, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    index = checked((uint)i);
                    return true;
                }
            }

            index = uint.MaxValue;
            return false;
        }

        internal Font GetFontFromFontFace(FontFace fontFace)
        {
            ArgumentNullException.ThrowIfNull(fontFace);

            Font faceFont = fontFace.Font;
            if (faceFont != null)
            {
                return faceFont;
            }

            for (int familyIndex = 0; familyIndex < _families.Count; familyIndex++)
            {
                foreach (Font font in _families[familyIndex])
                {
                    if (font.FontData.HasSameSource(fontFace.FontData))
                    {
                        return font;
                    }
                }
            }

            return Font.CreateStandalone(fontFace.FontData, fontFace.SimulationFlags);
        }
    }

    internal sealed class FontFamily : IEnumerable<Font>
    {
        private readonly IReadOnlyList<Font> _fonts;

        internal FontFamily(string ordinalName, LocalizedStrings familyNames, IReadOnlyList<Font> fonts)
        {
            OrdinalName = ordinalName;
            FamilyNames = familyNames;
            _fonts = fonts;
        }

        internal LocalizedStrings FamilyNames { get; }

        internal bool IsPhysical => true;

        internal bool IsComposite => false;

        internal string OrdinalName { get; }

        internal uint Count => checked((uint)_fonts.Count);

        internal FontMetrics Metrics => _fonts.Count == 0 ? new FontMetrics() : _fonts[0].Metrics;

        internal FontMetrics DisplayMetrics(float emSize, float pixelsPerDip)
        {
            return Metrics;
        }

        internal Font GetFirstMatchingFont(FontWeight weight, FontStretch stretch, FontStyle style)
        {
            if (_fonts.Count == 0)
            {
                return null;
            }

            Font bestFont = _fonts[0];
            int bestScore = int.MaxValue;

            foreach (Font font in _fonts)
            {
                int score = Math.Abs((int)font.Weight - (int)weight) * 2
                    + Math.Abs((int)font.Stretch - (int)stretch) * 25
                    + (font.Style == style ? 0 : 1000);

                if (score < bestScore)
                {
                    bestFont = font;
                    bestScore = score;
                }
            }

            return bestFont;
        }

        public IEnumerator<Font> GetEnumerator()
        {
            return _fonts.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal sealed class Font
    {
        private FontFamily _family;
        private readonly FontSimulations _simulationFlags;

        internal Font(PortableFontData fontData, FontSimulations simulationFlags)
        {
            FontData = fontData;
            _simulationFlags = simulationFlags;
            FaceNames = fontData.GetNameStrings(PortableFontData.NameIdPreferredSubfamily, PortableFontData.NameIdSubfamily, fontData.FaceName);
        }

        internal PortableFontData FontData { get; }

        internal FontFamily Family => _family;

        internal FontWeight Weight => FontData.Weight;

        internal FontStretch Stretch => FontData.Stretch;

        internal FontStyle Style => FontData.Style;

        internal bool IsSymbolFont => FontData.IsSymbolFont;

        internal LocalizedStrings FaceNames { get; }

        internal FontSimulations SimulationFlags => _simulationFlags;

        internal FontMetrics Metrics => FontData.Metrics;

        internal double Version => FontData.Version;

        internal IntPtr DWriteFontAddRef => IntPtr.Zero;

        internal bool HasCharacter(uint unicodeScalar)
        {
            return FontData.GetGlyphIndex(unicodeScalar) != 0;
        }

        internal FontMetrics DisplayMetrics(float emSize, float pixelsPerDip)
        {
            return Metrics;
        }

        internal static void ResetFontFaceCache()
        {
        }

        internal FontFace GetFontFace()
        {
            return new FontFace(this, FontData, _simulationFlags);
        }

        internal bool GetInformationalStrings(InformationalStringID informationalStringID, out LocalizedStrings localizedStrings)
        {
            return FontData.TryGetInformationalStrings(informationalStringID, out localizedStrings);
        }

        internal void SetFamily(FontFamily family)
        {
            _family = family;
        }

        internal static Font CreateStandalone(PortableFontData fontData, FontSimulations simulationFlags)
        {
            Font font = new Font(fontData, simulationFlags);
            FontFamily family = new FontFamily(
                fontData.FamilyName,
                fontData.GetNameStrings(PortableFontData.NameIdPreferredFamily, PortableFontData.NameIdFamily, fontData.FamilyName),
                new[] { font });

            font.SetFamily(family);
            return font;
        }
    }

    internal sealed unsafe class FontFace : IDisposable
    {
        private readonly Font _font;
        private readonly PortableFontData _fontData;
        private readonly FontSimulations _simulationFlags;

        internal FontFace(Font font, PortableFontData fontData, FontSimulations simulationFlags)
        {
            _font = font;
            _fontData = fontData;
            _simulationFlags = simulationFlags;
        }

        internal Font Font => _font;

        internal PortableFontData FontData => _fontData;

        internal FontFaceType Type => _fontData.FaceType;

        internal uint Index => _fontData.FaceIndex;

        internal FontSimulations SimulationFlags => _simulationFlags;

        internal bool IsSymbolFont => _fontData.IsSymbolFont;

        internal FontMetrics Metrics => _fontData.Metrics;

        internal ushort GlyphCount => _fontData.GlyphCount;

        internal IntPtr DWriteFontFaceAddRef => IntPtr.Zero;

        internal FontFile GetFileZero()
        {
            return new FontFile(_fontData.SourceUri);
        }

        internal void AddRef()
        {
        }

        internal void Release()
        {
            Dispose();
        }

        internal void GetDesignGlyphMetrics(ushort* pGlyphIndices, uint glyphCount, GlyphMetrics* pGlyphMetrics)
        {
            for (uint i = 0; i < glyphCount; i++)
            {
                pGlyphMetrics[i] = _fontData.GetGlyphMetrics(pGlyphIndices[i]);
            }
        }

        internal void GetDisplayGlyphMetrics(
            ushort* pGlyphIndices,
            uint glyphCount,
            GlyphMetrics* pGlyphMetrics,
            float emSize,
            bool useDisplayNatural,
            bool isSideways,
            float pixelsPerDip)
        {
            GetDesignGlyphMetrics(pGlyphIndices, glyphCount, pGlyphMetrics);
        }

        internal void GetArrayOfGlyphIndices(uint* pCodePoints, uint glyphCount, ushort* pGlyphIndices)
        {
            for (uint i = 0; i < glyphCount; i++)
            {
                pGlyphIndices[i] = _fontData.GetGlyphIndex(pCodePoints[i]);
            }
        }

        internal bool TryGetFontTable(OpenTypeTableTag openTypeTableTag, out byte[] tableData)
        {
            return _fontData.TryGetTable((uint)openTypeTableTag, out tableData);
        }

        internal bool ReadFontEmbeddingRights(out ushort fsType)
        {
            return _fontData.TryGetEmbeddingRights(out fsType);
        }

        public void Dispose()
        {
        }
    }

    internal sealed class PortableFontData
    {
        internal const ushort NameIdCopyright = 0;
        internal const ushort NameIdFamily = 1;
        internal const ushort NameIdSubfamily = 2;
        internal const ushort NameIdUniqueIdentifier = 3;
        internal const ushort NameIdFullName = 4;
        internal const ushort NameIdVersion = 5;
        internal const ushort NameIdTrademark = 7;
        internal const ushort NameIdManufacturer = 8;
        internal const ushort NameIdDesigner = 9;
        internal const ushort NameIdDescription = 10;
        internal const ushort NameIdVendorUrl = 11;
        internal const ushort NameIdDesignerUrl = 12;
        internal const ushort NameIdLicense = 13;
        internal const ushort NameIdSampleText = 19;
        internal const ushort NameIdPreferredFamily = 16;
        internal const ushort NameIdPreferredSubfamily = 17;

        private const uint TagTrueTypeCollection = 0x74746366;
        private const uint TagHead = 0x68656164;
        private const uint TagHhea = 0x68686561;
        private const uint TagMaxp = 0x6D617870;
        private const uint TagHmtx = 0x686D7478;
        private const uint TagCmap = 0x636D6170;
        private const uint TagName = 0x6E616D65;
        private const uint TagOs2 = 0x4F532F32;
        private const uint TagPost = 0x706F7374;
        private const uint TagLoca = 0x6C6F6361;
        private const uint TagGlyf = 0x676C7966;
        private const uint TagCff = 0x43464620;

        private readonly byte[] _data;
        private readonly ProGpuSfntFontFace _sfntFace;
        private readonly Dictionary<uint, TableRecord> _tables = new Dictionary<uint, TableRecord>();
        private readonly Dictionary<ushort, LocalizedStrings> _nameStrings = new Dictionary<ushort, LocalizedStrings>();
        private readonly uint _faceOffset;
        private readonly bool _isCollection;
        private readonly ushort _numberOfHMetrics;
        private readonly short _indexToLocFormat;
        private readonly ushort _fsType;
        private readonly ushort[] _format4EndCodes = Array.Empty<ushort>();
        private readonly ushort[] _format4StartCodes = Array.Empty<ushort>();
        private readonly short[] _format4Deltas = Array.Empty<short>();
        private readonly ushort[] _format4RangeOffsets = Array.Empty<ushort>();
        private readonly uint _format4RangeOffsetsTableOffset;
        private readonly uint[] _format12StartCodes = Array.Empty<uint>();
        private readonly uint[] _format12EndCodes = Array.Empty<uint>();
        private readonly uint[] _format12StartGlyphIds = Array.Empty<uint>();

        private PortableFontData(byte[] data, Uri sourceUri, uint faceIndex, uint faceOffset, bool isCollection)
        {
            _data = data;
            SourceUri = sourceUri;
            FaceIndex = faceIndex;
            _faceOffset = faceOffset;
            _isCollection = isCollection;
            _sfntFace = ProGpuSfntFontFace.Load(data, checked((int)faceIndex));

            ParseTableDirectory();
            ParseNames();

            FamilyName = GetFirstName(NameIdPreferredFamily, NameIdFamily)
                ?? Path.GetFileNameWithoutExtension(SourceUri.IsFile ? SourceUri.LocalPath : SourceUri.AbsoluteUri);
            FaceName = GetFirstName(NameIdPreferredSubfamily, NameIdSubfamily) ?? "Regular";
            FullName = GetFirstName(NameIdFullName) ?? string.Concat(FamilyName, " ", FaceName).Trim();

            (Metrics, _numberOfHMetrics, _indexToLocFormat, Version) = ParseMetrics();
            (Weight, Stretch, Style, _fsType) = ParseOs2AndStyle();
            FaceType = _isCollection ? FontFaceType.TrueTypeCollection : (_tables.ContainsKey(TagCff) ? FontFaceType.CFF : FontFaceType.TrueType);
            GlyphCount = ParseGlyphCount();

            CmapData cmapData = ParseCmap();
            _format4EndCodes = cmapData.Format4EndCodes;
            _format4StartCodes = cmapData.Format4StartCodes;
            _format4Deltas = cmapData.Format4Deltas;
            _format4RangeOffsets = cmapData.Format4RangeOffsets;
            _format4RangeOffsetsTableOffset = cmapData.Format4RangeOffsetsTableOffset;
            _format12StartCodes = cmapData.Format12StartCodes;
            _format12EndCodes = cmapData.Format12EndCodes;
            _format12StartGlyphIds = cmapData.Format12StartGlyphIds;
            IsSymbolFont = cmapData.IsSymbolFont;
        }

        internal Uri SourceUri { get; }

        internal uint FaceIndex { get; }

        internal string FamilyName { get; }

        internal string FaceName { get; }

        internal string FullName { get; }

        internal FontMetrics Metrics { get; }

        internal FontWeight Weight { get; }

        internal FontStretch Stretch { get; }

        internal FontStyle Style { get; }

        internal FontFaceType FaceType { get; }

        internal ushort GlyphCount { get; }

        internal bool IsSymbolFont { get; }

        internal double Version { get; }

        internal static bool IsSupportedFontPath(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ttc", StringComparison.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<PortableFontData> LoadFaces(Uri uri, IFontSource fontSource)
        {
            byte[] data = ReadFontBytes(uri, fontSource);
            List<uint> faceOffsets = GetFaceOffsets(data);
            List<PortableFontData> faces = new List<PortableFontData>(faceOffsets.Count);
            bool isCollection = ReadUInt(data, 0) == TagTrueTypeCollection;

            for (int i = 0; i < faceOffsets.Count; i++)
            {
                faces.Add(CreateFace(data, uri, checked((uint)i), faceOffsets[i], isCollection));
            }

            return faces;
        }

        internal static PortableFontData LoadFace(Uri uri, IFontSource fontSource, uint faceIndex)
        {
            byte[] data = ReadFontBytes(uri, fontSource);
            List<uint> faceOffsets = GetFaceOffsets(data);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(faceIndex, checked((uint)faceOffsets.Count), nameof(faceIndex));

            bool isCollection = ReadUInt(data, 0) == TagTrueTypeCollection;
            return CreateFace(data, uri, faceIndex, faceOffsets[checked((int)faceIndex)], isCollection);
        }

        internal static LocalizedStrings CreateInvariantStrings(string value)
        {
            LocalizedStrings strings = new LocalizedStrings();
            if (!string.IsNullOrEmpty(value))
            {
                strings[CultureInfo.InvariantCulture] = value;
            }

            return strings;
        }

        internal bool HasSameSource(PortableFontData other)
        {
            return other != null
                && FaceIndex == other.FaceIndex
                && string.Equals(SourceUri.AbsoluteUri, other.SourceUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        }

        internal LocalizedStrings GetNameStrings(ushort preferredNameId, ushort fallbackNameId, string fallback)
        {
            if (_nameStrings.TryGetValue(preferredNameId, out LocalizedStrings preferred) && preferred.Count > 0)
            {
                return preferred;
            }

            if (_nameStrings.TryGetValue(fallbackNameId, out LocalizedStrings fallbackStrings) && fallbackStrings.Count > 0)
            {
                return fallbackStrings;
            }

            return CreateInvariantStrings(fallback);
        }

        internal bool TryGetInformationalStrings(InformationalStringID informationalStringID, out LocalizedStrings localizedStrings)
        {
            ushort nameId = informationalStringID switch
            {
                InformationalStringID.CopyrightNotice => NameIdCopyright,
                InformationalStringID.VersionStrings => NameIdVersion,
                InformationalStringID.Trademark => NameIdTrademark,
                InformationalStringID.Manufacturer => NameIdManufacturer,
                InformationalStringID.Designer => NameIdDesigner,
                InformationalStringID.DesignerURL => NameIdDesignerUrl,
                InformationalStringID.Description => NameIdDescription,
                InformationalStringID.FontVendorURL => NameIdVendorUrl,
                InformationalStringID.LicenseDescription => NameIdLicense,
                InformationalStringID.SampleText => NameIdSampleText,
                InformationalStringID.Win32SubFamilyNames => NameIdSubfamily,
                InformationalStringID.WIN32FamilyNames => NameIdFamily,
                InformationalStringID.PreferredSubFamilyNames => NameIdPreferredSubfamily,
                InformationalStringID.PreferredFamilyNames => NameIdPreferredFamily,
                _ => 0
            };

            if (nameId != 0 && _nameStrings.TryGetValue(nameId, out localizedStrings) && localizedStrings.Count > 0)
            {
                return true;
            }

            localizedStrings = null;
            return false;
        }

        internal ushort GetGlyphIndex(uint codePoint)
        {
            if (_format12StartCodes.Length > 0)
            {
                int low = 0;
                int high = _format12StartCodes.Length - 1;
                while (low <= high)
                {
                    int mid = low + ((high - low) / 2);
                    uint start = _format12StartCodes[mid];
                    uint end = _format12EndCodes[mid];
                    if (codePoint >= start && codePoint <= end)
                    {
                        uint glyphIndex = _format12StartGlyphIds[mid] + (codePoint - start);
                        return glyphIndex <= ushort.MaxValue ? (ushort)glyphIndex : (ushort)0;
                    }

                    if (codePoint < start)
                    {
                        high = mid - 1;
                    }
                    else
                    {
                        low = mid + 1;
                    }
                }
            }

            if (_format4EndCodes.Length == 0 || codePoint > ushort.MaxValue)
            {
                return 0;
            }

            ushort code = (ushort)codePoint;
            int segment = -1;
            for (int i = 0; i < _format4EndCodes.Length; i++)
            {
                if (_format4EndCodes[i] >= code)
                {
                    segment = i;
                    break;
                }
            }

            if (segment < 0 || _format4StartCodes[segment] > code)
            {
                return 0;
            }

            ushort rangeOffset = _format4RangeOffsets[segment];
            if (rangeOffset == 0)
            {
                return (ushort)((code + _format4Deltas[segment]) & 0xFFFF);
            }

            uint rangeOffsetAddress = _format4RangeOffsetsTableOffset + checked((uint)(segment * 2));
            uint glyphIndexAddress = rangeOffsetAddress + rangeOffset + checked((uint)((code - _format4StartCodes[segment]) * 2));
            if (!CanRead(glyphIndexAddress, sizeof(ushort)))
            {
                return 0;
            }

            ushort rawIndex = ReadUShort(glyphIndexAddress);
            return rawIndex == 0 ? (ushort)0 : (ushort)((rawIndex + _format4Deltas[segment]) & 0xFFFF);
        }

        internal GlyphMetrics GetGlyphMetrics(ushort glyphIndex)
        {
            if (glyphIndex >= GlyphCount)
            {
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));
            }

            ushort advanceWidth = GetAdvanceWidth(glyphIndex);
            short leftSideBearing = GetLeftSideBearing(glyphIndex);
            GlyphBounds bounds = GetGlyphBounds(glyphIndex);

            int blackBoxWidth = bounds.XMax - bounds.XMin;
            int advanceHeight = Metrics.Ascent + Metrics.Descent + Math.Max(0, (int)Metrics.LineGap);
            if (advanceHeight <= 0)
            {
                advanceHeight = Metrics.DesignUnitsPerEm;
            }

            return new GlyphMetrics
            {
                LeftSideBearing = leftSideBearing,
                AdvanceWidth = advanceWidth,
                RightSideBearing = advanceWidth - leftSideBearing - blackBoxWidth,
                TopSideBearing = Metrics.Ascent - bounds.YMax,
                AdvanceHeight = checked((uint)advanceHeight),
                BottomSideBearing = advanceHeight - Metrics.Ascent + bounds.YMin,
                VerticalOriginY = Metrics.Ascent
            };
        }

        internal bool TryGetTable(uint tag, out byte[] tableData)
        {
            if (_sfntFace.TryGetTable(TagToString(tag), out ReadOnlyMemory<byte> tableDataMemory))
            {
                tableData = tableDataMemory.ToArray();
                return true;
            }

            if (_tables.TryGetValue(tag, out TableRecord table))
            {
                tableData = new byte[table.Length];
                Array.Copy(_data, checked((int)table.Offset), tableData, 0, checked((int)table.Length));
                return true;
            }

            tableData = null;
            return false;
        }

        internal bool TryGetEmbeddingRights(out ushort fsType)
        {
            fsType = _fsType;
            return _tables.ContainsKey(TagOs2);
        }

        private static PortableFontData CreateFace(byte[] data, Uri uri, uint faceIndex, uint faceOffset, bool isCollection)
        {
            try
            {
                return new PortableFontData(data, uri, faceIndex, faceOffset, isCollection);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IndexOutOfRangeException || ex is OverflowException)
            {
                throw new FileFormatException(uri.AbsoluteUri, ex);
            }
        }

        private static byte[] ReadFontBytes(Uri uri, IFontSource fontSource)
        {
            if (fontSource != null)
            {
                fontSource.TestFileOpenable();
                using (Stream stream = fontSource.GetStream())
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    stream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            if (!uri.IsFile)
            {
                throw new NotSupportedException("The portable WPF text interface only supports local font files when no font source factory is available.");
            }

            return File.ReadAllBytes(uri.LocalPath);
        }

        private static List<uint> GetFaceOffsets(byte[] data)
        {
            try
            {
                IReadOnlyList<ProGpuSfntFontFace> faces = ProGpuSfntFontFace.LoadFaces(data);
                List<uint> offsets = new List<uint>(faces.Count);
                foreach (ProGpuSfntFontFace face in faces)
                {
                    offsets.Add(face.BaseOffset);
                }

                return offsets;
            }
            catch (FormatException ex)
            {
                throw new FileFormatException("Invalid SFNT font data.", ex);
            }
        }

        private void ParseTableDirectory()
        {
            uint sfntVersion = ReadUInt(_faceOffset);
            if (sfntVersion != 0x00010000 && sfntVersion != 0x4F54544F)
            {
                throw new FileFormatException(SourceUri.AbsoluteUri);
            }

            ushort tableCount = ReadUShort(_faceOffset + 4);
            uint directoryOffset = _faceOffset + 12;
            for (int i = 0; i < tableCount; i++)
            {
                uint recordOffset = directoryOffset + checked((uint)(i * 16));
                uint tag = ReadUInt(recordOffset);
                uint tableOffset = ReadUInt(recordOffset + 8);
                uint tableLength = ReadUInt(recordOffset + 12);
                EnsureRange(tableOffset, tableLength);
                _tables[tag] = new TableRecord(tableOffset, tableLength);
            }

            RequireTable(TagHead);
            RequireTable(TagHhea);
            RequireTable(TagMaxp);
            RequireTable(TagHmtx);
            RequireTable(TagCmap);
        }

        private (FontMetrics metrics, ushort numberOfHMetrics, short indexToLocFormat, double version) ParseMetrics()
        {
            TableRecord head = RequireTable(TagHead);
            TableRecord hhea = RequireTable(TagHhea);
            FontMetrics metrics = new FontMetrics
            {
                DesignUnitsPerEm = ReadUShort(head.Offset + 18),
                Ascent = ToPositiveMetric(ReadShort(hhea.Offset + 4)),
                Descent = ToPositiveMetric(ReadShort(hhea.Offset + 6)),
                LineGap = ReadShort(hhea.Offset + 8),
                CapHeight = 0,
                XHeight = 0,
                UnderlinePosition = 0,
                UnderlineThickness = 0,
                StrikethroughPosition = 0,
                StrikethroughThickness = 0
            };

            if (metrics.DesignUnitsPerEm == 0)
            {
                metrics.DesignUnitsPerEm = 1;
            }

            if (_tables.TryGetValue(TagOs2, out TableRecord os2))
            {
                metrics.StrikethroughThickness = ReadUShort(os2.Offset + 26);
                metrics.StrikethroughPosition = ReadShort(os2.Offset + 28);

                if (os2.Length >= 90)
                {
                    short sxHeight = ReadShort(os2.Offset + 86);
                    short sCapHeight = ReadShort(os2.Offset + 88);
                    metrics.XHeight = ToPositiveMetric(sxHeight);
                    metrics.CapHeight = ToPositiveMetric(sCapHeight);
                }
            }

            if (_tables.TryGetValue(TagPost, out TableRecord post) && post.Length >= 12)
            {
                metrics.UnderlinePosition = ReadShort(post.Offset + 8);
                metrics.UnderlineThickness = ToPositiveMetric(ReadShort(post.Offset + 10));
            }

            if (metrics.CapHeight == 0)
            {
                metrics.CapHeight = checked((ushort)Math.Max(1, (metrics.DesignUnitsPerEm * 7) / 10));
            }

            if (metrics.XHeight == 0)
            {
                metrics.XHeight = checked((ushort)Math.Max(1, metrics.DesignUnitsPerEm / 2));
            }

            if (metrics.UnderlineThickness == 0)
            {
                metrics.UnderlineThickness = checked((ushort)Math.Max(1, metrics.DesignUnitsPerEm / 20));
            }

            ushort numberOfHMetrics = ReadUShort(hhea.Offset + 34);
            short indexToLocFormat = ReadShort(head.Offset + 50);
            double version = ReadFixed(head.Offset + 4);

            return (metrics, numberOfHMetrics, indexToLocFormat, version);
        }

        private (FontWeight weight, FontStretch stretch, FontStyle style, ushort fsType) ParseOs2AndStyle()
        {
            FontWeight weight = FontWeight.Normal;
            FontStretch stretch = FontStretch.Normal;
            FontStyle style = FontStyle.Normal;
            ushort fsType = 0;

            if (_tables.TryGetValue(TagOs2, out TableRecord os2))
            {
                if (os2.Length >= 10)
                {
                    ushort weightClass = ReadUShort(os2.Offset + 4);
                    weight = (FontWeight)Math.Clamp((int)weightClass, 1, 1000);
                    ushort widthClass = ReadUShort(os2.Offset + 6);
                    if (widthClass >= 1 && widthClass <= 9)
                    {
                        stretch = (FontStretch)widthClass;
                    }

                    fsType = ReadUShort(os2.Offset + 8);
                }

                if (os2.Length >= 64)
                {
                    ushort fsSelection = ReadUShort(os2.Offset + 62);
                    if ((fsSelection & 0x0001) != 0)
                    {
                        style = FontStyle.Italic;
                    }
                }
            }

            TableRecord head = RequireTable(TagHead);
            ushort macStyle = ReadUShort(head.Offset + 44);
            if ((macStyle & 0x0002) != 0)
            {
                style = FontStyle.Italic;
            }

            if ((macStyle & 0x0001) != 0 && (int)weight < (int)FontWeight.Bold)
            {
                weight = FontWeight.Bold;
            }

            return (weight, stretch, style, fsType);
        }

        private ushort ParseGlyphCount()
        {
            TableRecord maxp = RequireTable(TagMaxp);
            return ReadUShort(maxp.Offset + 4);
        }

        private void ParseNames()
        {
            if (!_tables.TryGetValue(TagName, out TableRecord name) || name.Length < 6)
            {
                return;
            }

            ushort count = ReadUShort(name.Offset + 2);
            ushort stringOffset = ReadUShort(name.Offset + 4);
            uint recordOffset = name.Offset + 6;

            for (int i = 0; i < count; i++)
            {
                uint offset = recordOffset + checked((uint)(i * 12));
                if (!CanRead(offset, 12))
                {
                    break;
                }

                ushort platformId = ReadUShort(offset);
                ushort languageId = ReadUShort(offset + 4);
                ushort nameId = ReadUShort(offset + 6);
                ushort length = ReadUShort(offset + 8);
                ushort nameOffset = ReadUShort(offset + 10);
                uint valueOffset = name.Offset + stringOffset + nameOffset;
                if (!CanRead(valueOffset, length))
                {
                    continue;
                }

                string value = DecodeName(platformId, valueOffset, length);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!_nameStrings.TryGetValue(nameId, out LocalizedStrings strings))
                {
                    strings = new LocalizedStrings();
                    _nameStrings[nameId] = strings;
                }

                CultureInfo culture = GetCulture(platformId, languageId);
                if (!strings.ContainsKey(culture))
                {
                    strings[culture] = value.Trim();
                }
            }
        }

        private CmapData ParseCmap()
        {
            TableRecord cmap = RequireTable(TagCmap);
            ushort tableCount = ReadUShort(cmap.Offset + 2);
            uint format4Offset = 0;
            uint format12Offset = 0;
            bool symbolFont = false;

            for (int i = 0; i < tableCount; i++)
            {
                uint recordOffset = cmap.Offset + 4 + checked((uint)(i * 8));
                ushort platformId = ReadUShort(recordOffset);
                ushort encodingId = ReadUShort(recordOffset + 2);
                uint subtableOffset = cmap.Offset + ReadUInt(recordOffset + 4);
                if (!CanRead(subtableOffset, sizeof(ushort)))
                {
                    continue;
                }

                ushort format = ReadUShort(subtableOffset);
                if (format == 12 && IsUnicodeCmap(platformId, encodingId))
                {
                    format12Offset = subtableOffset;
                }
                else if (format == 4 && IsUnicodeCmap(platformId, encodingId))
                {
                    format4Offset = subtableOffset;
                }
                else if (format == 4 && platformId == 3 && encodingId == 0 && format4Offset == 0)
                {
                    symbolFont = true;
                    format4Offset = subtableOffset;
                }
            }

            CmapData data = new CmapData
            {
                Format4EndCodes = Array.Empty<ushort>(),
                Format4StartCodes = Array.Empty<ushort>(),
                Format4Deltas = Array.Empty<short>(),
                Format4RangeOffsets = Array.Empty<ushort>(),
                Format12StartCodes = Array.Empty<uint>(),
                Format12EndCodes = Array.Empty<uint>(),
                Format12StartGlyphIds = Array.Empty<uint>(),
                IsSymbolFont = symbolFont
            };

            if (format12Offset != 0 && CanRead(format12Offset, 16))
            {
                uint groupCount = ReadUInt(format12Offset + 12);
                data.Format12StartCodes = new uint[groupCount];
                data.Format12EndCodes = new uint[groupCount];
                data.Format12StartGlyphIds = new uint[groupCount];
                uint groupOffset = format12Offset + 16;
                for (uint i = 0; i < groupCount; i++)
                {
                    uint offset = groupOffset + i * 12;
                    if (!CanRead(offset, 12))
                    {
                        break;
                    }

                    data.Format12StartCodes[i] = ReadUInt(offset);
                    data.Format12EndCodes[i] = ReadUInt(offset + 4);
                    data.Format12StartGlyphIds[i] = ReadUInt(offset + 8);
                }
            }

            if (format4Offset != 0 && CanRead(format4Offset, 14))
            {
                ushort segCount = checked((ushort)(ReadUShort(format4Offset + 6) / 2));
                data.Format4EndCodes = new ushort[segCount];
                data.Format4StartCodes = new ushort[segCount];
                data.Format4Deltas = new short[segCount];
                data.Format4RangeOffsets = new ushort[segCount];

                uint endCodeOffset = format4Offset + 14;
                uint startCodeOffset = endCodeOffset + checked((uint)(segCount * 2)) + 2;
                uint deltaOffset = startCodeOffset + checked((uint)(segCount * 2));
                uint rangeOffset = deltaOffset + checked((uint)(segCount * 2));
                data.Format4RangeOffsetsTableOffset = rangeOffset;

                for (int i = 0; i < segCount; i++)
                {
                    data.Format4EndCodes[i] = ReadUShort(endCodeOffset + checked((uint)(i * 2)));
                    data.Format4StartCodes[i] = ReadUShort(startCodeOffset + checked((uint)(i * 2)));
                    data.Format4Deltas[i] = ReadShort(deltaOffset + checked((uint)(i * 2)));
                    data.Format4RangeOffsets[i] = ReadUShort(rangeOffset + checked((uint)(i * 2)));
                }
            }

            return data;
        }

        private ushort GetAdvanceWidth(ushort glyphIndex)
        {
            if (_numberOfHMetrics == 0 || !_tables.TryGetValue(TagHmtx, out TableRecord hmtx))
            {
                return checked((ushort)(Metrics.DesignUnitsPerEm / 2));
            }

            uint offset = glyphIndex < _numberOfHMetrics
                ? hmtx.Offset + checked((uint)(glyphIndex * 4))
                : hmtx.Offset + checked((uint)((_numberOfHMetrics - 1) * 4));

            return ReadUShort(offset);
        }

        private short GetLeftSideBearing(ushort glyphIndex)
        {
            if (_numberOfHMetrics == 0 || !_tables.TryGetValue(TagHmtx, out TableRecord hmtx))
            {
                return 0;
            }

            uint offset = glyphIndex < _numberOfHMetrics
                ? hmtx.Offset + checked((uint)(glyphIndex * 4)) + 2
                : hmtx.Offset + checked((uint)(_numberOfHMetrics * 4)) + checked((uint)((glyphIndex - _numberOfHMetrics) * 2));

            return CanRead(offset, sizeof(ushort)) ? ReadShort(offset) : (short)0;
        }

        private GlyphBounds GetGlyphBounds(ushort glyphIndex)
        {
            if (!_tables.TryGetValue(TagLoca, out TableRecord loca) || !_tables.TryGetValue(TagGlyf, out TableRecord glyf))
            {
                return default;
            }

            uint startOffset;
            uint endOffset;
            if (_indexToLocFormat == 0)
            {
                uint locaOffset = loca.Offset + checked((uint)(glyphIndex * 2));
                startOffset = checked((uint)(ReadUShort(locaOffset) * 2));
                endOffset = checked((uint)(ReadUShort(locaOffset + 2) * 2));
            }
            else
            {
                uint locaOffset = loca.Offset + checked((uint)(glyphIndex * 4));
                startOffset = ReadUInt(locaOffset);
                endOffset = ReadUInt(locaOffset + 4);
            }

            if (startOffset == endOffset)
            {
                return default;
            }

            uint glyphOffset = glyf.Offset + startOffset;
            if (!CanRead(glyphOffset, 10))
            {
                return default;
            }

            return new GlyphBounds(
                ReadShort(glyphOffset + 2),
                ReadShort(glyphOffset + 4),
                ReadShort(glyphOffset + 6),
                ReadShort(glyphOffset + 8));
        }

        private string GetFirstName(params ushort[] nameIds)
        {
            foreach (ushort nameId in nameIds)
            {
                if (_nameStrings.TryGetValue(nameId, out LocalizedStrings strings))
                {
                    if (strings.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out string english) && !string.IsNullOrEmpty(english))
                    {
                        return english;
                    }

                    foreach (string value in strings.Values)
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
            }

            return null;
        }

        private TableRecord RequireTable(uint tag)
        {
            if (_tables.TryGetValue(tag, out TableRecord table))
            {
                return table;
            }

            throw new FileFormatException(SourceUri.AbsoluteUri);
        }

        private ushort ReadUShort(uint offset)
        {
            return ReadUShort(_data, offset);
        }

        private short ReadShort(uint offset)
        {
            return unchecked((short)ReadUShort(offset));
        }

        private uint ReadUInt(uint offset)
        {
            return ReadUInt(_data, offset);
        }

        private double ReadFixed(uint offset)
        {
            short major = ReadShort(offset);
            ushort minor = ReadUShort(offset + 2);
            return major + (minor / 65536.0);
        }

        private bool CanRead(uint offset, int length)
        {
            return offset <= _data.Length && length >= 0 && offset + (uint)length <= _data.Length;
        }

        private void EnsureRange(uint offset, uint length)
        {
            if (offset > _data.Length || length > _data.Length || offset + length > _data.Length)
            {
                throw new FileFormatException(SourceUri.AbsoluteUri);
            }
        }

        private string DecodeName(ushort platformId, uint offset, ushort length)
        {
            byte[] bytes = new byte[length];
            Array.Copy(_data, checked((int)offset), bytes, 0, length);

            string value = platformId == 0 || platformId == 3
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.Latin1.GetString(bytes);

            return value.Replace("\0", string.Empty);
        }

        private static CultureInfo GetCulture(ushort platformId, ushort languageId)
        {
            if (platformId == 3)
            {
                try
                {
                    return CultureInfo.GetCultureInfo(languageId);
                }
                catch (CultureNotFoundException)
                {
                }
            }

            return CultureInfo.InvariantCulture;
        }

        private static bool IsUnicodeCmap(ushort platformId, ushort encodingId)
        {
            return platformId == 0
                || (platformId == 3 && (encodingId == 1 || encodingId == 10));
        }

        private static ushort ToPositiveMetric(short value)
        {
            int positive = value < 0 ? -value : value;
            return checked((ushort)Math.Clamp(positive, 0, ushort.MaxValue));
        }

        private static ushort ReadUShort(byte[] data, uint offset)
        {
            int index = checked((int)offset);
            return (ushort)((data[index] << 8) | data[index + 1]);
        }

        private static uint ReadUInt(byte[] data, uint offset)
        {
            int index = checked((int)offset);
            return ((uint)data[index] << 24)
                | ((uint)data[index + 1] << 16)
                | ((uint)data[index + 2] << 8)
                | data[index + 3];
        }

        private static string TagToString(uint tag)
        {
            return new string(new[]
            {
                (char)((tag >> 24) & 0xFF),
                (char)((tag >> 16) & 0xFF),
                (char)((tag >> 8) & 0xFF),
                (char)(tag & 0xFF)
            });
        }

        private readonly struct TableRecord
        {
            internal TableRecord(uint offset, uint length)
            {
                Offset = offset;
                Length = length;
            }

            internal uint Offset { get; }

            internal uint Length { get; }
        }

        private readonly struct GlyphBounds
        {
            internal GlyphBounds(short xMin, short yMin, short xMax, short yMax)
            {
                XMin = xMin;
                YMin = yMin;
                XMax = xMax;
                YMax = yMax;
            }

            internal int XMin { get; }

            internal int YMin { get; }

            internal int XMax { get; }

            internal int YMax { get; }
        }

        private struct CmapData
        {
            internal ushort[] Format4EndCodes;
            internal ushort[] Format4StartCodes;
            internal short[] Format4Deltas;
            internal ushort[] Format4RangeOffsets;
            internal uint Format4RangeOffsetsTableOffset;
            internal uint[] Format12StartCodes;
            internal uint[] Format12EndCodes;
            internal uint[] Format12StartGlyphIds;
            internal bool IsSymbolFont;
        }
    }

    internal sealed class FontFile : IDisposable
    {
        private readonly Uri _uri;

        internal FontFile(Uri uri)
        {
            _uri = uri;
        }

        internal string GetUriPath()
        {
            return _uri.IsFile ? _uri.LocalPath : _uri.AbsoluteUri;
        }

        public void Dispose()
        {
        }
    }

    internal sealed unsafe class TextAnalyzer
    {
        internal const char CharHyphen = '\x002d';

        internal delegate int CreateTextAnalysisSource(
            char* text,
            uint length,
            char* culture,
            void* factory,
            bool isRightToLeft,
            char* numberCulture,
            bool ignoreUserOverride,
            uint numberSubstitutionMethod,
            void** ppTextAnalysisSource);

        internal delegate void* CreateTextAnalysisSink();

        internal delegate void* GetScriptAnalysisList(void* textAnalysisSink);

        internal delegate void* GetNumberSubstitutionList(void* textAnalysisSink);

        internal static IList<MS.Internal.Span> Itemize(
            char* text,
            uint length,
            CultureInfo culture,
            Native.IDWriteFactory* pDWriteFactory,
            bool isRightToLeftParagraph,
            CultureInfo numberCulture,
            bool ignoreUserOverride,
            uint numberSubstitutionMethod,
            IClassification classificationUtility,
            CreateTextAnalysisSink createTextAnalysisSink,
            GetScriptAnalysisList getScriptAnalysisList,
            GetNumberSubstitutionList getNumberSubstitutionList,
            CreateTextAnalysisSource createTextAnalysisSource)
        {
            return new List<MS.Internal.Span>
            {
                new MS.Internal.Span(new ItemProps(numberCulture), checked((int)length))
            };
        }

        internal void GetGlyphsAndTheirPlacements(
            char* textString,
            uint textLength,
            Font font,
            ushort blankGlyphIndex,
            bool isSideways,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            double fontEmSize,
            double scalingFactor,
            float pixelsPerDip,
            TextFormattingMode textFormattingMode,
            ItemProps itemProps,
            out ushort[] clusterMap,
            out ushort[] glyphIndices,
            out int[] glyphAdvances,
            out GlyphOffset[] glyphOffsets)
        {
            ArgumentNullException.ThrowIfNull(font);

            SimpleGlyphRun glyphRun = CreateSimpleGlyphRun(textString, textLength, font, blankGlyphIndex);
            clusterMap = glyphRun.ClusterMap;
            glyphIndices = glyphRun.GlyphIndices;
            glyphAdvances = new int[glyphIndices.Length];
            glyphOffsets = new GlyphOffset[glyphIndices.Length];

            FillGlyphPlacements(
                textString,
                clusterMap,
                textLength,
                glyphIndices,
                checked((uint)glyphIndices.Length),
                font,
                fontEmSize,
                scalingFactor,
                isSideways,
                glyphAdvances,
                glyphOffsets);
        }

        internal void GetGlyphs(
            char* textString,
            uint textLength,
            Font font,
            ushort blankGlyphIndex,
            bool isSideways,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            uint maxGlyphCount,
            TextFormattingMode textFormattingMode,
            ItemProps itemProps,
            ushort* clusterMap,
            ushort* textProps,
            ushort* glyphIndices,
            uint* glyphProps,
            int* pfCanGlyphAlone,
            out uint actualGlyphCount)
        {
            ArgumentNullException.ThrowIfNull(font);

            SimpleGlyphRun glyphRun = CreateSimpleGlyphRun(textString, textLength, font, blankGlyphIndex);
            actualGlyphCount = checked((uint)glyphRun.GlyphIndices.Length);

            for (uint i = 0; i < textLength; i++)
            {
                if (clusterMap != null)
                {
                    clusterMap[i] = glyphRun.ClusterMap[i];
                }

                if (textProps != null)
                {
                    textProps[i] = 0;
                }

                if (pfCanGlyphAlone != null)
                {
                    pfCanGlyphAlone[i] = 1;
                }
            }

            if (actualGlyphCount > maxGlyphCount)
            {
                return;
            }

            for (uint i = 0; i < actualGlyphCount; i++)
            {
                glyphIndices[i] = glyphRun.GlyphIndices[i];
                if (glyphProps != null)
                {
                    glyphProps[i] = 0;
                }
            }
        }

        internal void GetGlyphPlacements(
            char* textString,
            ushort* clusterMap,
            ushort* textProps,
            uint textLength,
            ushort* glyphIndices,
            uint* glyphProps,
            uint glyphCount,
            Font font,
            double fontEmSize,
            double scalingFactor,
            bool isSideways,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            TextFormattingMode textFormattingMode,
            ItemProps itemProps,
            float pixelsPerDip,
            int* glyphAdvances,
            out GlyphOffset[] glyphOffsets)
        {
            ArgumentNullException.ThrowIfNull(font);

            glyphOffsets = new GlyphOffset[glyphCount];
            FillGlyphPlacements(
                textString,
                clusterMap,
                textLength,
                glyphIndices,
                glyphCount,
                font,
                fontEmSize,
                scalingFactor,
                isSideways,
                glyphAdvances,
                glyphOffsets);
        }

        private static SimpleGlyphRun CreateSimpleGlyphRun(char* textString, uint textLength, Font font, ushort blankGlyphIndex)
        {
            ushort[] clusterMap = new ushort[textLength];
            List<ushort> glyphIndices = new List<ushort>(checked((int)textLength));

            uint textIndex = 0;
            while (textIndex < textLength)
            {
                ushort glyphCluster = checked((ushort)glyphIndices.Count);
                uint codePoint = ReadCodePoint(textString, textLength, textIndex, out uint codeUnitCount);
                ushort glyphIndex = GetSimpleGlyphIndex(font, codePoint, blankGlyphIndex);
                glyphIndices.Add(glyphIndex);

                for (uint i = 0; i < codeUnitCount; i++)
                {
                    clusterMap[textIndex + i] = glyphCluster;
                }

                textIndex += codeUnitCount;
            }

            return new SimpleGlyphRun(clusterMap, glyphIndices.ToArray());
        }

        private static ushort GetSimpleGlyphIndex(Font font, uint codePoint, ushort blankGlyphIndex)
        {
            if (codePoint == 0x00AD)
            {
                ushort hyphenGlyph = font.FontData.GetGlyphIndex(CharHyphen);
                return hyphenGlyph != 0 ? hyphenGlyph : blankGlyphIndex;
            }

            if (IsFormattingControl(codePoint))
            {
                return blankGlyphIndex;
            }

            return font.FontData.GetGlyphIndex(codePoint);
        }

        private static void FillGlyphPlacements(
            char* textString,
            ushort* clusterMap,
            uint textLength,
            ushort* glyphIndices,
            uint glyphCount,
            Font font,
            double fontEmSize,
            double scalingFactor,
            bool isSideways,
            int* glyphAdvances,
            GlyphOffset[] glyphOffsets)
        {
            for (uint i = 0; i < glyphCount; i++)
            {
                ushort glyphIndex = glyphIndices[i];
                int advance = 0;

                if (!IsControlGlyph(textString, clusterMap, textLength, i))
                {
                    GlyphMetrics metrics = font.FontData.GetGlyphMetrics(glyphIndex);
                    uint designAdvance = isSideways ? metrics.AdvanceHeight : metrics.AdvanceWidth;
                    advance = checked((int)Math.Round(designAdvance * fontEmSize * scalingFactor / font.Metrics.DesignUnitsPerEm));
                }

                glyphAdvances[i] = advance;
                glyphOffsets[i] = default;
            }
        }

        private static void FillGlyphPlacements(
            char* textString,
            ushort[] clusterMap,
            uint textLength,
            ushort[] glyphIndices,
            uint glyphCount,
            Font font,
            double fontEmSize,
            double scalingFactor,
            bool isSideways,
            int[] glyphAdvances,
            GlyphOffset[] glyphOffsets)
        {
            fixed (ushort* pClusterMap = clusterMap)
            fixed (ushort* pGlyphIndices = glyphIndices)
            fixed (int* pGlyphAdvances = glyphAdvances)
            {
                FillGlyphPlacements(
                    textString,
                    pClusterMap,
                    textLength,
                    pGlyphIndices,
                    glyphCount,
                    font,
                    fontEmSize,
                    scalingFactor,
                    isSideways,
                    pGlyphAdvances,
                    glyphOffsets);
            }
        }

        private static bool IsControlGlyph(char* textString, ushort* clusterMap, uint textLength, uint glyphIndex)
        {
            for (uint i = 0; i < textLength; i++)
            {
                if (clusterMap[i] == glyphIndex)
                {
                    uint codePoint = ReadCodePoint(textString, textLength, i, out _);
                    return IsFormattingControl(codePoint);
                }
            }

            return false;
        }

        private static uint ReadCodePoint(char* textString, uint textLength, uint textIndex, out uint codeUnitCount)
        {
            char current = textString[textIndex];
            if (char.IsHighSurrogate(current) && textIndex + 1 < textLength && char.IsLowSurrogate(textString[textIndex + 1]))
            {
                codeUnitCount = 2;
                return checked((uint)char.ConvertToUtf32(current, textString[textIndex + 1]));
            }

            codeUnitCount = 1;
            return current;
        }

        private static bool IsFormattingControl(uint codePoint)
        {
            return codePoint < 0x20 || (codePoint >= 0x7F && codePoint <= 0x9F);
        }

        private readonly struct SimpleGlyphRun
        {
            internal SimpleGlyphRun(ushort[] clusterMap, ushort[] glyphIndices)
            {
                ClusterMap = clusterMap;
                GlyphIndices = glyphIndices;
            }

            internal ushort[] ClusterMap { get; }

            internal ushort[] GlyphIndices { get; }
        }
    }

    internal static class DWriteTypeConverter
    {
        internal static ushort Convert(TextFormattingMode textFormattingMode)
        {
            return textFormattingMode == TextFormattingMode.Display ? (ushort)1 : (ushort)0;
        }
    }

    internal sealed unsafe class ItemProps
    {
        internal ItemProps()
            : this(CultureInfo.InvariantCulture)
        {
        }

        internal ItemProps(CultureInfo digitCulture)
        {
            DigitCulture = digitCulture ?? CultureInfo.InvariantCulture;
        }

        internal void* NumberSubstitutionNoAddRef => null;

        internal void* ScriptAnalysis => null;

        internal CultureInfo DigitCulture { get; }

        internal bool HasExtendedCharacter => false;

        internal bool NeedsCaretInfo => false;

        internal bool IsIndic => false;

        internal bool IsLatin => true;

        internal bool HasCombiningMark => false;

        internal bool CanShapeTogether(ItemProps other)
        {
            return other != null && Equals(DigitCulture, other.DigitCulture);
        }
    }
}

namespace MS.Internal.Text.TextInterface.Native
{
    internal struct IDWriteFactory
    {
    }
}

namespace MS.Internal
{
    internal static unsafe class TrueTypeSubsetter
    {
        internal static byte[] ComputeSubset(void* fontData, int fileSize, Uri sourceUri, int directoryOffset, ushort[] glyphArray)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
            if (fontData == null)
            {
                throw new ArgumentNullException(nameof(fontData));
            }

            byte[] fontCopy = new byte[fileSize];
            Marshal.Copy((IntPtr)fontData, fontCopy, 0, fileSize);
            return fontCopy;
        }
    }

    internal static class NativeWPFDLLLoader
    {
        internal static void LoadDwrite()
        {
        }
    }
}
