using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

/// <summary>
/// The members workbook, read back with ClosedXML (design spec §7). The cell <b>types</b> are the
/// point of most of these: a national ID written as a number and one written as text render
/// identically to a test that only reads the displayed value, and the type is exactly what
/// design spec §7.3 exists to pin.
/// </summary>
[Collection("postgres")]
public sealed class MemberExcelExportTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string ExportPath = "/api/v1/family-members/export.xlsx";
    private const string XlsxMediaType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private async Task<FamilyMemberResponse> CreateAsync(
        Guid? parentId,
        string name,
        string? nationalId = null,
        string? mobile = null,
        bool isDeceased = false) =>
        (await (await _client.PostAsJsonAsync(
            "/api/v1/family-members",
            new CreateFamilyMemberRequest(name, parentId)
            {
                NationalId = nationalId,
                MobileNumber = mobile,
                IsDeceased = isDeceased
            })).Content.ReadFromJsonAsync<FamilyMemberResponse>())!;

    private async Task<FamilyMemberListItem> RootAsync() =>
        (await _client.GetFromJsonAsync<List<FamilyMemberListItem>>("/api/v1/family-members"))!
        .Single(m => m.ParentId is null);

    /// <summary>Downloads the workbook and opens it. The caller owns the returned workbook.</summary>
    private async Task<XLWorkbook> DownloadAsync(string query = "", string language = "en")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ExportPath}{query}");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(XlsxMediaType);

        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    private static IXLWorksheet SheetOf(XLWorkbook workbook) => workbook.Worksheets.First();

    private static string CellText(IXLWorksheet sheet, int row, int column) =>
        sheet.Cell(row, column).GetString();

    /// <summary>The data rows, excluding the header. One per member.</summary>
    private static int DataRowCount(IXLWorksheet sheet) => sheet.LastRowUsed()!.RowNumber() - 1;

    [Fact]
    public async Task The_workbook_has_a_header_row_and_one_row_per_member()
    {
        await AuthenticateAsync();
        var members = await _client.GetFromJsonAsync<List<FamilyMemberListItem>>(
            "/api/v1/family-members");

        using var workbook = await DownloadAsync();
        var sheet = SheetOf(workbook);

        CellText(sheet, 1, 1).Should().Be("National ID");
        CellText(sheet, 1, 8).Should().Be("Status");
        DataRowCount(sheet).Should().Be(members!.Count);
    }

    [Fact]
    public async Task The_headers_follow_the_requested_language()
    {
        await AuthenticateAsync();

        using var arabic = await DownloadAsync(language: "ar");

        CellText(SheetOf(arabic), 1, 1).Should().Be("رقم الهوية");
    }

    [Fact]
    public async Task The_sheet_is_right_to_left_only_in_arabic()
    {
        // Specification §19's columns read right to left in Arabic; without this the first column
        // lands on the left and the order reads backwards (design spec §7.4).
        await AuthenticateAsync();

        using var arabic = await DownloadAsync(language: "ar");
        using var english = await DownloadAsync(language: "en");

        SheetOf(arabic).RightToLeft.Should().BeTrue();
        SheetOf(english).RightToLeft.Should().BeFalse();
    }

    [Fact]
    public async Task The_identifier_columns_are_text_cells_not_numbers()
    {
        // The assertion with teeth: 123456789 as a number and as text look the same to a test
        // that reads only the displayed value.
        await AuthenticateAsync();
        var root = await RootAsync();
        var member = await CreateAsync(root.Id, "زياد المصدَّر", "123456789", "+970599123456");

        using var workbook = await DownloadAsync($"?search={Uri.EscapeDataString(member.Name)}");
        var sheet = SheetOf(workbook);

        sheet.Cell(2, 1).DataType.Should().Be(XLDataType.Text);
        sheet.Cell(2, 3).DataType.Should().Be(XLDataType.Text);
        sheet.Cell(2, 4).DataType.Should().Be(XLDataType.Text);
    }

    [Fact]
    public async Task A_leading_zero_national_id_survives_the_round_trip()
    {
        await AuthenticateAsync();
        var root = await RootAsync();
        var member = await CreateAsync(root.Id, "خالد المصدَّر", "012345678");

        using var workbook = await DownloadAsync($"?search={Uri.EscapeDataString(member.Name)}");

        CellText(SheetOf(workbook), 2, 1).Should().Be("012345678");
    }

    [Fact]
    public async Task A_phone_number_keeps_its_plus_and_is_not_a_formula()
    {
        await AuthenticateAsync();
        var root = await RootAsync();
        var member = await CreateAsync(root.Id, "سالم المصدَّر", mobile: "+970599123456");

        using var workbook = await DownloadAsync($"?search={Uri.EscapeDataString(member.Name)}");
        var cell = SheetOf(workbook).Cell(2, 3);

        cell.GetString().Should().Be("+970599123456");
        cell.HasFormula.Should().BeFalse();
    }

    [Fact]
    public async Task Generation_is_a_number_cell()
    {
        // The one column that should sort and filter numerically in Excel: it is a count.
        await AuthenticateAsync();

        using var workbook = await DownloadAsync();

        SheetOf(workbook).Cell(2, 7).DataType.Should().Be(XLDataType.Number);
    }

    [Fact]
    public async Task The_root_reads_root_rather_than_blank()
    {
        await AuthenticateAsync();

        using var workbook = await DownloadAsync("?generation=0");
        var sheet = SheetOf(workbook);

        DataRowCount(sheet).Should().Be(1);
        CellText(sheet, 2, 6).Should().Be("Root");
        sheet.Cell(2, 7).GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task The_export_respects_the_filter()
    {
        // Design spec §8 asks for this asserted by row count against the same filter run through
        // the list endpoint — one query, two callers, one answer.
        //
        // The deceased members are created here rather than relied on from the seed: the imported
        // family predates the life-details migration, so every seeded member is alive on a freshly
        // migrated database and the filter would narrow to nothing.
        await AuthenticateAsync();
        var root = await RootAsync();
        var branch = await CreateAsync(root.Id, "فرع التصدير");
        await CreateAsync(branch.Id, "متوفى الأول", isDeceased: true);
        await CreateAsync(branch.Id, "متوفى الثاني", isDeceased: true);
        await CreateAsync(branch.Id, "حي واحد");

        const string query = "?status=deceased&generation=2";

        var listed = await _client.GetFromJsonAsync<List<FamilyMemberListItem>>(
            $"/api/v1/family-members{query}");
        using var workbook = await DownloadAsync(query);

        listed!.Should().HaveCount(2, "the filter must narrow to something for this to mean anything");
        DataRowCount(SheetOf(workbook)).Should().Be(listed.Count);
    }

    [Fact]
    public async Task An_empty_result_still_produces_a_workbook_with_its_headers()
    {
        // A zero-byte file, or one with no headers, reads as a broken download rather than as an
        // empty answer.
        await AuthenticateAsync();

        using var workbook = await DownloadAsync("?generation=999");
        var sheet = SheetOf(workbook);

        CellText(sheet, 1, 1).Should().Be("National ID");
        DataRowCount(sheet).Should().Be(0);
    }

    [Fact]
    public async Task The_full_name_walks_the_parent_chain()
    {
        await AuthenticateAsync();
        var root = await RootAsync();
        var father = await CreateAsync(root.Id, "سليمان المصدَّر");
        var son = await CreateAsync(father.Id, "فارس المصدَّر");

        using var workbook = await DownloadAsync($"?search={Uri.EscapeDataString(son.Name)}");

        CellText(SheetOf(workbook), 2, 2)
            .Should().Be($"{son.Name} {father.Name} {root.Name}");
    }

    [Fact]
    public async Task The_download_is_an_attachment_named_for_the_family()
    {
        await AuthenticateAsync();

        var disposition = (await _client.GetAsync(ExportPath)).Content.Headers.ContentDisposition!;

        disposition.DispositionType.Should().Be("attachment");
        // Arabic must travel percent-encoded in filename*, never raw in filename — the bug the
        // PDF endpoint's own test pins, and a second endpoint must not reintroduce it.
        disposition.FileNameStar.Should().NotBeNullOrWhiteSpace();
        disposition.FileNameStar.Should().EndWith(".xlsx");
    }

    [Fact]
    public async Task An_unrecognised_status_is_the_same_400_the_list_gives()
    {
        // Three callers, one code — this is the third caller MemberFilterBinding was extracted for.
        await AuthenticateAsync();

        var response = await _client.GetAsync($"{ExportPath}?status=dead");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("FILTER_INVALID_STATUS");
    }

    [Fact]
    public async Task The_export_requires_authentication()
    {
        var response = await _client.GetAsync(ExportPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
