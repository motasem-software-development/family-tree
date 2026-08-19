namespace FamilyTree.Import;

/// <summary>
/// Turns a glyph id, as drawn by some font resource, back into text.
///
/// <para>
/// The font matters. An Identity-H subset numbers its glyphs from zero, so two embedded subsets
/// in one document both start near glyph id 0 and their id spaces overlap completely: a glyph id
/// is only meaningful together with the font that drew it. This seam exists so
/// <see cref="ContentStream.Read"/> can be given either a per-font decoder
/// (<see cref="PdfFonts"/>) or a single font's map (<see cref="ToUnicodeCMap"/>) without two
/// copies of the interpreter.
/// </para>
/// </summary>
public interface IGlyphDecoder
{
    /// <param name="fontResourceName">
    /// The page-local resource name from the content stream's own <c>Tf</c> operator (e.g.
    /// <c>F6</c>), without the leading slash. Null when the stream selected no font.
    /// </param>
    string? Lookup(string? fontResourceName, int glyphId);
}

/// <summary>
/// Every font resource a PDF page declares, each with its OWN <c>/ToUnicode</c> map (final
/// review, Important 3).
///
/// <para>
/// <see cref="ToUnicodeCMap.Parse(IEnumerable{byte[]})"/> merges all of a document's CMap streams
/// into one flat glyph-id -> string dictionary with no per-font keying. With a single embedded
/// font that is harmless, and every test on this branch used exactly that -- the flagship
/// round-trip rendered with NO caption, while every production export has one and a caption
/// embeds a second font. With two Identity-H subsets whose glyph ids both start near zero, the
/// later stream's entries simply overwrite the earlier's: measured on the round-trip fixture's
/// own tree, ح decoded as 8, ن as m, and ل as e, and the reconstructed hierarchy no longer
/// matched the source.
/// </para>
///
/// <para>
/// Note this is a different defect from the parked <c>bfrange</c> offset one. That one
/// mis-decodes within a single font's map; this one attributes one font's map to another font's
/// glyphs. Fixing the keying does not touch the bfrange arithmetic, and does not need to.
/// </para>
/// </summary>
public sealed class PdfFonts : IGlyphDecoder
{
    private readonly IReadOnlyDictionary<string, ToUnicodeCMap> _byResourceName;

    private PdfFonts(IReadOnlyDictionary<string, ToUnicodeCMap> byResourceName) =>
        _byResourceName = byResourceName;

    public IReadOnlyCollection<string> ResourceNames => (IReadOnlyCollection<string>)_byResourceName.Keys;

    public ToUnicodeCMap? For(string resourceName) => _byResourceName.GetValueOrDefault(resourceName);

    public string? Lookup(string? fontResourceName, int glyphId) =>
        fontResourceName is not null && _byResourceName.TryGetValue(fontResourceName, out var cmap)
            ? cmap.Lookup(glyphId)
            : null;

    /// <summary>
    /// Reads the font resources of the FIRST page that declares any.
    ///
    /// <para>
    /// Resource names are page-local, so "the document's fonts" is not a well-defined thing to
    /// ask for -- two pages may legitimately bind the same name to different fonts. Every caller
    /// here reads one page's content stream, so one page's bindings is the right scope; a
    /// multi-page document whose later pages rebind a name would need this widened, and this is
    /// the place to widen it.
    /// </para>
    /// </summary>
    public static PdfFonts ParseFirstPage(byte[] pdf)
    {
        var objects = PdfObjects.Read(pdf);
        var byNumber = objects.ToDictionary(o => o.Number);

        foreach (var page in PdfObjects.Pages(objects))
        {
            var resources = PdfObjects.FontResourcesOf(page, objects);
            if (resources.Count == 0) continue;

            var maps = new Dictionary<string, ToUnicodeCMap>();
            foreach (var (name, fontNumber) in resources)
            {
                if (!byNumber.TryGetValue(fontNumber, out var font)) continue;
                if (PdfObjects.Reference(font.Dictionary, "/ToUnicode") is not { } cmapNumber) continue;
                if (!byNumber.TryGetValue(cmapNumber, out var cmapObject)) continue;
                if (cmapObject.Stream is null) continue;

                maps[name] = ToUnicodeCMap.Parse([cmapObject.Stream]);
            }

            if (maps.Count > 0) return new PdfFonts(maps);
        }

        return new PdfFonts(new Dictionary<string, ToUnicodeCMap>());
    }
}
