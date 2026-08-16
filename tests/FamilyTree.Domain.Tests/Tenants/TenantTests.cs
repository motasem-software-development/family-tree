using FluentAssertions;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.Tenants;

namespace FamilyTree.Domain.Tests.Tenants;

public class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_assigns_identity_timestamps_and_active_state()
    {
        var tenant = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);

        tenant.Id.Should().NotBeEmpty();
        tenant.Name.Should().Be("Al-Saqqa Family");
        tenant.Slug.Should().Be("al-saqqa");
        tenant.IsActive.Should().BeTrue();
        tenant.CreatedAt.Should().Be(Now);
        tenant.UpdatedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var act = () => Tenant.Create(name, "al-saqqa", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("TENANT_NAME_REQUIRED");
    }

    [Fact]
    public void Create_rejects_name_longer_than_200_characters()
    {
        var act = () => Tenant.Create(new string('x', 201), "al-saqqa", Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("TENANT_NAME_TOO_LONG");
    }

    [Theory]
    [InlineData("Al Saqqa")]
    [InlineData("al_saqqa")]
    [InlineData("-al-saqqa")]
    [InlineData("AL-SAQQA")]
    public void Create_rejects_slug_that_is_not_lowercase_kebab_case(string slug)
    {
        var act = () => Tenant.Create("Al-Saqqa Family", slug, Now);

        act.Should().Throw<DomainException>()
           .Which.Code.Should().Be("TENANT_SLUG_INVALID");
    }

    [Fact]
    public void Rename_changes_name_and_advances_updated_at()
    {
        var tenant = Tenant.Create("Old", "old", Now);
        var later = Now.AddDays(1);

        tenant.Rename("New", later);

        tenant.Name.Should().Be("New");
        tenant.UpdatedAt.Should().Be(later);
        tenant.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Deactivate_then_activate_round_trips_is_active()
    {
        var tenant = Tenant.Create("Al-Saqqa Family", "al-saqqa", Now);

        tenant.Deactivate(Now.AddHours(1));
        tenant.IsActive.Should().BeFalse();

        tenant.Activate(Now.AddHours(2));
        tenant.IsActive.Should().BeTrue();
        tenant.UpdatedAt.Should().Be(Now.AddHours(2));
    }
}
