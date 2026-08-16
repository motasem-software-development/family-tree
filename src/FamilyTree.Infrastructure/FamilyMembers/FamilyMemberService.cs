using FamilyTree.Application.Common;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        await context.SaveChangesAsync(ct);

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

    internal static FamilyMemberResponse Map(FamilyMember member) => new(
        member.Id, member.Name, member.ParentId, member.Version, member.CreatedAt, member.UpdatedAt);
}
