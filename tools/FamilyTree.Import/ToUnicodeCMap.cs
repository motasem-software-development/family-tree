using System.Text.RegularExpressions;
using System.Text;

namespace FamilyTree.Import;

public sealed class ToUnicodeCMap
{
    private readonly Dictionary<int, string> _map = new();

    public int Count => _map.Count;
    public string? Lookup(int glyphId) => _map.GetValueOrDefault(glyphId);

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
