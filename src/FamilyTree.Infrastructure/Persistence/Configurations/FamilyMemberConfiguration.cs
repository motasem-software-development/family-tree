using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable("family_members");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(FamilyMember.MaxNameLength);

        // Optimistic concurrency (design spec §3.1, technical specification §43). EF puts this
        // column in the UPDATE's WHERE clause; a stale value matches no row and raises
        // DbUpdateConcurrencyException, which the service turns into 409 CONCURRENCY_CONFLICT.
        builder.Property(x => x.Version).IsConcurrencyToken();

        // Design spec §3.3 — the pair of constraints that makes a cross-tree parent link
        // physically unrepresentable. The alternate key is what the composite self-reference
        // (added as raw DDL in the AddFamilyMembers migration — see the migration for why)
        // points at; it costs one redundant index.
        //
        // The composite self-FK itself is deliberately NOT modeled via the fluent API here.
        // EF Core's change-tracker fixup nulls the optional ParentId of a tracked dependent
        // when its tracked principal is marked for deletion, even with OnDelete(Restrict) —
        // this was verified empirically: Remove(parent) flips a tracked child to Modified with
        // ParentId cleared *before* SaveChanges runs, so the DELETE that reaches PostgreSQL
        // never violates anything and the "cannot delete a parent with children" guarantee
        // silently evaporates. Leaving EF unaware of the relationship (no HasOne/HasForeignKey
        // here) means there is no fixup to perform; the constraint is enforced purely by the
        // database via the raw SQL in the migration.
        //
        // Consequence for callers (Task 2 review, Important finding 1): because EF's command
        // batcher has no dependency edge for this relationship, it cannot topologically order a
        // parent and its child within a single SaveChanges call. Adding a parent and its child
        // in one SaveChanges, or deleting a subtree (a member and its descendants) in one
        // SaveChanges, is NOT ordering-safe — EF may emit the child's INSERT/DELETE before the
        // parent's, and PostgreSQL will reject it with 23503 (foreign_key_violation). Every
        // current code path adds or removes one FamilyMember per SaveChanges, so this does not
        // currently bite, but Phase 2.5's bulk import (~350 members per tree) must save each
        // generation (or each member) in its own SaveChanges, ordered parent-before-child on
        // insert and child-before-parent on delete.
        builder.HasAlternateKey(x => new { x.Id, x.FamilyTreeId });

        // Design spec §3.3 applied to the tenant axis (Task 2 review, Important finding 2): a
        // composite FK — not a single-column family_tree_id reference — so a member's tenant_id
        // and family_tree_id cannot independently satisfy two single-column FKs while pointing
        // at a tree owned by a different tenant. Anchored on FamilyTreeAggregate's
        // (Id, TenantId) alternate key. Both columns here are non-nullable and this is not a
        // self-reference, so the fluent route works cleanly — no change-tracker fixup hazard
        // like the self-FK above.
        builder.HasOne<FamilyTreeAggregate>()
               .WithMany()
               .HasForeignKey(x => new { x.FamilyTreeId, x.TenantId })
               .HasPrincipalKey(x => new { x.Id, x.TenantId })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tenant>()
               .WithMany()
               .HasForeignKey(x => x.TenantId)
               .OnDelete(DeleteBehavior.Restrict);

        // Technical specification §12. The (family_tree_id, parent_id) index carries tree
        // traversal — "give me the children of this member" — which is the hot path.
        builder.HasIndex(x => x.FamilyTreeId);
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => new { x.FamilyTreeId, x.ParentId });
        builder.HasIndex(x => new { x.FamilyTreeId, x.Name });
        builder.HasIndex(x => x.TenantId);
    }
}
