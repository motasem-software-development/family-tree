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
