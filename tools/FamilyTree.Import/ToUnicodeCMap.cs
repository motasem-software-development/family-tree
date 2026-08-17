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

            foreach (Match block in Regex.Matches(text, "beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(block.Groups[1].Value, "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>"))
                    cmap._map[Hex(e.Groups[1])] = Char(e.Groups[2]);

            foreach (Match block in Regex.Matches(text, "beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(block.Groups[1].Value,
                             "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>"))
                {
                    int lo = Hex(e.Groups[1]), hi = Hex(e.Groups[2]), dst = Hex4(e.Groups[3]);
                    for (var g = lo; g <= hi; g++)
                        cmap._map[g] = char.ConvertFromUtf32(dst + (g - lo));
                }
        }
        return cmap;
    }

    private static int Hex(Group g) => Convert.ToInt32(g.Value, 16);
    private static int Hex4(Group g) => Convert.ToInt32(g.Value[..4], 16);
    private static string Char(Group g) => char.ConvertFromUtf32(Hex4(g));
}
