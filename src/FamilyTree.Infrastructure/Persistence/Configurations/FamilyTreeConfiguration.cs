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

        // Design spec §3.3 applied to the tenant axis (finding raised in Task 2 review): anchors
        // family_members' composite (family_tree_id, tenant_id) foreign key, so a member's
        // tenant_id and family_tree_id cannot independently satisfy their single-column FKs
        // while still pointing at a tree that belongs to a different tenant. Without this, a
        // bug that writes the wrong tenant_id on a member would only be caught by application
        // code, and the query filter (keyed off member.tenant_id) would expose the row to the
        // wrong tenant.
        builder.HasAlternateKey(x => new { x.Id, x.TenantId });

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
