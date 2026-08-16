using FamilyTree.Application.Auth;
using FamilyTree.Application.Authorization;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Infrastructure.Authorization;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // AddDbContext, not AddDbContextPool: the context holds per-request tenant state,
        // and a pooled instance reused across requests would leak tenant scope. See plan header.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
                   .UseSnakeCaseNamingConvention());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
