using System.Diagnostics;

namespace FamilyTree.Application.Tests.Export;

/// <summary>Extracts a PDF's text layer with poppler's pdftotext.</summary>
public static class PdfText
{
    public static string Extract(string pdfPath)
    {
        var output = Path.ChangeExtension(pdfPath, ".txt");

        // -enc UTF-8 pins the output encoding explicitly rather than relying on the installed
        // tool's default (Xpdf's pdftotext defaults to Latin1, which cannot represent Arabic at
        // all and would fail this gate regardless of renderer correctness).
        using var process = Process.Start(
            new ProcessStartInfo("pdftotext", $"-enc UTF-8 \"{pdfPath}\" \"{output}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("pdftotext is not installed");

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());

        try { return File.ReadAllText(output); }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
