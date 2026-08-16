using FamilyTree.Application.Common;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// Every query here runs through the tenant query filter, so "not found" and "belongs to
/// another tenant" are the same code path — which is what makes the uniform 404 in design
/// spec §4.4 true by construction rather than by discipline.
/// </summary>
public sealed class FamilyMemberService(
    ApplicationDbContext context,
    ITenantContext tenant,
    TimeProvider timeProvider) : IFamilyMemberService
{
    /// <summary>PostgreSQL SQLSTATE for a foreign key violation.</summary>
    private const string ForeignKeyViolation = "23503";

    public async Task<FamilyMemberResponse> CreateAsync(
        CreateFamilyMemberRequest request, CancellationToken ct = default)
    {
        var tree = await context.FamilyTrees.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");

        if (request.ParentId is { } parentId && parentId != Guid.Empty)
        {
            // Filtered lookup: a parent in another tenant is simply not there.
            var parentExists = await context.FamilyMembers
                .AnyAsync(m => m.Id == parentId && m.FamilyTreeId == tree.Id, ct);

            if (!parentExists)
                throw new DomainException("MEMBER_PARENT_NOT_FOUND", "The specified parent does not exist.");
        }

        var member = FamilyMember.Create(
            tenant.TenantId, tree.Id, request.ParentId, request.Name, timeProvider.GetUtcNow());

        context.FamilyMembers.Add(member);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ForeignKeyViolation })
        {
            // The pre-check above is check-then-act and can lose a race to a concurrent delete
            // of the parent. The raw fk_member_parent violation carries no code on its own, so
            // it must be mapped to the same code the pre-check emits (spec §26 / import races).
            throw new DomainException("MEMBER_PARENT_NOT_FOUND", "The specified parent does not exist.");
        }

        return Map(member);
    }

    public async Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return member is null ? null : Map(member);
    }

    public async Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default)
    {
        var members = await context.FamilyMembers
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        return members.Select(Map).ToList();
    }

    public async Task<FamilyMemberResponse> UpdateAsync(
        Guid id, UpdateFamilyMemberRequest request, CancellationToken ct = default)
    {
        if (request.ParentId is not null || request.TenantId is not null || request.FamilyTreeId is not null)
            throw new DomainException(
                "MEMBER_FIELD_NOT_UPDATABLE",
                "Parent, tenant, and family tree cannot be changed through this endpoint.");

        // Tracked (not AsNoTracking): SaveChanges needs the entity in the change tracker.
        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // Design spec §3.2, layer 2: an explicit ownership assertion in the application
        // service, independent of the EF global query filter (layer 1). Deliberately
        // redundant today — the filter already hides other tenants' rows — so that a future
        // change to layer 1 (e.g. an .IgnoreQueryFilters() lookup) cannot silently open a
        // cross-tenant write. Same exception, same code, same message as "no such member":
        // the two cases must stay indistinguishable (design spec §4.4).
        if (member.TenantId != tenant.TenantId)
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        member.Rename(request.Name, timeProvider.GetUtcNow());

        // Load-bearing. EF builds `UPDATE ... WHERE id = @id AND version = @original`, and
        // `@original` defaults to the value it just READ — which always matches, making the
        // concurrency token inert. Substituting the version the CLIENT held is what turns a
        // stale write into a conflict instead of a silent overwrite.
        context.Entry(member).Property(m => m.Version).OriginalValue = request.Version;

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "CONCURRENCY_CONFLICT", "This member was changed by someone else. Reload and try again.");
        }

        return Map(member);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // Design spec §3.2, layer 2: see the identical assertion in UpdateAsync for rationale.
        if (member.TenantId != tenant.TenantId)
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // The FK's OnDelete(Restrict) would also stop this, but a DbUpdateException carries no
        // stable code for the client. Checking first is what makes the 409 contractual
        // (technical specification §26).
        var hasChildren = await context.FamilyMembers.AnyAsync(m => m.ParentId == id, ct);
        if (hasChildren)
            throw new ConflictException(
                "MEMBER_HAS_CHILDREN", "This member cannot be deleted because they have children.");

        context.FamilyMembers.Remove(member);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ForeignKeyViolation })
        {
            // The pre-check above raced a concurrent create of a child under this member. Map
            // the raw fk_member_parent violation to the same coded 409 the pre-check emits.
            throw new ConflictException(
                "MEMBER_HAS_CHILDREN", "This member cannot be deleted because they have children.");
        }
    }

    internal static FamilyMemberResponse Map(FamilyMember member) => new(
        member.Id, member.Name, member.ParentId, member.Version, member.CreatedAt, member.UpdatedAt);
}
