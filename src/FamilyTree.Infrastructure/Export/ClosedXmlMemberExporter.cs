using ClosedXML.Excel;
using FamilyTree.Application.Common;
using FamilyTree.Application.Countries;
using FamilyTree.Application.Export;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// The only file in the codebase that touches ClosedXML — the same split
/// <see cref="SkiaTreeRenderer"/> gives the PDF export (design spec §7.1). Everything about
/// <i>what</i> goes in a cell lives in <see cref="MemberExportRows"/>, in Application, where it is
/// testable without producing a workbook. This file decides only where the cell goes and what
/// type it is.
///
/// It runs <see cref="FamilyMemberQuery"/> itself rather than accepting a client-supplied id
/// list, which is what makes specification §18's "export respects filters" and §27's "export
/// respects permissions" one guarantee rather than two (design spec §4.1).
/// </summary>
public sealed class ClosedXmlMemberExporter(
    ApplicationDbContext context,
    ITenantContext tenant,
    ICountryService countries) : IMemberExcelExporter
{
    /// <summary>1-based, matching ClosedXML. Specification §19's order.</summary>
    private const int NationalIdColumn = 1;
    private const int MobileColumn = 3;
    private const int WhatsAppColumn = 4;
    private const int ColumnCount = 8;

    public async Task<ExcelExportResult> ExportAsync(
        MemberFilter filter, CaptionLanguage language, CancellationToken ct = default)
    {
        var tree = await context.FamilyTrees.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");

        // The same query, the same filter, the same limit the list endpoint uses. No streaming
        // and no size cap: 351 members is a small in-memory workbook (design spec §7.4), and a
        // workbook of strings is nothing like a rendered document of the same member count — the
        // PDF exporter's MemberCap is deliberately not copied here.
        var members = await FamilyMemberQuery.ListAsync(
            context, tenant.TenantId, filter, FamilyMemberQuery.NoLimit, 0, ct);

        // The whole family, names and parents only — not the filtered rows. The Full Name column
        // walks the parent chain, and a filtered list has holes in it: composing from it would
        // drop a father the filter excluded and hand the user a different name than the page
        // just showed them. Goes through the tenant query filter, unlike the raw-SQL list above.
        var lineage = await context.FamilyMembers
            .AsNoTracking()
            .Select(m => new { m.Id, m.Name, m.ParentId })
            .ToDictionaryAsync(m => m.Id, m => new NamedMember(m.Name, m.ParentId), ct);

        var rows = MemberExportRows.Build(members, lineage, await countries.ListAsync(ct), language);

        return new ExcelExportResult(Write(rows, language), tree.Name);
    }

    private static byte[] Write(IReadOnlyList<MemberExportRow> rows, CaptionLanguage language)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Members");

        // Specification §19's columns read right to left in Arabic; without this the first column
        // lands on the left and the order reads backwards (design spec §7.4).
        sheet.RightToLeft = language is CaptionLanguage.Ar;

        var headers = MemberExportRows.Headers(language);
        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }
        sheet.Row(1).Style.Font.Bold = true;

        // Set before writing, not after: Excel decides a cell's type from the value it is given,
        // so a national ID typed afterwards has already lost its leading zero, and a phone number
        // beginning "+" has already been read as a formula (design spec §7.3).
        foreach (var column in new[] { NationalIdColumn, MobileColumn, WhatsAppColumn })
        {
            sheet.Column(column).Style.NumberFormat.Format = "@";
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var line = index + 2;

            SetText(sheet, line, NationalIdColumn, row.NationalId);
            sheet.Cell(line, 2).Value = row.FullName;
            SetText(sheet, line, MobileColumn, row.MobileNumber);
            SetText(sheet, line, WhatsAppColumn, row.WhatsAppNumber);
            sheet.Cell(line, 5).Value = row.Country;
            sheet.Cell(line, 6).Value = row.Branch;
            // The one column that should sort and filter numerically: it is a count.
            sheet.Cell(line, 7).Value = row.Generation;
            sheet.Cell(line, 8).Value = row.Status;
        }

        sheet.Columns(1, ColumnCount).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes an identifier as text. <c>SetValue&lt;string&gt;</c> rather than the untyped
    /// setter: the untyped one infers a type from the string's content, which is exactly the
    /// inference design spec §7.3 exists to prevent.
    /// </summary>
    private static void SetText(IXLWorksheet sheet, int row, int column, string value)
    {
        var cell = sheet.Cell(row, column);
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(value);
    }
}
