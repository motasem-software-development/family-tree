using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(Tenant.MaxNameLength);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(Tenant.MaxSlugLength);
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}
