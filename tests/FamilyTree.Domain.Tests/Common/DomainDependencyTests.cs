using FluentAssertions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Tests.Common;

public class DomainDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Microsoft.Extensions.DependencyInjection"
    ];

    [Fact]
    public void Domain_assembly_references_no_infrastructure_packages()
    {
        var referenced = typeof(Entity).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referenced.Should().NotContain(
            name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)),
            "the domain layer must stay free of infrastructure concerns");
    }
}
