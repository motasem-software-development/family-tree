using System.Text;
using FamilyTree.Api.Authorization;
using FamilyTree.Api.Endpoints.Auth;
using FamilyTree.Api.Endpoints.FamilyMembers;
using FamilyTree.Api.Endpoints.FamilyTrees;
using FamilyTree.Api.Endpoints.Me;
using FamilyTree.Api.Endpoints.Users;
using FamilyTree.Api.Errors;
using FamilyTree.Api.Middleware;
using FamilyTree.Application.Common;
using FamilyTree.Infrastructure;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorizationBuilder().AddPermissionPolicies();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<PasswordChangeGateMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapFamilyMemberEndpoints();
app.MapFamilyTreeEndpoints();
app.MapUserEndpoints();

// Seeding is idempotent and runs on startup. Schema migration is NOT run here — per the
// technical specification §48, production schema changes belong to CI/CD, never to app startup.
//
// The Testing guard is load-bearing, not tidiness: WebApplicationFactory builds and starts the
// host the moment a test touches .Services, which happens BEFORE the test's own MigrateAsync().
// Without the guard, SeedAsync would query tables that do not exist yet on a fresh Testcontainers
// database and every ApiFactory-based test class would die at host startup. ApiFactory sets
// UseEnvironment("Testing") and seeds explicitly after migrating. Do not remove this.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.Run();

public partial class Program;
