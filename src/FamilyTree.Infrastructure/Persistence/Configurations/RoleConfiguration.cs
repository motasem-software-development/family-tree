using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(Role.MaxNameLength);
        builder.Property(x => x.Description).HasMaxLength(500);

        // Role names are unique within a tenant, not globally.
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
