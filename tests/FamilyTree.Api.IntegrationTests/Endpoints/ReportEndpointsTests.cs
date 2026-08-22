using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Auth;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.Authorization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class ReportEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
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

    // NOTE: deviation from the brief's literal listing. The brief minted every token against a
    // random Guid tenant, but ReportService.GetAsync 404s a tenant with no family tree — by
    // design, "exactly as FamilyTreeService.LoadTreeAsync does" (see its comment) — and
    // AuthorizationTests documents the same random-tenant-token 404 for GET /api/v1/family-tree.
    // A random tenant can never see 200 here, so the OK-path tests authenticate as the seeded
    // tenant instead, following AdministratorGuardTests' SeededTenantIdAsync pattern. The
    // unauthenticated/forbidden tests are unaffected and keep the brief's random tenant.
    private void AuthenticateWith(Guid tenantId, params string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokens.CreateAccessToken(
            Guid.CreateVersion7(), tenantId, "someone@example.com", permissions,
            mustChangePassword: false).Value;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var response = await _client.GetAsync("/api/v1/reports");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Design §4: reports reuse FamilyTree.View. A token carrying an unrelated permission must
    /// not open them.
    /// </summary>
    [Fact]
    public async Task A_token_without_family_tree_view_is_forbidden()
    {
        AuthenticateWith(Guid.CreateVersion7(), Permissions.Audit.View);

        var response = await _client.GetAsync("/api/v1/reports");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_carrying_family_tree_view_is_admitted()
    {
        AuthenticateWith(await _factory.SeededTenantIdAsync(), Permissions.FamilyTree.View);

        var response = await _client.GetAsync("/api/v1/reports");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_response_carries_all_five_sections_and_the_reference_day()
    {
        AuthenticateWith(await _factory.SeededTenantIdAsync(), Permissions.FamilyTree.View);

        var report = await _client.GetFromJsonAsync<ReportsResponse>("/api/v1/reports");

        report.Should().NotBeNull();
        report!.Structure.Should().NotBeNull();
        report.LifeStatus.Should().NotBeNull();
        report.Completeness.Should().NotBeNull();
        report.Upcoming.Should().NotBeNull();
        report.Activity.Should().NotBeNull();
        report.GeneratedOn.Should().NotBe(default);
    }

    /// <summary>The fixed windows are contract, so a client can label the screen from them.</summary>
    [Fact]
    public async Task The_windows_are_reported_so_a_client_need_not_hardcode_them()
    {
        AuthenticateWith(await _factory.SeededTenantIdAsync(), Permissions.FamilyTree.View);

        var report = await _client.GetFromJsonAsync<ReportsResponse>("/api/v1/reports");

        report!.Upcoming.WindowDays.Should().Be(30);
        report.Activity.WindowDays.Should().Be(30);
    }
}
