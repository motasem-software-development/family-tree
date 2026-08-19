using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyTree.Import;

/// <param name="Dictionary">
/// The object's body up to its <c>stream</c> keyword (or to <c>endobj</c> for an object that has
/// no stream), as Latin1 text. PDF syntax outside stream data is byte-oriented ASCII, so Latin1
/// round-trips it without loss.
/// </param>
/// <param name="Stream">The object's decompressed stream data, or null when it has none.</param>
public sealed record PdfObject(int Number, string Dictionary, byte[]? Stream);

/// <summary>
/// A minimal reader for a PDF's indirect-object skeleton -- enough to answer "which
/// <c>/ToUnicode</c> CMap belongs to which font resource name", which
/// <see cref="PdfStreams.Inflate"/> deliberately cannot: it returns stream payloads with the
/// object numbers, and therefore every structural relationship, discarded.
///
/// <para>
/// This is not a general PDF parser and does not try to be. It reads uncompressed indirect
/// objects (<c>N G obj ... endobj</c>) written linearly, which is what Skia's PDF backend and the
/// reference fixture both emit; it does not read cross-reference streams or object streams, and
/// an input that uses them simply yields no fonts, which callers treat as "fall back to the
/// undifferentiated map" rather than as an error.
/// </para>
/// </summary>
public static class PdfObjects
{
    private static readonly Regex ObjectHeader = new(@"(\d+)\s+\d+\s+obj", RegexOptions.Compiled);

    public static IReadOnlyList<PdfObject> Read(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var objects = new List<PdfObject>();

        foreach (Match header in ObjectHeader.Matches(text))
        {
            var bodyStart = header.Index + header.Length;
            var end = text.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
            if (end < 0) continue;

            var streamStart = text.IndexOf("stream", bodyStart, StringComparison.Ordinal);
            var hasStream = streamStart >= 0 && streamStart < end;

            var dictionary = text[bodyStart..(hasStream ? streamStart : end)];
            objects.Add(new PdfObject(
                int.Parse(header.Groups[1].Value),
                dictionary,
                hasStream ? StreamDataAt(pdf, text, streamStart) : null));
        }

        return objects;
    }

    /// <summary>
    /// The font resource names a page declares, mapped to the object number of the font each
    /// names -- i.e. the contents of that page's <c>/Resources /Font</c> dictionary. Resource
    /// names are page-local, which is the whole reason a glyph id means nothing without one.
    /// </summary>
    public static IReadOnlyDictionary<string, int> FontResourcesOf(
        PdfObject page, IReadOnlyList<PdfObject> objects)
    {
        var resources = DictionaryValue(page.Dictionary, "/Resources");

        // /Resources may be written inline or as a reference to its own object.
        if (resources is null && Reference(page.Dictionary, "/Resources") is { } resourcesRef)
            resources = objects.FirstOrDefault(o => o.Number == resourcesRef)?.Dictionary;

        var fonts = resources is null ? null : DictionaryValue(resources, "/Font");
        if (fonts is null) return new Dictionary<string, int>();

        return Regex.Matches(fonts, @"/([^\s/<>\[\]]+)\s+(\d+)\s+\d+\s+R")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
    }

    public static IEnumerable<PdfObject> Pages(IReadOnlyList<PdfObject> objects) =>
        objects.Where(o => Regex.IsMatch(o.Dictionary, @"/Type\s*/Page\b"));

    /// <summary>The object number an indirect reference names, e.g. <c>/ToUnicode 14 0 R</c>.</summary>
    public static int? Reference(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, Regex.Escape(key) + @"\s+(\d+)\s+\d+\s+R");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// The text between the balanced <c>&lt;&lt;</c> and <c>&gt;&gt;</c> that follow
    /// <paramref name="key"/>, or null when the key is absent or its value is not an inline
    /// dictionary. Balanced, not first-match: <c>/Resources</c> nests <c>/Font</c> and
    /// <c>/ExtGState</c> inside it, so stopping at the first <c>&gt;&gt;</c> would truncate it.
    /// </summary>
    private static string? DictionaryValue(string source, string key)
    {
        var at = source.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;

        var open = source.IndexOf("<<", at, StringComparison.Ordinal);
        if (open < 0) return null;

        // Anything other than whitespace between the key and the "<<" means the key's value is
        // something else entirely and this "<<" belongs to a later key.
        if (source[(at + key.Length)..open].Any(c => !char.IsWhiteSpace(c))) return null;

        var depth = 0;
        for (var i = open; i < source.Length - 1; i++)
        {
            if (source[i] == '<' && source[i + 1] == '<') { depth++; i++; }
            else if (source[i] == '>' && source[i + 1] == '>')
            {
                depth--;
                if (depth == 0) return source[(open + 2)..i];
                i++;
            }
        }

        return null;
    }

    private static byte[]? StreamDataAt(byte[] pdf, string text, int streamKeywordIndex)
    {
        var data = streamKeywordIndex + "stream".Length;
        if (data < pdf.Length && pdf[data] == (byte)'\r') data++;
        if (data < pdf.Length && pdf[data] == (byte)'\n') data++;

        var stop = text.IndexOf("endstream", data, StringComparison.Ordinal);
        if (stop < 0) return null;

        try
        {
            using var input = new MemoryStream(pdf, data, stop - data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            // Not a Flate stream. Every stream this reader cares about is one, so a raw stream is
            // simply not decodable here -- same tolerance PdfStreams.Inflate applies.
            return null;
        }
    }
}
