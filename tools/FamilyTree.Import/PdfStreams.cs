using System.IO.Compression;

namespace FamilyTree.Import;

public static class PdfStreams
{
    public static IReadOnlyList<byte[]> Inflate(byte[] pdf)
    {
        var results = new List<byte[]>();
        var marker = "stream"u8.ToArray();
        var end = "endstream"u8.ToArray();

        for (var i = 0; ; )
        {
            var start = IndexOf(pdf, marker, i);
            if (start < 0) break;

            var data = start + marker.Length;
            if (data < pdf.Length && pdf[data] == (byte)'\r') data++;
            if (data < pdf.Length && pdf[data] == (byte)'\n') data++;

            var stop = IndexOf(pdf, end, data);
            if (stop < 0) break;

            try
            {
                using var input = new MemoryStream(pdf, data, stop - data);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                results.Add(output.ToArray());
            }
            catch (InvalidDataException)
            {
                // Not a Flate stream — the geometry we need is, so skipping is correct here.
            }

            i = stop + end.Length;
        }

        return results;
    }

    public static byte[] LargestOf(IReadOnlyList<byte[]> streams) =>
        streams.MaxBy(s => s.Length) ?? throw new InvalidOperationException("No streams.");

    /// <summary>
    /// Picks the page content stream by shape, not raw size, so it works for both the
    /// reference fixture (XMind's export) and Skia's export.
    ///
    /// <para>
    /// XMind's <c>familytree.pdf</c> has no other Flate stream competing in size, so
    /// <see cref="LargestOf"/> (byte-size only) happens to already pick the content stream
    /// there -- confirmed by <c>ContentStreamOf_agrees_with_LargestOf_on_the_reference_fixture</c>,
    /// which pins both selectors to the same 244,206-byte stream and must keep passing. Skia's
    /// export embeds a full NotoSans TTF subset (measured: 261,460 bytes here) as its own Flate
    /// stream, which is *larger* than the actual content stream (measured: 21,459 bytes) --
    /// <see cref="LargestOf"/> picks the font and <see cref="ContentStream.Read"/> silently
    /// returns zero glyphs and zero paths from it.
    /// </para>
    ///
    /// <para>
    /// A PDF content stream is textual (PDF operator/operand syntax in ASCII); an embedded
    /// TrueType font program is binary. This picks the largest stream whose bytes are
    /// overwhelmingly printable ASCII/whitespace, which selects the content stream over any
    /// font program in both emitters' output regardless of which stream happens to be biggest.
    /// The small textual CMap stream is excluded by the "largest" tie-break, exactly as with
    /// <see cref="LargestOf"/>.
    /// </para>
    /// </summary>
    public static byte[] ContentStreamOf(IReadOnlyList<byte[]> streams) =>
        streams.Where(IsMostlyText).MaxBy(s => s.Length)
        ?? throw new InvalidOperationException("No text-like stream found.");

    private static bool IsMostlyText(byte[] s)
    {
        if (s.Length == 0) return false;

        var printable = s.Count(b => b is (>= 0x20 and <= 0x7E) or (byte)'\n' or (byte)'\r' or (byte)'\t');
        return printable / (double)s.Length >= 0.95;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        if (needle.Length == 0) return from;

        for (var i = from; i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched) return i;
        }

        return -1;
    }
}
