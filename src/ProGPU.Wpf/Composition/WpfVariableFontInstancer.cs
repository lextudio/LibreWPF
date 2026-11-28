using System.Buffers.Binary;
using System.Text;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using ProGpuLineSegment = ProGPU.Vector.LineSegment;
using ProGpuPathGeometry = ProGPU.Vector.PathGeometry;
using ProGpuQuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>
/// Generates a static font from one weight of a variable TrueType font. Outlines and advances come
/// from the managed variation engine (<see cref="TtfFont.WithVariations(FontVariationSetting[])"/>),
/// so the static font matches what the variable font draws at that weight. Layout tables
/// (GPOS/GSUB/GDEF) are kept unchanged, so kerning stays at the default instance.
/// </summary>
internal sealed class WpfVariableFontInstancer : IPortableVariableFontInstancer
{
    internal static readonly WpfVariableFontInstancer Default = new();

    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureVariableFontInstancer(Default);

    private static readonly string[] DroppedTables =
    {
        "fvar", "gvar", "avar", "HVAR", "MVAR", "STAT", "cvar", "VVAR", "cvt ", "fpgm", "prep", "gasp", "DSIG",
    };

    public bool TryGetWeightRange(string fontPath, out int minimum, out int maximum, out int defaultWeight)
    {
        minimum = maximum = defaultWeight = 0;
        try
        {
            var font = new TtfFont(fontPath);
            foreach (FontVariationAxis axis in font.VariationAxes)
            {
                if (string.Equals(axis.Tag, "wght", StringComparison.Ordinal))
                {
                    minimum = (int)MathF.Ceiling(axis.Minimum);
                    maximum = (int)MathF.Floor(axis.Maximum);
                    defaultWeight = (int)MathF.Round(axis.Default);
                    return maximum > minimum;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or FormatException or InvalidDataException or KeyNotFoundException or IndexOutOfRangeException
            or OverflowException or NotSupportedException)
        {
        }

        return false;
    }

    public byte[] CreateWeightInstance(byte[] fontData, int weight)
    {
        if (fontData.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(fontData) != 0x00010000)
        {
            throw new NotSupportedException("Only single-face TrueType variable fonts can be instanced.");
        }

        Dictionary<string, byte[]> tables = ReadTables(fontData);
        if (!tables.TryGetValue("head", out byte[] head)
            || !tables.TryGetValue("hhea", out byte[] hhea)
            || !tables.TryGetValue("maxp", out byte[] maxp)
            || !tables.TryGetValue("OS/2", out byte[] os2)
            || !tables.ContainsKey("glyf")
            || !tables.ContainsKey("fvar"))
        {
            throw new NotSupportedException("The font is not a variable TrueType font.");
        }

        ushort unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(18));
        ushort glyphCount = BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));

        var variable = new TtfFont(fontData, 0);
        TtfFont instance = variable.WithVariations(new FontVariationSetting("wght", weight));

        var glyf = new MemoryStream();
        var loca = new byte[checked((glyphCount + 1) * 4)];
        var hmtx = new byte[checked(glyphCount * 4)];
        short fontXMin = short.MaxValue, fontYMin = short.MaxValue, fontXMax = short.MinValue, fontYMax = short.MinValue;
        ushort maxPoints = 0, maxContours = 0, maxAdvance = 0;
        short minLsb = short.MaxValue;
        int maxExtent = short.MinValue;

