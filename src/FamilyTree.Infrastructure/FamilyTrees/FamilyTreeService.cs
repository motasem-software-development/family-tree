using FamilyTree.Application.Common;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.FamilyTrees;

public sealed class FamilyTreeService(
    ApplicationDbContext context,
    ITenantContext tenant,
    TimeProvider timeProvider) : IFamilyTreeService
{
    public async Task<FamilyTreeResponse> GetAsync(CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: false, ct);
        var memberCount = await context.FamilyMembers.CountAsync(ct);

        return new FamilyTreeResponse(tree.Id, tree.Name, memberCount);
    }

    public async Task<FamilyTreeResponse> RenameAsync(
        RenameFamilyTreeRequest request, CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: true, ct);

        tree.Rename(request.Name, timeProvider.GetUtcNow());
        await context.SaveChangesAsync(ct);

        var memberCount = await context.FamilyMembers.CountAsync(ct);
        return new FamilyTreeResponse(tree.Id, tree.Name, memberCount);
    }

    public async Task<FamilyTreeViewResponse> GetViewAsync(
        MemberFilter filter, int? maxDepth, CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: false, ct);

        // V1 loads the whole tree and shapes it in memory (design spec §4.5). The parameters
        // are honoured server-side so switching to a windowed query later changes only this
        // method, never the contract.
        //
        // The filter is applied during assembly rather than in SQL, deliberately (design spec
        // §4.2): the ancestor rule needs matches *and* their ancestor chains, which is a
        // materially harder query than filtering a list already in hand. This read still goes
        // through the tenant query filter, unlike the members list.
        var members = await context.FamilyMembers.AsNoTracking().ToListAsync(ct);

        return new FamilyTreeViewResponse(
            tree.Id, tree.Name, FamilyTreeAssembler.Assemble(members, filter, maxDepth));
    }

    public Task<IReadOnlyList<BranchResponse>> ListBranchesAsync(
        Guid? rootId, CancellationToken ct = default) =>
        // Raw SQL, so the tenant predicate is explicit rather than inherited from the query
        // filter — see FamilyMemberQuery's class comment.
        FamilyMemberQuery.ListBranchesAsync(context, tenant.TenantId, rootId, ct);

    public Task<IReadOnlyList<int>> ListGenerationsAsync(
        Guid? rootId, CancellationToken ct = default) =>
        FamilyMemberQuery.ListGenerationsAsync(context, tenant.TenantId, rootId, ct);

    private async Task<FamilyTreeAggregate> LoadTreeAsync(bool tracked, CancellationToken ct)
    {
        var query = tracked ? context.FamilyTrees : context.FamilyTrees.AsNoTracking();

        // Filtered: a caller whose tenant has no tree gets the same 404 as an unknown one.
        return await query.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");
    }
}
