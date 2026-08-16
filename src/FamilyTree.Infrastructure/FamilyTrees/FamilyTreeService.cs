using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.FamilyTrees;

public sealed class FamilyTreeService(
    ApplicationDbContext context,
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
        Guid? rootId, int? maxDepth, CancellationToken ct = default)
    {
        var tree = await LoadTreeAsync(tracked: false, ct);

        // V1 loads the whole tree and shapes it in memory (design spec §4.5). The parameters
        // are honoured server-side so switching to a windowed query later changes only this
        // method, never the contract.
        var members = await context.FamilyMembers.AsNoTracking().ToListAsync(ct);

        return new FamilyTreeViewResponse(
            tree.Id, tree.Name, FamilyTreeAssembler.Assemble(members, rootId, maxDepth));
    }

    private async Task<FamilyTreeAggregate> LoadTreeAsync(bool tracked, CancellationToken ct)
    {
        var query = tracked ? context.FamilyTrees : context.FamilyTrees.AsNoTracking();

        // Filtered: a caller whose tenant has no tree gets the same 404 as an unknown one.
        return await query.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");
    }
}
