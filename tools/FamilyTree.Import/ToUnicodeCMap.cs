using System.Text.RegularExpressions;
using System.Text;

namespace FamilyTree.Import;

/// <summary>
/// ONE font's glyph-id -> text mapping.
///
/// <para>
/// <see cref="Parse(IEnumerable{byte[]})"/> merges every CMap stream it is handed into a single
/// map, which is only safe for a document with exactly one embedded font: Identity-H subsets
/// number their glyphs from zero, so two fonts' id spaces overlap and the later stream silently
/// overwrites the earlier (final review, Important 3). Prefer <see cref="PdfFonts.ParseFirstPage"/>,
/// which keys a map per font resource. The merging overload remains for callers that genuinely
/// have a single font, and for tests that hand this class raw CMap bytes with no PDF structure
/// around them.
/// </para>
/// </summary>
public sealed class ToUnicodeCMap : IGlyphDecoder
{
    private readonly Dictionary<int, string> _map = new();

    public int Count => _map.Count;
    public string? Lookup(int glyphId) => _map.GetValueOrDefault(glyphId);

    /// <summary>Ignores the font resource name: this map already IS one font's, or a merge of
    /// several that the caller has accepted as interchangeable.</summary>
    string? IGlyphDecoder.Lookup(string? fontResourceName, int glyphId) => Lookup(glyphId);

    public static ToUnicodeCMap Parse(IEnumerable<byte[]> streams)
    {
        var cmap = new ToUnicodeCMap();
        foreach (var raw in streams)
        {
            var text = Encoding.Latin1.GetString(raw);
            if (!text.Contains("begincmap", StringComparison.Ordinal)) continue;

            // Parse bfchar entries: <glyphId> <unicode>
            // Properly handle multi-code-unit destinations (ligatures, supplementary planes)
            foreach (Match block in Regex.Matches(text, "beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(block.Groups[1].Value, "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>"))
                    cmap._map[Hex(e.Groups[1])] = DecodeUtf16Be(e.Groups[2].Value);

            // Parse bfrange entries: <lo> <hi> <dstStart> (triple form only)
            foreach (Match block in Regex.Matches(text, "beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
            {
                var content = block.Groups[1].Value;

                // Detect array form [<d1> <d2> ...] and reject it loudly, not silently
                if (content.Contains('['))
                    throw new NotSupportedException("ToUnicode CMap bfrange array form is not supported in this implementation. Only triple form <lo> <hi> <dst> is supported.");

                foreach (Match e in Regex.Matches(content, "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>"))
                {
                    int lo = Hex(e.Groups[1]), hi = Hex(e.Groups[2]);
                    string dstHex = e.Groups[3].Value;

                    // Offset form only works with single UTF-16 code unit (4 hex digits) destinations in triple form
                    if (dstHex.Length != 4)
                        throw new InvalidOperationException($"bfrange triple form offset only supports single UTF-16 code unit destinations (4 hex digits), got {dstHex.Length} digits: {dstHex}");

                    int dstStart = Hex(e.Groups[3]);
                    for (var g = lo; g <= hi; g++)
                        cmap._map[g] = char.ConvertFromUtf32(dstStart + (g - lo));
                }
            }
        }
        return cmap;
    }

    /// <summary>
    /// Decodes a sequence of UTF-16BE code units from a hex string.
    /// Each 4 hex digits represents one 16-bit code unit.
    /// Surrogate pairs are automatically combined into the correct character(s).
    /// Multi-code-unit sequences (ligatures, supplementary-plane chars) are preserved as complete strings.
    /// </summary>
    private static string DecodeUtf16Be(string hexString)
    {
        if (hexString.Length % 4 != 0)
            throw new InvalidOperationException($"Destination hex value must have length that is a multiple of 4 (got {hexString.Length}: {hexString})");

        var chars = new char[hexString.Length / 4];
        for (int i = 0; i < hexString.Length; i += 4)
        {
            int codeUnit = Convert.ToInt32(hexString.Substring(i, 4), 16);
            chars[i / 4] = (char)codeUnit;
        }

        return new string(chars);
    }

    private static int Hex(Group g) => Convert.ToInt32(g.Value, 16);
}
