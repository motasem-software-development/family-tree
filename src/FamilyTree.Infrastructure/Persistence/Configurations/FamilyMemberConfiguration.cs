using FamilyTree.Domain.Countries;
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
        // The two life-detail invariants are enforced by the database as well as by the domain,
        // the same belt-and-braces the cross-tenant parent link gets in §3.3 below: Phase 2.5's
        // bulk import writes members in volume, and a check constraint cannot be bypassed by a
        // code path that forgets to call the aggregate. The "not in the future" rule is
        // deliberately NOT here — it depends on the current date, so it would have to be
        // re-evaluated on every read to stay true, and CHECK is only evaluated on write.
        builder.ToTable("family_members", table =>
        {
            table.HasCheckConstraint(
                "ck_member_death_after_birth",
                "date_of_death IS NULL OR date_of_birth IS NULL OR date_of_death >= date_of_birth");

            table.HasCheckConstraint(
                "ck_member_death_date_implies_deceased",
                "date_of_death IS NULL OR is_deceased");

            // Same belt-and-braces argument as the two constraints above: Phase 2.5's bulk
            // import writes members in volume and a CHECK cannot be bypassed by a code path
            // that forgets the aggregate. Uniqueness is a filtered index, not a CHECK — a
            // constraint cannot see other rows.
            table.HasCheckConstraint(
                "ck_member_national_id_digits",
                "national_id IS NULL OR national_id ~ '^[0-9]{9}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(FamilyMember.MaxNameLength);

        // DateOnly maps to PostgreSQL `date` — a calendar date with no time and no zone, which
        // is exactly what a birth or death date is. Storing them as timestamps would invent a
        // midnight that shifts the day under a time-zone conversion.
        builder.Property(x => x.DateOfBirth).HasColumnType("date");
        builder.Property(x => x.DateOfDeath).HasColumnType("date");
        builder.Property(x => x.IsDeceased).IsRequired().HasDefaultValue(false);

        // Text, not a numeric type: specification §4.2 requires the value to survive exactly as
        // entered, and any numeric column would eat a leading zero.
        builder.Property(x => x.NationalId).HasMaxLength(9);
        builder.Property(x => x.MobileNumber).HasMaxLength(20);
        builder.Property(x => x.WhatsAppNumber).HasMaxLength(20);

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

        // Restrict, not Cascade: a country is reference data, and deleting one must never
        // silently delete the people who live there. In practice countries are never deleted;
        // the constraint is what makes that a guarantee rather than a habit.
        builder.HasOne<Country>()
               .WithMany()
               .HasForeignKey(x => x.CountryId)
               .OnDelete(DeleteBehavior.Restrict);

        // Technical specification §12. The (family_tree_id, parent_id) index carries tree
        // traversal — "give me the children of this member" — which is the hot path.
        builder.HasIndex(x => x.FamilyTreeId);
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => new { x.FamilyTreeId, x.ParentId });
        builder.HasIndex(x => new { x.FamilyTreeId, x.Name });
        builder.HasIndex(x => x.TenantId);

        // Design §2.3. Per-tenant, not global: two tenants are unrelated families, and a global
        // unique index would let one tenant's write fail because of a row it cannot see —
        // leaking the existence of that record across the boundary. Filtered on NOT NULL
        // because the overwhelming majority of members have no recorded ID and nulls must not
        // collide with each other.
        builder.HasIndex(x => new { x.TenantId, x.NationalId })
               .HasDatabaseName("ux_family_members_tenant_national_id")
               .IsUnique()
               .HasFilter("national_id IS NOT NULL");

        // Specification §25 — both are filter predicates on the members list.
        builder.HasIndex(x => x.CountryId);
        builder.HasIndex(x => x.IsDeceased);
    }
}
