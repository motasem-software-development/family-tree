using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.Countries;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class CountryEndpointTests(PostgresFixture fixture) : IAsyncLifetime
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

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    [Fact]
    public async Task Countries_are_returned_to_any_authenticated_caller()
    {
        await AuthenticateAsync();

        var countries = await _client.GetFromJsonAsync<List<CountryResponse>>("/api/v1/countries");

        countries.Should().NotBeNull();
        countries!.Should().Contain(c => c.Code == "PS" && c.DialCode == "+970");
    }

    [Fact]
    public async Task Countries_are_refused_to_an_anonymous_caller()
    {
        var response = await _client.GetAsync("/api/v1/countries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
