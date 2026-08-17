using System.Globalization;
using System.Text;

namespace FamilyTree.Import;

public readonly record struct Glyph(int GlyphId, string Text, double X, double Y, double Size);

public sealed record PdfPath(IReadOnlyList<(double X, double Y)> Points, string Ops, char Terminator);

public sealed record PageContent(IReadOnlyList<Glyph> Glyphs, IReadOnlyList<PdfPath> Paths);

/// <summary>
/// Interprets a decompressed PDF page-content stream into positioned glyphs and paths,
/// in page space. Tracks the graphics-state matrix stack (q/Q/cm) and the text matrix
/// (Tm, and Td/TD which translate it) exactly as the PDF imaging model requires: within a
/// BT/ET block only the first glyph is usually preceded by a fresh Tm — subsequent glyphs
/// are commonly positioned by a chain of Td translations, so both must be tracked to get
/// correct glyph coordinates.
/// </summary>
public static class ContentStream
{
    private static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

    public static PageContent Read(byte[] content, ToUnicodeCMap cmap)
    {
        var tokens = Encoding.Latin1.GetString(content)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var glyphs = new List<Glyph>();
        var paths = new List<PdfPath>();

        var ctm = (double[])Identity.Clone();
        var ctmStack = new Stack<double[]>();
        var tm = (double[])Identity.Clone();
        var fontSize = 0d;

        var activePoints = new List<(double X, double Y)>();
        var activeOps = new StringBuilder();
        var pathActive = false;

        for (var i = 0; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "q":
                    ctmStack.Push((double[])ctm.Clone());
                    break;

                case "Q":
                    if (ctmStack.Count > 0) ctm = ctmStack.Pop();
                    break;

                case "cm":
                    ctm = Multiply(Operands(tokens, i, 6), ctm);
                    break;

                case "BT":
                case "ET":
                    // Per spec, BT resets the text matrix (and the line matrix it doubles as
                    // here) to identity; ET has no further effect but is reset too for symmetry.
                    tm = (double[])Identity.Clone();
                    break;

                case "Tm":
                    tm = Operands(tokens, i, 6);
                    break;

                case "Td":
                case "TD":
                    {
                        var t = Operands(tokens, i, 2);
                        tm = Multiply(new[] { 1, 0, 0, 1, t[0], t[1] }, tm);
                        break;
                    }

                case "Tf":
                    fontSize = ParseNum(tokens[i - 1]);
                    break;

                case "Tj":
                    {
                        // One Tj here draws exactly one glyph: the first 4 hex digits of the
                        // operand are the glyph id, resolved through the ToUnicode CMap. The
                        // glyph's page position is the origin (0,0) transformed by Tm x CTM.
                        var hex = tokens[i - 1].Trim('<', '>');
                        var glyphId = Convert.ToInt32(hex[..Math.Min(4, hex.Length)], 16);
                        var full = Multiply(tm, ctm);
                        glyphs.Add(new Glyph(glyphId, cmap.Lookup(glyphId) ?? "", full[4], full[5], fontSize));
                        break;
                    }

                case "m":
                    {
                        var p = Operands(tokens, i, 2);
                        activePoints = new List<(double, double)> { TransformPoint(p[0], p[1], ctm) };
                        activeOps.Clear();
                        pathActive = true;
                        break;
                    }

                case "l":
                    if (pathActive)
                    {
                        var p = Operands(tokens, i, 2);
                        activePoints.Add(TransformPoint(p[0], p[1], ctm));
                        activeOps.Append('l');
                    }
                    break;

                case "c":
                    if (pathActive)
                    {
                        // Take only the curve's endpoint (the last x,y pair of the six
                        // operands), which is what bounding boxes and endpoints need.
                        var p = Operands(tokens, i, 2);
                        activePoints.Add(TransformPoint(p[0], p[1], ctm));
                        activeOps.Append('c');
                    }
                    break;

                case "S":
                case "f":
                case "h":
                case "B":
                case "n":
                    if (pathActive)
                    {
                        paths.Add(new PdfPath(activePoints, activeOps.ToString(), tokens[i][0]));
                        pathActive = false;
                    }
                    break;
            }
        }

        return new PageContent(glyphs, paths);
    }

    /// <summary>
    /// Path construction operators (m/l/c) record points in the coordinate space active at
    /// draw time, which is local to whatever CTM is in effect (many PDFs wrap each shape in
    /// its own q / cm / ... / Q, reusing a shared local template scaled and translated per
    /// instance). Unlike glyphs -- positioned via Tm x CTM at the point of use -- path points
    /// must be pushed through the CTM alone (no text matrix) to land in page space.
    /// </summary>
    private static (double X, double Y) TransformPoint(double x, double y, double[] ctm) =>
        (x * ctm[0] + y * ctm[2] + ctm[4], x * ctm[1] + y * ctm[3] + ctm[5]);

    private static double[] Operands(string[] tokens, int operatorIndex, int count)
    {
        var result = new double[count];
        for (var k = 0; k < count; k++)
            result[k] = ParseNum(tokens[operatorIndex - count + k]);
        return result;
    }

    private static double ParseNum(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>
    /// PDF affine matrix composition m x n, both given as [a b c d e f]:
    /// [a*a'+b*c', a*b'+b*d', c*a'+d*c', c*b'+d*d', e*a'+f*c'+e', e*b'+f*d'+f'].
    /// </summary>
    private static double[] Multiply(double[] m, double[] n) =>
    [
        m[0] * n[0] + m[1] * n[2],
        m[0] * n[1] + m[1] * n[3],
        m[2] * n[0] + m[3] * n[2],
        m[2] * n[1] + m[3] * n[3],
        m[4] * n[0] + m[5] * n[2] + n[4],
        m[4] * n[1] + m[5] * n[3] + n[5],
    ];
}
