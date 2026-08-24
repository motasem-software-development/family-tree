using FamilyTree.Application.FamilyMembers;

namespace FamilyTree.Application.Export;

/// <returns>The workbook bytes, and the family tree name used for the download filename.</returns>
public sealed record ExcelExportResult(byte[] Content, string FamilyTreeName);

/// <summary>
/// The Infrastructure seam for the members export, shaped like <see cref="IFamilyTreeExporter"/>:
/// Application defines the contract and owns the row-building logic, Infrastructure owns
/// ClosedXML. That split is what keeps the package out of this project (design spec §7.1).
///
/// The implementation re-runs the same filtered query the list endpoint uses rather than
/// accepting a client-supplied id list. That is what makes specification §18's "export respects
/// filters" and §27's "export respects permissions" one guarantee rather than two (design
/// spec §4.1).
/// </summary>
public interface IMemberExcelExporter
{
    Task<ExcelExportResult> ExportAsync(
        MemberFilter filter, CaptionLanguage language, CancellationToken ct = default);
}
