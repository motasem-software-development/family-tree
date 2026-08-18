using FamilyTree.Application.Common;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Str0ng!Seed#Password";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Jwt:Issuer", "https://localhost:5001");
        builder.UseSetting("Jwt:Audience", "familytree-api");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-at-least-32-bytes-long!!");
        builder.UseSetting("Seed:TenantName", "Al-Saqqa Family");
        builder.UseSetting("Seed:TenantSlug", "al-saqqa");
        builder.UseSetting("Seed:FamilyTreeName", "عائلة السقا");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);

        // Same claims-based context the API uses, plus an override for tests that call a
        // service directly instead of over HTTP. With no override set the behaviour is
        // identical to production. See OverridableTenantContext.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContext>();
            services.AddScoped<OverridableTenantContext>();
            services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<OverridableTenantContext>());
        });
    }

    /// <summary>
    /// A scope acting as <paramref name="tenantId"/>, as an authenticated request for that
    /// tenant would. Nothing else may be resolved before the override is set.
    /// </summary>
    public AsyncServiceScope CreateTenantScope(Guid tenantId)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OverridableTenantContext>().Override = tenantId;
        return scope;
    }

    /// <summary>The tenant created by <see cref="ResetAndSeedAsync"/>.</summary>
    public async Task<Guid> SeededTenantIdAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Tenants.Select(t => t.Id).SingleAsync();
    }

    /// <summary>Migrates and seeds a clean database for the test class.</summary>
    public async Task ResetAndSeedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }
}