        for (int glyph = 0; glyph < glyphCount; glyph++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyph * 4), checked((uint)glyf.Length));

            ProGpuPathGeometry outline = instance.GetGlyphOutline((ushort)glyph);
            short xMin = 0, yMin = 0, xMax = 0, yMax = 0;
            bool hasOutline = false;
            if (outline != null)
            {
                hasOutline = WriteSimpleGlyph(glyf, outline, ref xMin, ref yMin, ref xMax, ref yMax, ref maxPoints, ref maxContours);
            }

            float advanceUnits = instance.GetAdvanceWidth((ushort)glyph, unitsPerEm);
            ushort advance = (ushort)Math.Clamp((int)MathF.Round(advanceUnits), 0, ushort.MaxValue);
            short lsb = hasOutline ? xMin : (short)0;
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(glyph * 4), advance);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(glyph * 4 + 2), lsb);

            maxAdvance = Math.Max(maxAdvance, advance);
            if (hasOutline)
            {
                fontXMin = Math.Min(fontXMin, xMin);
                fontYMin = Math.Min(fontYMin, yMin);
                fontXMax = Math.Max(fontXMax, xMax);
                fontYMax = Math.Max(fontYMax, yMax);
                minLsb = Math.Min(minLsb, lsb);
                maxExtent = Math.Max(maxExtent, lsb + (xMax - xMin));
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyphCount * 4), checked((uint)glyf.Length));

        if (fontXMin > fontXMax)
        {
            fontXMin = fontYMin = fontXMax = fontYMax = 0;
            minLsb = 0;
            maxExtent = 0;
        }

        byte[] newHead = (byte[])head.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(newHead.AsSpan(8), 0); // checkSumAdjustment, set last
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(36), fontXMin);
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(38), fontYMin);
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(40), fontXMax);
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(42), fontYMax);
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(50), 1); // long loca offsets

        byte[] newHhea = (byte[])hhea.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(newHhea.AsSpan(10), maxAdvance);
        BinaryPrimitives.WriteInt16BigEndian(newHhea.AsSpan(12), minLsb);
        BinaryPrimitives.WriteInt16BigEndian(newHhea.AsSpan(16), (short)Math.Clamp(maxExtent, short.MinValue, short.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(newHhea.AsSpan(34), glyphCount);

        byte[] newMaxp = (byte[])maxp.Clone();
        if (newMaxp.Length >= 32)
        {
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(6), maxPoints);
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(8), maxContours);
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(10), 0);
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(12), 0);
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(26), 0);
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(28), 0);
            BinaryPrimitives.WriteUInt16BigEndian(newMaxp.AsSpan(30), 0);
        }

        byte[] newOs2 = (byte[])os2.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(newOs2.AsSpan(4), (ushort)weight);

        var output = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, byte[]> table in tables)
        {
            if (Array.IndexOf(DroppedTables, table.Key) < 0)
            {
                output[table.Key] = table.Value;
            }
        }

        output["head"] = newHead;
        output["hhea"] = newHhea;
        output["maxp"] = newMaxp;
        output["OS/2"] = newOs2;
        output["hmtx"] = hmtx;
        output["loca"] = loca;
        output["glyf"] = glyf.ToArray();

        return Assemble(output);
    }


    private static bool WriteSimpleGlyph(
        MemoryStream glyf,
        ProGpuPathGeometry outline,
        ref short xMin,
        ref short yMin,
        ref short xMax,
        ref short yMax,
        ref ushort maxPoints,
        ref ushort maxContours)
    {
        var points = new List<(int X, int Y, bool OnCurve)>();
        var endPoints = new List<ushort>();

        foreach (global::ProGPU.Vector.PathFigure figure in outline.Figures)
        {
            int first = points.Count;
            points.Add((Round(figure.StartPoint.X), Round(figure.StartPoint.Y), true));

            foreach (global::ProGPU.Vector.PathSegment segment in figure.Segments)
            {
                switch (segment)
                {
                    case ProGpuLineSegment line:
                        points.Add((Round(line.Point.X), Round(line.Point.Y), true));
                        break;
                    case ProGpuQuadraticBezierSegment quad:
                        points.Add((Round(quad.ControlPoint.X), Round(quad.ControlPoint.Y), false));
                        points.Add((Round(quad.Point.X), Round(quad.Point.Y), true));
                        break;
                    default:
                        throw new NotSupportedException("Only quadratic TrueType outlines can be instanced.");
                }
            }

            // A closed figure repeats its start point; TrueType contours close implicitly.
            int count = points.Count - first;
            if (count > 1 && points[^1].OnCurve && points[^1].X == points[first].X && points[^1].Y == points[first].Y)
            {
                points.RemoveAt(points.Count - 1);
                count--;
            }

            if (count < 2)
            {
                points.RemoveRange(first, count);
                continue;
            }

            endPoints.Add(checked((ushort)(points.Count - 1)));
        }

        if (endPoints.Count == 0)
        {
            return false;
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach ((int x, int y, _) in points)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        xMin = checked((short)minX);
        yMin = checked((short)minY);
        xMax = checked((short)maxX);
        yMax = checked((short)maxY);
        maxPoints = (ushort)Math.Max(maxPoints, points.Count);
        maxContours = (ushort)Math.Max(maxContours, endPoints.Count);

        long start = glyf.Length;
        Span<byte> two = stackalloc byte[2];
        WriteInt16(glyf, (short)endPoints.Count);
        WriteInt16(glyf, xMin);
        WriteInt16(glyf, yMin);
        WriteInt16(glyf, xMax);
        WriteInt16(glyf, yMax);
        foreach (ushort end in endPoints)
        {
            BinaryPrimitives.WriteUInt16BigEndian(two, end);
            glyf.Write(two);
        }

        WriteInt16(glyf, 0); // no instructions

        foreach ((_, _, bool onCurve) in points)
        {
            glyf.WriteByte(onCurve ? (byte)1 : (byte)0);
        }

        int previous = 0;
        foreach ((int x, _, _) in points)
        {
            WriteInt16(glyf, checked((short)(x - previous)));
            previous = x;
        }

        previous = 0;
        foreach ((_, int y, _) in points)
        {
            WriteInt16(glyf, checked((short)(y - previous)));
            previous = y;
        }

        while (((glyf.Length - start) & 3) != 0)
        {
            glyf.WriteByte(0);
        }

        return true;
    }

    private static int Round(float value) => (int)MathF.Round(value);

    private static void WriteInt16(MemoryStream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static Dictionary<string, byte[]> ReadTables(byte[] data)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var tables = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(data, record, 4);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
            if ((ulong)offset + length > (ulong)data.Length)
            {
                throw new InvalidDataException("A font table lies outside the file.");
            }

            tables[tag] = data.AsSpan((int)offset, (int)length).ToArray();
        }

        return tables;
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        int count = tables.Count;
        int directorySize = 12 + count * 16;
        int total = directorySize;
        foreach (byte[] table in tables.Values)
        {
            total += (table.Length + 3) & ~3;
        }

        var output = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(output, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)count);
        int entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= count)
        {
            entrySelector++;
        }

        int searchRange = (1 << entrySelector) * 16;
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), (ushort)(count * 16 - searchRange));

        int offset = directorySize;
        int record = 12;
        int headOffset = -1;
        foreach (KeyValuePair<string, byte[]> pair in tables)
        {
            byte[] table = pair.Value;
            Encoding.ASCII.GetBytes(pair.Key, output.AsSpan(record, 4));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(record + 4), Checksum(table));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(record + 12), (uint)table.Length);
            table.CopyTo(output.AsSpan(offset));
            if (pair.Key == "head")
            {
                headOffset = offset;
            }

            offset += (table.Length + 3) & ~3;
            record += 16;
        }

        if (headOffset >= 0)
        {
            uint adjustment = unchecked(0xB1B0AFBAu - Checksum(output));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(headOffset + 8), adjustment);
        }

        return output;
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        int whole = data.Length & ~3;
        for (int i = 0; i < whole; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i)));
        }

        if (whole < data.Length)
        {
            uint tail = 0;
            for (int i = whole; i < data.Length; i++)
            {
                tail |= (uint)data[i] << (24 - 8 * (i - whole));
            }

            sum = unchecked(sum + tail);
        }

        return sum;
    }
}
