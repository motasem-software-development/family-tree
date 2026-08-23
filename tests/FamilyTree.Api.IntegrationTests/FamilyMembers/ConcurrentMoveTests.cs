using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.FamilyMembers;

/// <summary>
/// Two moves that are each acyclic against their own snapshot can jointly close a loop: A under
/// B while B goes under A. Each context is a separate connection, so this is the real race
/// rather than a simulation of it — the per-tenant advisory lock in MoveAsync is the only thing
/// that makes it come out right.
/// </summary>
public sealed class ConcurrentMoveTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static IFamilyMemberService ServiceFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    [Fact]
    public async Task Two_moves_that_would_close_a_loop_cannot_both_succeed()
    {
        Guid tenantId;
        await using (var seed = ContextFor(Guid.Empty))
        {
            var tenant = Tenant.Create("Tenant race", "mv-race", Now);
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            seed.FamilyTrees.Add(FamilyTreeAggregate.Create(tenant.Id, "Tree race", Now));
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        FamilyMemberResponse first, second;
        await using (var context = ContextFor(tenantId))
        {
            var service = ServiceFor(context, tenantId);
            first = await service.CreateAsync(new CreateFamilyMemberRequest("سليمان", null), default);
            second = await service.CreateAsync(new CreateFamilyMemberRequest("داوود", null), default);
        }

        // Separate contexts, therefore separate connections and separate transactions.
        await using var contextA = ContextFor(tenantId);
        await using var contextB = ContextFor(tenantId);

        var moveA = Task.Run(async () =>
        {
            try
            {
                await ServiceFor(contextA, tenantId).MoveAsync(
                    first.Id, new MoveFamilyMemberRequest(second.Id, first.Version), default);
                return true;
            }
            catch (Exception) { return false; }
        });

        var moveB = Task.Run(async () =>
        {
            try
            {
                await ServiceFor(contextB, tenantId).MoveAsync(
                    second.Id, new MoveFamilyMemberRequest(first.Id, second.Version), default);
                return true;
            }
            catch (Exception) { return false; }
        });

        var outcomes = await Task.WhenAll(moveA, moveB);

        // Exactly one, not "at most one": the lock is held to commit, so the loser reads the
        // winner's committed row and is refused. A run where NEITHER move lands means the
        // command is broken, not that the lock worked.
        outcomes.Count(succeeded => succeeded).Should().Be(1);

        await using var verify = ContextFor(tenantId);
        var members = verify.FamilyMembers.ToList();
        var firstRow = members.Single(m => m.Id == first.Id);
        var secondRow = members.Single(m => m.Id == second.Id);

        // Exactly one direction landed. This states the loop's absence and the winner's
        // effect in one assertion: both true would be the cycle, neither true would be the
        // silent no-op the count above already forbids.
        var firstMoved = firstRow.ParentId == second.Id;
        var secondMoved = secondRow.ParentId == first.Id;
        (firstMoved ^ secondMoved).Should().BeTrue();
    }
}
