using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Reports;

/// <summary>
/// Loads once, delegates to the pure calculators. Every statistic comes from the same member
/// list, so no two sections of one response can disagree about the tree they describe.
/// </summary>
public sealed class ReportService(
    ApplicationDbContext context, TimeProvider timeProvider) : IReportService
{
    public async Task<ReportsResponse> GetAsync(CancellationToken ct = default)
    {
        // Filtered by tenant: a caller whose tenant has no tree gets the same 404 as an
        // unknown one, exactly as FamilyTreeService.LoadTreeAsync does.
        _ = await context.FamilyTrees.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");

        // V1 loads the whole tree, matching FamilyTreeService.GetViewAsync. Switching to a
        // windowed query later changes only this method, never the contract. Loading the
        // complete tenant member list in this one query is also what upholds the
        // StructureReport precondition: with every member present, the database's composite
        // self-FK guarantees every parent link resolves, so TotalMembers matches what the tree
        // screen renders and generation 1 is exactly Branches.
        var members = await context.FamilyMembers.AsNoTracking().ToListAsync(ct);

        var now = timeProvider.GetUtcNow();
        // One reference day for the whole response. Deriving it per calculator would let a
        // request spanning midnight compute two different "todays".
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var generations = GenerationIndex.Build(members);

        return new ReportsResponse(
            GeneratedOn: today,
            Structure: StructureCalculator.Calculate(members, generations),
            LifeStatus: LifeStatusCalculator.Calculate(members, generations, today),
            Completeness: CompletenessCalculator.Calculate(members),
            Upcoming: UpcomingCalculator.Calculate(members, today),
            Activity: ActivityCalculator.Calculate(members, now));
    }
}
