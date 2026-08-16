using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class FamilyTreeConfiguration : IEntityTypeConfiguration<FamilyTreeAggregate>
{
    public void Configure(EntityTypeBuilder<FamilyTreeAggregate> builder)
    {
        builder.ToTable("family_trees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(FamilyTreeAggregate.MaxNameLength);

        // BR-001: one customer owns exactly one family tree in V1.
        builder.HasIndex(x => x.TenantId).IsUnique();

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
