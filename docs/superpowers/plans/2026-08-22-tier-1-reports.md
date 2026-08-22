# Tier 1 Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship five read-only reports — structure, life status, data completeness, upcoming dates, recent activity — as one aggregate API endpoint and one `/reports` screen in the SPA.

**Architecture:** All arithmetic lives in `FamilyTree.Application/Reports` as pure static calculators over a flat `IReadOnlyList<FamilyMember>`, following the `FamilyTreeAssembler` precedent: no EF, no async, unit-tested in milliseconds. `ReportService` in Infrastructure loads the tenant's tree and members exactly once and delegates to every calculator. One endpoint, `GET /api/v1/reports`, guarded by the existing `FamilyTree.View` permission. No migration and no new permission.

**Tech Stack:** .NET 10, EF Core (Npgsql), xUnit + FluentAssertions, Testcontainers for integration tests. React 19 + TypeScript, TanStack Query, react-i18next, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-08-22-tier-1-reports-design.md`

## Global Constraints

- **No migration.** No entity, column, permission constant, or seed row changes. If a task seems to need one, stop and raise it.
- **Authorization:** every reports endpoint uses `Permissions.FamilyTree.View`. Never introduce a `Report.View` code.
- **Fixed windows and caps** (spec §5), defined once in `ReportLimits` and never inlined: `UpcomingWindowDays = 30`, `ActivityWindowDays = 30`, `MaxMembersPerList = 50`.
- **Every capped list returns its untruncated count** alongside the truncated rows. No exceptions.
- **Reference day** is always `DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)`. Never `DateTime.Now`, never `DateOnly.FromDateTime(DateTime.Today)`. Calculators receive the day as a parameter; they never read a clock.
- **Calculators are pure static classes** in `FamilyTree.Application/Reports`, taking `IReadOnlyList<FamilyMember>` and returning a contract record. No `DbContext`, no `async`, no i18n.
- **Dependency direction:** `Application` must not reference `Infrastructure` or `Api`. Contract records live in `FamilyTree.Contracts/Reports`.
- **Test fixtures use Arabic names** (`سليمان`, `فارس`, `محمود`, `عمر`, `داوود`), matching the existing suites.
- **Frontend:** no hardcoded user-facing strings — every label is an i18n key present in **both** `ar.json` and `en.json`, or `locales.test.ts` fails. Layout uses logical properties so RTL works unchanged.
- **Commit after every task**, using the conventional-commit types already in this repo (`feat`, `fix`, `test`, `docs`, `refactor`).

---

### Task 1: Shared report primitives

The three pieces every later calculator depends on: the tuning constants, the generation walk, and whole-year age arithmetic.

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/MemberRef.cs`
- Create: `src/FamilyTree.Application/Reports/ReportLimits.cs`
- Create: `src/FamilyTree.Application/Reports/GenerationIndex.cs`
- Create: `src/FamilyTree.Application/Reports/Ages.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/GenerationIndexTests.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/AgesTests.cs`

**Interfaces:**
- Consumes: `FamilyTree.Domain.FamilyMembers.FamilyMember` (existing).
- Produces:
  - `record MemberRef(Guid Id, string Name, Guid? ParentId)` with `static MemberRef From(FamilyMember member)`
  - `static class ReportLimits { const int UpcomingWindowDays = 30; const int ActivityWindowDays = 30; const int MaxMembersPerList = 50; }`
  - `static IReadOnlyDictionary<Guid, int> GenerationIndex.Build(IReadOnlyList<FamilyMember> members)`
  - `static int Ages.YearsBetween(DateOnly from, DateOnly to)`

- [x] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Application.Tests/Reports/GenerationIndexTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class GenerationIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    [Fact]
    public void An_empty_tree_yields_an_empty_index()
    {
        GenerationIndex.Build([]).Should().BeEmpty();
    }

    [Fact]
    public void A_parentless_member_is_generation_one()
    {
        var suleiman = Member("سليمان");

        GenerationIndex.Build([suleiman])[suleiman.Id].Should().Be(1);
    }

    [Fact]
    public void Each_step_down_the_chain_adds_a_generation()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);

        var index = GenerationIndex.Build([suleiman, faris, mahmoud]);

        index[suleiman.Id].Should().Be(1);
        index[faris.Id].Should().Be(2);
        index[mahmoud.Id].Should().Be(3);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);

        var index = GenerationIndex.Build([mahmoud, faris, suleiman]);

        index[mahmoud.Id].Should().Be(3);
    }

    /// <summary>
    /// Spec §6: the composite self-FK makes this unrepresentable in the database, but the
    /// calculator is a pure function over whatever list it is handed and must not throw.
    /// </summary>
    [Fact]
    public void A_member_whose_parent_is_absent_is_treated_as_generation_one()
    {
        var orphan = Member("داوود", Guid.CreateVersion7());

        GenerationIndex.Build([orphan])[orphan.Id].Should().Be(1);
    }

    /// <summary>A cycle must terminate, not hang. The bound is the member count.</summary>
    [Fact]
    public void A_cyclic_parent_chain_terminates()
    {
        var a = Member("عمر");
        var b = Member("خالد", a.Id);
        Reparent(a, b.Id);

        var act = () => GenerationIndex.Build([a, b]);

        act.Should().NotThrow();
    }

    /// <summary>
    /// ParentId has a private setter and no re-parent command exists before Phase 5, so the
    /// only way to build a cycle for this test is reflection. It is confined to this test.
    /// </summary>
    private static void Reparent(FamilyMember member, Guid parentId) =>
        typeof(FamilyMember)
            .GetProperty(nameof(FamilyMember.ParentId))!
            .SetValue(member, parentId);
}
```

Create `tests/FamilyTree.Application.Tests/Reports/AgesTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class AgesTests
{
    [Fact]
    public void The_day_before_a_birthday_the_age_has_not_yet_incremented()
    {
        Ages.YearsBetween(new DateOnly(1990, 8, 22), new DateOnly(2026, 8, 21)).Should().Be(35);
    }

    [Fact]
    public void On_the_birthday_the_age_increments()
    {
        Ages.YearsBetween(new DateOnly(1990, 8, 22), new DateOnly(2026, 8, 22)).Should().Be(36);
    }

    [Fact]
    public void A_birth_earlier_in_the_same_year_counts_as_zero_years()
    {
        Ages.YearsBetween(new DateOnly(2026, 1, 5), new DateOnly(2026, 8, 22)).Should().Be(0);
    }

    /// <summary>A leap-day birth measured in a common year: DateOnly.AddYears clamps to the 28th.</summary>
    [Fact]
    public void A_leap_day_birth_increments_in_a_common_year()
    {
        Ages.YearsBetween(new DateOnly(2000, 2, 29), new DateOnly(2027, 3, 1)).Should().Be(27);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~Reports"`
Expected: FAIL — the namespace `FamilyTree.Application.Reports` and its types do not exist yet (compile error).

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/MemberRef.cs`:

```csharp
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Contracts.Reports;

/// <summary>
/// A member as a report row identifies one. <paramref name="ParentId"/> is carried instead of
/// a composed full name because identity in this model comes from the lineage, and the SPA
/// already owns that rule in fullName.ts — see design §7. Composing it here would put the same
/// rule in two languages.
/// </summary>
public sealed record MemberRef(Guid Id, string Name, Guid? ParentId)
{
    public static MemberRef From(FamilyMember member) =>
        new(member.Id, member.Name, member.ParentId);
}
```

> Note: `FamilyTree.Contracts` already references `FamilyTree.Domain` — verify with
> `grep ProjectReference src/FamilyTree.Contracts/FamilyTree.Contracts.csproj`. If it does not,
> drop the `From` factory from this record and map in each calculator instead. Do not add the
> reference.

Create `src/FamilyTree.Application/Reports/ReportLimits.cs`:

```csharp
namespace FamilyTree.Application.Reports;

/// <summary>
/// The reports take no query parameters (design §4), so these are the whole of their tuning.
/// Fixed rather than caller-supplied: one cacheable response shape, and no validation surface.
/// Changing a window is a code change, which is honest for V1.
/// </summary>
public static class ReportLimits
{
    public const int UpcomingWindowDays = 30;
    public const int ActivityWindowDays = 30;

    /// <summary>
    /// Caps every member-bearing list. Each such list returns its untruncated count alongside,
    /// so a truncation is always visible in the contract (design §5).
    /// </summary>
    public const int MaxMembersPerList = 50;
}
```

Create `src/FamilyTree.Application/Reports/GenerationIndex.cs`:

```csharp
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

/// <summary>
/// Maps every member to its generation, where a parentless member is generation 1 — BR-003:
/// the root family is the family_trees row, not a member.
/// </summary>
public static class GenerationIndex
{
    public static IReadOnlyDictionary<Guid, int> Build(IReadOnlyList<FamilyMember> members)
    {
        var byId = members.ToDictionary(m => m.Id);
        var generations = new Dictionary<Guid, int>(members.Count);

        foreach (var member in members)
            generations[member.Id] = GenerationOf(member, byId, members.Count);

        return generations;
    }

    /// <summary>
    /// Walks upward, bounded by the member count exactly as FamilyTreeAssembler.GenerationOf
    /// bounds it, so a malformed chain terminates instead of looping. Stops on an unresolvable
    /// parent rather than throwing: the composite self-FK makes that unrepresentable in the
    /// database, but this is a pure function over whatever list it is handed (design §6).
    /// </summary>
    private static int GenerationOf(
        FamilyMember member, IReadOnlyDictionary<Guid, FamilyMember> byId, int bound)
    {
        var generation = 1;
        var current = member;

        while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            generation++;
            current = parent;
            if (generation > bound) break;
        }

        return generation;
    }
}
```

Create `src/FamilyTree.Application/Reports/Ages.cs`:

```csharp
namespace FamilyTree.Application.Reports;

public static class Ages
{
    /// <summary>
    /// Whole years elapsed, decremented when the anniversary has not yet come round in the
    /// target year. DateOnly.AddYears clamps 29 February to the 28th in a common year, which
    /// is what makes a leap-day birth increment on the right day.
    /// </summary>
    public static int YearsBetween(DateOnly from, DateOnly to)
    {
        var years = to.Year - from.Year;
        if (to < from.AddYears(years)) years--;
        return years;
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~Reports"`
Expected: PASS — 10 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports src/FamilyTree.Application/Reports tests/FamilyTree.Application.Tests/Reports
git commit -m "feat: add shared report primitives

GenerationIndex, whole-year age arithmetic, the fixed windows and caps,
and the MemberRef row shape. MemberRef carries ParentId rather than a
composed name so the lineage rule stays in one language (design 7)."
```

---

### Task 2: Structure report

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/StructureReport.cs`
- Create: `src/FamilyTree.Application/Reports/StructureCalculator.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/StructureCalculatorTests.cs`

**Interfaces:**
- Consumes: `GenerationIndex.Build`, `ReportLimits` (Task 1).
- Produces:
  - `record StructureReport(int TotalMembers, int Depth, IReadOnlyList<GenerationCount> Generations, IReadOnlyList<BranchSummary> Branches, int MembersWithChildren, int LeafMembers, decimal AverageChildrenPerParent)`
  - `record GenerationCount(int Generation, int Count)`
  - `record BranchSummary(Guid Id, string Name, int DescendantCount, int Depth)`
  - `static StructureReport StructureCalculator.Calculate(IReadOnlyList<FamilyMember> members, IReadOnlyDictionary<Guid, int> generations)`

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Application.Tests/Reports/StructureCalculatorTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class StructureCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    private static StructureReport Calculate(params FamilyMember[] members) =>
        StructureCalculator.Calculate(members, GenerationIndex.Build(members));

    /// <summary>سليمان → (فارس → محمود, عمر), plus a separate root داوود.</summary>
    private static FamilyMember[] TwoBranches()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);
        var omar = Member("عمر", suleiman.Id);
        var dawood = Member("داوود");
        return [suleiman, faris, mahmoud, omar, dawood];
    }

    [Fact]
    public void An_empty_tree_reports_zeros_and_no_branches()
    {
        var report = Calculate();

        report.TotalMembers.Should().Be(0);
        report.Depth.Should().Be(0);
        report.Generations.Should().BeEmpty();
        report.Branches.Should().BeEmpty();
        report.AverageChildrenPerParent.Should().Be(0m);
    }

    [Fact]
    public void Depth_is_the_deepest_generation()
    {
        Calculate(TwoBranches()).Depth.Should().Be(3);
    }

    [Fact]
    public void Generations_are_counted_in_order()
    {
        var report = Calculate(TwoBranches());

        report.Generations.Should().BeEquivalentTo(
            [new GenerationCount(1, 2), new GenerationCount(2, 2), new GenerationCount(3, 1)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_branch_counts_every_descendant_and_its_own_depth()
    {
        var report = Calculate(TwoBranches());

        var suleiman = report.Branches.Single(b => b.Name == "سليمان");
        suleiman.DescendantCount.Should().Be(3);
        suleiman.Depth.Should().Be(3);

        var dawood = report.Branches.Single(b => b.Name == "داوود");
        dawood.DescendantCount.Should().Be(0);
        dawood.Depth.Should().Be(1);
    }

    [Fact]
    public void Leaves_and_parents_partition_the_tree()
    {
        var report = Calculate(TwoBranches());

        report.MembersWithChildren.Should().Be(2);   // سليمان, فارس
        report.LeafMembers.Should().Be(3);           // محمود, عمر, داوود
        (report.MembersWithChildren + report.LeafMembers).Should().Be(report.TotalMembers);
    }

    /// <summary>Divided by parents, not by everyone: 3 children across 2 parents.</summary>
    [Fact]
    public void Average_children_counts_only_members_who_have_children()
    {
        Calculate(TwoBranches()).AverageChildrenPerParent.Should().Be(1.5m);
    }

    [Fact]
    public void A_tree_with_no_parents_reports_an_average_of_zero_rather_than_dividing_by_zero()
    {
        Calculate(Member("سليمان")).AverageChildrenPerParent.Should().Be(0m);
    }

    /// <summary>
    /// Design §5 invariants. Generation 1 is exactly the branch roots, and the report counts
    /// the same members the tree screen renders — the two assertions that catch a broken walk.
    /// </summary>
    [Fact]
    public void Generation_one_is_exactly_the_set_of_branches()
    {
        var members = TwoBranches();
        var report = Calculate(members);

        report.Generations[0].Count.Should().Be(report.Branches.Count);
        report.TotalMembers.Should().Be(members.Length);
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~StructureCalculatorTests"`
Expected: FAIL — `StructureCalculator` does not exist.

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/StructureReport.cs`:

```csharp
namespace FamilyTree.Contracts.Reports;

/// <summary>
/// Shape only, no member lists: the tree screen already browses these. Because a parent link
/// is guaranteed to resolve, <paramref name="TotalMembers"/> always equals what the tree
/// screen renders and generation 1 is exactly <paramref name="Branches"/> (design §5).
/// </summary>
public sealed record StructureReport(
    int TotalMembers,
    int Depth,
    IReadOnlyList<GenerationCount> Generations,
    IReadOnlyList<BranchSummary> Branches,
    int MembersWithChildren,
    int LeafMembers,
    decimal AverageChildrenPerParent);

public sealed record GenerationCount(int Generation, int Count);

/// <summary>One first-generation member and the subtree hanging off it.</summary>
public sealed record BranchSummary(Guid Id, string Name, int DescendantCount, int Depth);
```

Create `src/FamilyTree.Application/Reports/StructureCalculator.cs`:

```csharp
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class StructureCalculator
{
    public static StructureReport Calculate(
        IReadOnlyList<FamilyMember> members, IReadOnlyDictionary<Guid, int> generations)
    {
        var childrenByParent = members
            .Where(m => m.ParentId is not null)
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var generationCounts = generations.Values
            .GroupBy(g => g)
            .OrderBy(g => g.Key)
            .Select(g => new GenerationCount(g.Key, g.Count()))
            .ToList();

        var branches = members
            .Where(m => m.ParentId is null)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(root => BranchOf(root, childrenByParent))
            .ToList();

        // Counted over members rather than over the dictionary's keys: a key naming a member
        // outside the list would otherwise inflate the parent count and break the partition.
        var membersWithChildren = members.Count(m => childrenByParent.ContainsKey(m.Id));
        var childCount = members.Count(m => m.ParentId is not null);

        return new StructureReport(
            TotalMembers: members.Count,
            Depth: generations.Values.DefaultIfEmpty(0).Max(),
            Generations: generationCounts,
            Branches: branches,
            MembersWithChildren: membersWithChildren,
            LeafMembers: members.Count - membersWithChildren,
            AverageChildrenPerParent: membersWithChildren == 0
                ? 0m
                : Math.Round((decimal)childCount / membersWithChildren, 2));
    }

    /// <summary>
    /// Iterative depth-first walk rather than recursion: a deep imported lineage should not be
    /// able to overflow the stack in a report.
    /// </summary>
    private static BranchSummary BranchOf(
        FamilyMember root, IReadOnlyDictionary<Guid, List<FamilyMember>> childrenByParent)
    {
        var descendants = 0;
        var depth = 1;
        var stack = new Stack<(FamilyMember Member, int Level)>();
        stack.Push((root, 1));

        while (stack.Count > 0)
        {
            var (member, level) = stack.Pop();
            depth = Math.Max(depth, level);

            if (!childrenByParent.TryGetValue(member.Id, out var children)) continue;

            foreach (var child in children)
            {
                descendants++;
                stack.Push((child, level + 1));
            }
        }

        return new BranchSummary(root.Id, root.Name, descendants, depth);
    }
}
```

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~StructureCalculatorTests"`
Expected: PASS — 8 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports/StructureReport.cs src/FamilyTree.Application/Reports/StructureCalculator.cs tests/FamilyTree.Application.Tests/Reports/StructureCalculatorTests.cs
git commit -m "feat: add the structure report calculator

Generation counts, per-branch descendant totals and depth, and the
leaf/parent partition. Average children divides by parents, not by
everyone, so the number describes something."
```

---

### Task 3: Life status report

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/LifeStatusReport.cs`
- Create: `src/FamilyTree.Application/Reports/LifeStatusCalculator.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/LifeStatusCalculatorTests.cs`

**Interfaces:**
- Consumes: `GenerationIndex.Build`, `Ages.YearsBetween` (Task 1).
- Produces:
  - `record LifeStatusReport(int Living, int Deceased, IReadOnlyList<GenerationLifeStatus> ByGeneration, IReadOnlyList<AgeBracketCount> LivingAges, int LivingWithoutBirthDate, LongevityStats? Longevity)`
  - `record GenerationLifeStatus(int Generation, int Living, int Deceased)`
  - `record AgeBracketCount(string Bracket, int Count)`
  - `record LongevityStats(int Count, int MinYears, int MaxYears, int MedianYears)`
  - `static LifeStatusReport LifeStatusCalculator.Calculate(IReadOnlyList<FamilyMember> members, IReadOnlyDictionary<Guid, int> generations, DateOnly today)`

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Application.Tests/Reports/LifeStatusCalculatorTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class LifeStatusCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 22);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(
        string name,
        Guid? parentId = null,
        DateOnly? born = null,
        DateOnly? died = null,
        bool deceased = false) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now, born, died, deceased);

    private static LifeStatusReport Calculate(params FamilyMember[] members) =>
        LifeStatusCalculator.Calculate(members, GenerationIndex.Build(members), Today);

    [Fact]
    public void An_empty_tree_reports_nothing_measurable()
    {
        var report = Calculate();

        report.Living.Should().Be(0);
        report.Deceased.Should().Be(0);
        report.Longevity.Should().BeNull();
    }

    [Fact]
    public void Members_split_by_the_deceased_flag()
    {
        var report = Calculate(
            Member("سليمان", deceased: true),
            Member("فارس"),
            Member("عمر"));

        report.Living.Should().Be(2);
        report.Deceased.Should().Be(1);
    }

    /// <summary>
    /// The flag, never `DateOfDeath is not null` — the domain deliberately allows a member
    /// known to have died whose date is lost.
    /// </summary>
    [Fact]
    public void A_deceased_member_without_a_death_date_still_counts_as_deceased()
    {
        Calculate(Member("سليمان", deceased: true)).Deceased.Should().Be(1);
    }

    [Fact]
    public void The_split_is_reported_per_generation()
    {
        var suleiman = Member("سليمان", deceased: true);
        var faris = Member("فارس", suleiman.Id);

        var report = Calculate(suleiman, faris);

        report.ByGeneration.Should().BeEquivalentTo(
            [new GenerationLifeStatus(1, 0, 1), new GenerationLifeStatus(2, 1, 0)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Living_members_are_bracketed_by_age()
    {
        var report = Calculate(
            Member("عمر", born: new DateOnly(2020, 1, 1)),    // 6
            Member("خالد", born: new DateOnly(1990, 1, 1)),   // 36
            Member("داوود", born: new DateOnly(1940, 1, 1))); // 86

        BracketCount(report, "0-17").Should().Be(1);
        BracketCount(report, "30-44").Should().Be(1);
        BracketCount(report, "75+").Should().Be(1);
    }

    /// <summary>Every bracket is present even at zero, so a chart's axis does not move between loads.</summary>
    [Fact]
    public void All_six_brackets_are_always_returned()
    {
        Calculate(Member("عمر")).LivingAges.Select(b => b.Bracket).Should().BeEquivalentTo(
            ["0-17", "18-29", "30-44", "45-59", "60-74", "75+"],
            options => options.WithStrictOrdering());
    }

    /// <summary>The histogram must not imply a population it did not measure.</summary>
    [Fact]
    public void Living_members_without_a_birth_date_are_excluded_from_the_brackets_and_counted_apart()
    {
        var report = Calculate(Member("عمر"), Member("خالد", born: new DateOnly(1990, 1, 1)));

        report.LivingWithoutBirthDate.Should().Be(1);
        report.LivingAges.Sum(b => b.Count).Should().Be(1);
    }

    [Fact]
    public void Longevity_covers_only_deceased_members_holding_both_dates()
    {
        var report = Calculate(
            Member("سليمان", born: new DateOnly(1900, 1, 1), died: new DateOnly(1980, 1, 1)), // 80
            Member("فارس", born: new DateOnly(1910, 1, 1), died: new DateOnly(1960, 1, 1)),   // 50
            Member("عمر", deceased: true),                                                     // no dates
            Member("خالد", born: new DateOnly(1990, 1, 1)));                                   // living

        report.Longevity!.Count.Should().Be(2);
        report.Longevity.MinYears.Should().Be(50);
        report.Longevity.MaxYears.Should().Be(80);
    }

    [Fact]
    public void Longevity_is_null_when_no_deceased_member_has_both_dates()
    {
        Calculate(Member("عمر", deceased: true)).Longevity.Should().BeNull();
    }

    /// <summary>Whole-year counts, so an even population takes the lower middle, not a mean.</summary>
    [Fact]
    public void An_even_longevity_population_takes_the_lower_middle_value()
    {
        var report = Calculate(
            Deceased("سليمان", 40), Deceased("فارس", 50),
            Deceased("عمر", 60), Deceased("خالد", 70));

        report.Longevity!.MedianYears.Should().Be(50);
    }

    [Fact]
    public void An_odd_longevity_population_takes_the_middle_value()
    {
        var report = Calculate(Deceased("سليمان", 40), Deceased("فارس", 50), Deceased("عمر", 60));

        report.Longevity!.MedianYears.Should().Be(50);
    }

    private static FamilyMember Deceased(string name, int years) =>
        Member(name, born: new DateOnly(1900, 1, 1), died: new DateOnly(1900 + years, 1, 1));

    private static int BracketCount(LifeStatusReport report, string bracket) =>
        report.LivingAges.Single(b => b.Bracket == bracket).Count;
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~LifeStatusCalculatorTests"`
Expected: FAIL — `LifeStatusCalculator` does not exist.

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/LifeStatusReport.cs`:

```csharp
namespace FamilyTree.Contracts.Reports;

/// <summary>
/// <paramref name="Longevity"/> is null when no deceased member holds both dates — the
/// realistic state of a freshly imported tree. A null says "not measurable"; zeros would read
/// as "measured, and zero" (design §5).
/// </summary>
public sealed record LifeStatusReport(
    int Living,
    int Deceased,
    IReadOnlyList<GenerationLifeStatus> ByGeneration,
    IReadOnlyList<AgeBracketCount> LivingAges,
    int LivingWithoutBirthDate,
    LongevityStats? Longevity);

public sealed record GenerationLifeStatus(int Generation, int Living, int Deceased);

public sealed record AgeBracketCount(string Bracket, int Count);

public sealed record LongevityStats(int Count, int MinYears, int MaxYears, int MedianYears);
```

Create `src/FamilyTree.Application/Reports/LifeStatusCalculator.cs`:

```csharp
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class LifeStatusCalculator
{
    /// <summary>
    /// Emitted in full even where a bracket is empty, so a chart's axis does not shift between
    /// two loads of the same screen.
    /// </summary>
    private static readonly (string Label, int Minimum, int Maximum)[] Brackets =
    [
        ("0-17", 0, 17),
        ("18-29", 18, 29),
        ("30-44", 30, 44),
        ("45-59", 45, 59),
        ("60-74", 60, 74),
        ("75+", 75, int.MaxValue)
    ];

    public static LifeStatusReport Calculate(
        IReadOnlyList<FamilyMember> members,
        IReadOnlyDictionary<Guid, int> generations,
        DateOnly today)
    {
        var living = members.Where(m => !m.IsDeceased).ToList();

        var byGeneration = members
            .GroupBy(m => generations.TryGetValue(m.Id, out var g) ? g : 1)
            .OrderBy(g => g.Key)
            .Select(g => new GenerationLifeStatus(
                g.Key, g.Count(m => !m.IsDeceased), g.Count(m => m.IsDeceased)))
            .ToList();

        var livingAges = living
            .Where(m => m.DateOfBirth is not null)
            .Select(m => Ages.YearsBetween(m.DateOfBirth!.Value, today))
            .ToList();

        return new LifeStatusReport(
            Living: living.Count,
            Deceased: members.Count - living.Count,
            ByGeneration: byGeneration,
            LivingAges: Bracket(livingAges),
            LivingWithoutBirthDate: living.Count(m => m.DateOfBirth is null),
            Longevity: Longevity(members));
    }

    private static IReadOnlyList<AgeBracketCount> Bracket(IReadOnlyList<int> ages) =>
        Brackets
            .Select(b => new AgeBracketCount(
                b.Label, ages.Count(age => age >= b.Minimum && age <= b.Maximum)))
            .ToList();

    private static LongevityStats? Longevity(IReadOnlyList<FamilyMember> members)
    {
        // Both dates, not merely the deceased flag: a lifespan needs two ends.
        var spans = members
            .Where(m => m.IsDeceased && m.DateOfBirth is not null && m.DateOfDeath is not null)
            .Select(m => Ages.YearsBetween(m.DateOfBirth!.Value, m.DateOfDeath!.Value))
            .OrderBy(years => years)
            .ToList();

        if (spans.Count == 0) return null;

        // The lower of the two middle values on an even count. These are whole-year counts;
        // an averaged 82.5 would imply a precision the data does not have (design §6).
        var median = spans[(spans.Count - 1) / 2];

        return new LongevityStats(spans.Count, spans[0], spans[^1], median);
    }
}
```

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~LifeStatusCalculatorTests"`
Expected: PASS — 11 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports/LifeStatusReport.cs src/FamilyTree.Application/Reports/LifeStatusCalculator.cs tests/FamilyTree.Application.Tests/Reports/LifeStatusCalculatorTests.cs
git commit -m "feat: add the life status report calculator

Living/deceased totals and per-generation split, an age histogram over
living members holding a birth date, and longevity over the deceased
holding both. Longevity is null rather than zeroed when unmeasurable."
```

---

### Task 4: Completeness report

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/CompletenessReport.cs`
- Create: `src/FamilyTree.Application/Reports/CompletenessCalculator.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/CompletenessCalculatorTests.cs`

**Interfaces:**
- Consumes: `ReportLimits.MaxMembersPerList`, `MemberRef.From` (Task 1).
- Produces:
  - `record CompletenessReport(int TotalMembers, int CompleteRecords, IReadOnlyList<CompletenessIssue> Issues)`
  - `record CompletenessIssue(string Code, int Count, IReadOnlyList<MemberRef> Members)`
  - `static class CompletenessCodes { const string MissingBirthDate = "MISSING_BIRTH_DATE"; const string DeceasedWithoutDeathDate = "DECEASED_WITHOUT_DEATH_DATE"; }`
  - `static CompletenessReport CompletenessCalculator.Calculate(IReadOnlyList<FamilyMember> members)`

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Application.Tests/Reports/CompletenessCalculatorTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class CompletenessCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(
        string name, DateOnly? born = null, DateOnly? died = null, bool deceased = false) =>
        FamilyMember.Create(TenantId, TreeId, null, name, Now, born, died, deceased);

    private static CompletenessIssue Issue(CompletenessReport report, string code) =>
        report.Issues.Single(i => i.Code == code);

    [Fact]
    public void An_empty_tree_reports_no_issues_and_no_complete_records()
    {
        var report = CompletenessCalculator.Calculate([]);

        report.TotalMembers.Should().Be(0);
        report.CompleteRecords.Should().Be(0);
        report.Issues.Should().OnlyContain(i => i.Count == 0);
    }

    [Fact]
    public void A_member_without_a_birth_date_is_listed()
    {
        var report = CompletenessCalculator.Calculate([Member("سليمان")]);

        var issue = Issue(report, CompletenessCodes.MissingBirthDate);
        issue.Count.Should().Be(1);
        issue.Members.Should().ContainSingle().Which.Name.Should().Be("سليمان");
    }

    [Fact]
    public void A_member_known_to_have_died_without_a_date_is_listed()
    {
        var report = CompletenessCalculator.Calculate(
            [Member("سليمان", born: new DateOnly(1900, 1, 1), deceased: true)]);

        Issue(report, CompletenessCodes.DeceasedWithoutDeathDate).Count.Should().Be(1);
    }

    /// <summary>Setting a death date implies the flag, so this member is not an issue.</summary>
    [Fact]
    public void A_deceased_member_holding_a_death_date_is_not_listed()
    {
        var report = CompletenessCalculator.Calculate(
            [Member("سليمان", born: new DateOnly(1900, 1, 1), died: new DateOnly(1980, 1, 1))]);

        Issue(report, CompletenessCodes.DeceasedWithoutDeathDate).Count.Should().Be(0);
        report.CompleteRecords.Should().Be(1);
    }

    /// <summary>The codes are independent worklists, not a partition of the members.</summary>
    [Fact]
    public void A_member_can_appear_under_more_than_one_code()
    {
        var report = CompletenessCalculator.Calculate([Member("سليمان", deceased: true)]);

        Issue(report, CompletenessCodes.MissingBirthDate).Count.Should().Be(1);
        Issue(report, CompletenessCodes.DeceasedWithoutDeathDate).Count.Should().Be(1);
        report.CompleteRecords.Should().Be(0);
    }

    [Fact]
    public void A_living_member_with_a_birth_date_is_complete()
    {
        var report = CompletenessCalculator.Calculate([Member("عمر", born: new DateOnly(1990, 1, 1))]);

        report.CompleteRecords.Should().Be(1);
    }

    /// <summary>Design §5: the true count survives truncation, so a client cannot under-report.</summary>
    [Fact]
    public void A_list_longer_than_the_cap_is_truncated_but_keeps_its_true_count()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList + 1)
            .Select(i => Member($"عضو {i}"))
            .ToList();

        var issue = Issue(
            CompletenessCalculator.Calculate(members), CompletenessCodes.MissingBirthDate);

        issue.Count.Should().Be(ReportLimits.MaxMembersPerList + 1);
        issue.Members.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }

    [Fact]
    public void A_list_exactly_at_the_cap_is_not_truncated()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList)
            .Select(i => Member($"عضو {i}"))
            .ToList();

        Issue(CompletenessCalculator.Calculate(members), CompletenessCodes.MissingBirthDate)
            .Members.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }

    /// <summary>Every code is always present, so a client renders a stable set of rows.</summary>
    [Fact]
    public void Both_codes_are_returned_even_when_no_member_is_affected()
    {
        var report = CompletenessCalculator.Calculate([Member("عمر", born: new DateOnly(1990, 1, 1))]);

        report.Issues.Select(i => i.Code).Should().BeEquivalentTo(
            [CompletenessCodes.MissingBirthDate, CompletenessCodes.DeceasedWithoutDeathDate],
            options => options.WithStrictOrdering());
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~CompletenessCalculatorTests"`
Expected: FAIL — `CompletenessCalculator` does not exist.

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/CompletenessReport.cs`:

```csharp
namespace FamilyTree.Contracts.Reports;

/// <summary>
/// A curation worklist. <paramref name="CompleteRecords"/> counts members flagged by no code
/// at all; the codes themselves are independent lists, not a partition, so a member may appear
/// under more than one (design §5).
/// </summary>
public sealed record CompletenessReport(
    int TotalMembers,
    int CompleteRecords,
    IReadOnlyList<CompletenessIssue> Issues);

/// <summary>
/// <paramref name="Count"/> is every affected member; <paramref name="Members"/> is capped at
/// ReportLimits.MaxMembersPerList. A client must render the count, never Members.Count.
/// </summary>
public sealed record CompletenessIssue(
    string Code, int Count, IReadOnlyList<MemberRef> Members);

/// <summary>
/// Stable codes, translated client-side like every other code in this API. There is
/// deliberately no orphaned-parent code: the composite self-FK on
/// (parent_id, family_tree_id) makes an unresolvable parent link unrepresentable, and an
/// issue that can never fire can never be tested either (design §6).
/// </summary>
public static class CompletenessCodes
{
    public const string MissingBirthDate = "MISSING_BIRTH_DATE";
    public const string DeceasedWithoutDeathDate = "DECEASED_WITHOUT_DEATH_DATE";
}
```

Create `src/FamilyTree.Application/Reports/CompletenessCalculator.cs`:

```csharp
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class CompletenessCalculator
{
    public static CompletenessReport Calculate(IReadOnlyList<FamilyMember> members)
    {
        var issues = new List<CompletenessIssue>
        {
            IssueFor(CompletenessCodes.MissingBirthDate, members, MissingBirthDate),
            IssueFor(CompletenessCodes.DeceasedWithoutDeathDate, members, DeceasedWithoutDeathDate)
        };

        return new CompletenessReport(
            TotalMembers: members.Count,
            CompleteRecords: members.Count(m => !MissingBirthDate(m) && !DeceasedWithoutDeathDate(m)),
            Issues: issues);
    }

    private static bool MissingBirthDate(FamilyMember member) => member.DateOfBirth is null;

    /// <summary>
    /// The flag with no date. Genealogy routinely establishes that someone died while the date
    /// itself is lost, which is exactly the record a curator needs to chase.
    /// </summary>
    private static bool DeceasedWithoutDeathDate(FamilyMember member) =>
        member.IsDeceased && member.DateOfDeath is null;

    /// <summary>
    /// Emitted even at zero, so the screen renders a stable set of rows rather than one that
    /// appears and disappears as the data is corrected.
    /// </summary>
    private static CompletenessIssue IssueFor(
        string code, IReadOnlyList<FamilyMember> members, Func<FamilyMember, bool> predicate)
    {
        var affected = members.Where(predicate).ToList();

        return new CompletenessIssue(
            code,
            affected.Count,
            affected
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Take(ReportLimits.MaxMembersPerList)
                .Select(MemberRef.From)
                .ToList());
    }
}
```

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~CompletenessCalculatorTests"`
Expected: PASS — 9 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports/CompletenessReport.cs src/FamilyTree.Application/Reports/CompletenessCalculator.cs tests/FamilyTree.Application.Tests/Reports/CompletenessCalculatorTests.cs
git commit -m "feat: add the completeness report calculator

Two worklists - members missing a birth date, and members known to have
died with no date recorded. Lists are capped but always carry the true
count, so a client cannot under-report the work outstanding."
```

---

### Task 5: Upcoming dates report

The task with the most date arithmetic. The occurrence helper is separated from the calculator because leap-day and year-boundary handling deserves its own tests.

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/UpcomingReport.cs`
- Create: `src/FamilyTree.Application/Reports/AnniversaryOccurrence.cs`
- Create: `src/FamilyTree.Application/Reports/UpcomingCalculator.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/AnniversaryOccurrenceTests.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/UpcomingCalculatorTests.cs`

**Interfaces:**
- Consumes: `Ages.YearsBetween`, `ReportLimits`, `MemberRef.From` (Task 1).
- Produces:
  - `record UpcomingReport(int WindowDays, int BirthdayCount, int AnniversaryCount, IReadOnlyList<UpcomingBirthday> Birthdays, IReadOnlyList<UpcomingAnniversary> Anniversaries)`
  - `record UpcomingBirthday(MemberRef Member, DateOnly DateOfBirth, DateOnly Occurrence, int DaysAway, int TurningAge)`
  - `record UpcomingAnniversary(MemberRef Member, DateOnly DateOfDeath, DateOnly Occurrence, int DaysAway, int Years)`
  - `static DateOnly AnniversaryOccurrence.Next(DateOnly anniversary, DateOnly today)`
  - `static UpcomingReport UpcomingCalculator.Calculate(IReadOnlyList<FamilyMember> members, DateOnly today)`

- [x] **Step 1: Write the failing tests**

Create `tests/FamilyTree.Application.Tests/Reports/AnniversaryOccurrenceTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class AnniversaryOccurrenceTests
{
    [Fact]
    public void An_anniversary_later_this_year_falls_this_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 9, 10), new DateOnly(2026, 8, 22))
            .Should().Be(new DateOnly(2026, 9, 10));
    }

    /// <summary>Today counts as upcoming: a birthday should not vanish on the morning of it.</summary>
    [Fact]
    public void An_anniversary_falling_today_is_today()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 8, 22), new DateOnly(2026, 8, 22))
            .Should().Be(new DateOnly(2026, 8, 22));
    }

    [Fact]
    public void An_anniversary_already_past_this_year_rolls_to_next_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 3, 10), new DateOnly(2026, 8, 22))
            .Should().Be(new DateOnly(2027, 3, 10));
    }

    /// <summary>The year-boundary case: a December reference day reaching into January.</summary>
    [Fact]
    public void A_january_anniversary_seen_from_december_falls_in_the_following_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(1990, 1, 5), new DateOnly(2026, 12, 20))
            .Should().Be(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public void A_leap_day_anniversary_falls_on_itself_in_a_leap_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(2000, 2, 29), new DateOnly(2028, 1, 1))
            .Should().Be(new DateOnly(2028, 2, 29));
    }

    /// <summary>
    /// Observed on 1 March in a common year: never dropped, and never landing before the
    /// anniversary date itself (design §6).
    /// </summary>
    [Fact]
    public void A_leap_day_anniversary_is_observed_on_the_first_of_march_in_a_common_year()
    {
        AnniversaryOccurrence.Next(new DateOnly(2000, 2, 29), new DateOnly(2027, 1, 1))
            .Should().Be(new DateOnly(2027, 3, 1));
    }
}
```

Create `tests/FamilyTree.Application.Tests/Reports/UpcomingCalculatorTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class UpcomingCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 22);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(
        string name, DateOnly? born = null, DateOnly? died = null, bool deceased = false) =>
        FamilyMember.Create(TenantId, TreeId, null, name, Now, born, died, deceased);

    private static UpcomingReport Calculate(params FamilyMember[] members) =>
        UpcomingCalculator.Calculate(members, Today);

    [Fact]
    public void An_empty_tree_has_nothing_upcoming()
    {
        var report = Calculate();

        report.Birthdays.Should().BeEmpty();
        report.Anniversaries.Should().BeEmpty();
        report.WindowDays.Should().Be(ReportLimits.UpcomingWindowDays);
    }

    [Fact]
    public void A_birthday_inside_the_window_is_listed_with_its_distance_and_new_age()
    {
        var report = Calculate(Member("عمر", born: new DateOnly(1990, 9, 1)));

        var birthday = report.Birthdays.Should().ContainSingle().Subject;
        birthday.Occurrence.Should().Be(new DateOnly(2026, 9, 1));
        birthday.DaysAway.Should().Be(10);
        birthday.TurningAge.Should().Be(36);
    }

    [Fact]
    public void A_birthday_beyond_the_window_is_omitted()
    {
        Calculate(Member("عمر", born: new DateOnly(1990, 11, 1))).Birthdays.Should().BeEmpty();
    }

    [Fact]
    public void A_birthday_falling_today_is_included_at_zero_days_away()
    {
        var report = Calculate(Member("عمر", born: new DateOnly(1990, 8, 22)));

        report.Birthdays.Should().ContainSingle().Which.DaysAway.Should().Be(0);
    }

    /// <summary>The window is inclusive at its far edge.</summary>
    [Fact]
    public void A_birthday_on_the_last_day_of_the_window_is_included()
    {
        var report = Calculate(Member("عمر", born: new DateOnly(1990, 9, 21)));

        report.Birthdays.Should().ContainSingle().Which.DaysAway.Should().Be(30);
    }

    [Fact]
    public void A_birthday_one_day_past_the_window_is_omitted()
    {
        Calculate(Member("عمر", born: new DateOnly(1990, 9, 22))).Birthdays.Should().BeEmpty();
    }

    /// <summary>A birthday list including the dead is a bug, not a feature.</summary>
    [Fact]
    public void A_deceased_members_birthday_is_not_listed()
    {
        var member = Member("سليمان", born: new DateOnly(1900, 9, 1), deceased: true);

        Calculate(member).Birthdays.Should().BeEmpty();
    }

    [Fact]
    public void A_death_anniversary_inside_the_window_is_listed()
    {
        var member = Member(
            "سليمان", born: new DateOnly(1900, 1, 1), died: new DateOnly(1980, 9, 1));

        var anniversary = Calculate(member).Anniversaries.Should().ContainSingle().Subject;
        anniversary.Occurrence.Should().Be(new DateOnly(2026, 9, 1));
        anniversary.Years.Should().Be(46);
    }

    /// <summary>
    /// The flag alone is not enough: the domain allows a death with no date, and those members
    /// belong in the completeness report, not given an invented anniversary.
    /// </summary>
    [Fact]
    public void A_deceased_member_without_a_death_date_has_no_anniversary()
    {
        Calculate(Member("سليمان", deceased: true)).Anniversaries.Should().BeEmpty();
    }

    [Fact]
    public void Birthdays_are_ordered_by_how_soon_they_fall()
    {
        var report = Calculate(
            Member("خالد", born: new DateOnly(1990, 9, 10)),
            Member("عمر", born: new DateOnly(1990, 8, 25)));

        report.Birthdays.Select(b => b.Member.Name).Should().ContainInOrder("عمر", "خالد");
    }

    /// <summary>The year-boundary case, end to end through the calculator.</summary>
    [Fact]
    public void A_january_birthday_is_reached_from_a_december_reference_day()
    {
        var report = UpcomingCalculator.Calculate(
            [Member("عمر", born: new DateOnly(1990, 1, 5))], new DateOnly(2026, 12, 20));

        var birthday = report.Birthdays.Should().ContainSingle().Subject;
        birthday.Occurrence.Should().Be(new DateOnly(2027, 1, 5));
        birthday.DaysAway.Should().Be(16);
        birthday.TurningAge.Should().Be(37);
    }

    /// <summary>Design §5: the upcoming lists are capped too, and disclose it.</summary>
    [Fact]
    public void A_birthday_list_longer_than_the_cap_is_truncated_but_keeps_its_true_count()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList + 1)
            .Select(i => Member($"عضو {i}", born: new DateOnly(1990, 8, 23)))
            .ToArray();

        var report = Calculate(members);

        report.BirthdayCount.Should().Be(ReportLimits.MaxMembersPerList + 1);
        report.Birthdays.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~Upcoming|FullyQualifiedName~AnniversaryOccurrence"`
Expected: FAIL — `AnniversaryOccurrence` and `UpcomingCalculator` do not exist.

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/UpcomingReport.cs`:

```csharp
namespace FamilyTree.Contracts.Reports;

/// <summary>
/// <paramref name="BirthdayCount"/> and <paramref name="AnniversaryCount"/> are the untruncated
/// totals: the lists are capped like every other, and a truncation no field discloses is a lie
/// the contract tells quietly (design §5).
/// </summary>
public sealed record UpcomingReport(
    int WindowDays,
    int BirthdayCount,
    int AnniversaryCount,
    IReadOnlyList<UpcomingBirthday> Birthdays,
    IReadOnlyList<UpcomingAnniversary> Anniversaries);

/// <summary>
/// <paramref name="Occurrence"/> is the day the observance falls on this cycle, which is not
/// always the anniversary date — see the 29 February rule. <paramref name="TurningAge"/> is
/// the age reached on that day, not the age today.
/// </summary>
public sealed record UpcomingBirthday(
    MemberRef Member, DateOnly DateOfBirth, DateOnly Occurrence, int DaysAway, int TurningAge);

public sealed record UpcomingAnniversary(
    MemberRef Member, DateOnly DateOfDeath, DateOnly Occurrence, int DaysAway, int Years);
```

Create `src/FamilyTree.Application/Reports/AnniversaryOccurrence.cs`:

```csharp
namespace FamilyTree.Application.Reports;

public static class AnniversaryOccurrence
{
    /// <summary>
    /// The next time this anniversary comes round, on or after <paramref name="today"/>.
    /// Inclusive of today: a birthday should not disappear on the morning of it.
    /// </summary>
    public static DateOnly Next(DateOnly anniversary, DateOnly today)
    {
        var thisYear = InYear(anniversary, today.Year);
        return thisYear >= today ? thisYear : InYear(anniversary, today.Year + 1);
    }

    /// <summary>
    /// 29 February is observed on 1 March in a common year. Chosen over skipping it, so the
    /// person never silently vanishes from a 30-day window, and over 28 February, so an
    /// observance never lands before its own anniversary date (design §6).
    /// </summary>
    private static DateOnly InYear(DateOnly anniversary, int year) =>
        anniversary is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year)
            ? new DateOnly(year, 3, 1)
            : new DateOnly(year, anniversary.Month, anniversary.Day);
}
```

Create `src/FamilyTree.Application/Reports/UpcomingCalculator.cs`:

```csharp
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class UpcomingCalculator
{
    public static UpcomingReport Calculate(IReadOnlyList<FamilyMember> members, DateOnly today)
    {
        // Birthdays are for the living only. Anniversaries need an actual date, not merely the
        // deceased flag — see the completeness report for members who have one without the other.
        var birthdays = members
            .Where(m => !m.IsDeceased && m.DateOfBirth is not null)
            .Select(m => Observance(m, m.DateOfBirth!.Value, today))
            .Where(o => o is not null)
            .Select(o => o!.Value)
            .OrderBy(o => o.DaysAway)
            .ThenBy(o => o.Member.Name, StringComparer.Ordinal)
            .ToList();

        var anniversaries = members
            .Where(m => m.DateOfDeath is not null)
            .Select(m => Observance(m, m.DateOfDeath!.Value, today))
            .Where(o => o is not null)
            .Select(o => o!.Value)
            .OrderBy(o => o.DaysAway)
            .ThenBy(o => o.Member.Name, StringComparer.Ordinal)
            .ToList();

        return new UpcomingReport(
            WindowDays: ReportLimits.UpcomingWindowDays,
            BirthdayCount: birthdays.Count,
            AnniversaryCount: anniversaries.Count,
            Birthdays: birthdays
                .Take(ReportLimits.MaxMembersPerList)
                .Select(o => new UpcomingBirthday(
                    o.Member, o.Anniversary, o.Occurrence, o.DaysAway, o.Years))
                .ToList(),
            Anniversaries: anniversaries
                .Take(ReportLimits.MaxMembersPerList)
                .Select(o => new UpcomingAnniversary(
                    o.Member, o.Anniversary, o.Occurrence, o.DaysAway, o.Years))
                .ToList());
    }

    private readonly record struct Observed(
        MemberRef Member, DateOnly Anniversary, DateOnly Occurrence, int DaysAway, int Years);

    /// <summary>
    /// Null when the next occurrence falls outside the window. The window is inclusive at both
    /// ends: today counts, and so does the thirtieth day.
    /// </summary>
    private static Observed? Observance(FamilyMember member, DateOnly anniversary, DateOnly today)
    {
        var occurrence = AnniversaryOccurrence.Next(anniversary, today);
        var daysAway = occurrence.DayNumber - today.DayNumber;

        if (daysAway > ReportLimits.UpcomingWindowDays) return null;

        return new Observed(
            MemberRef.From(member),
            anniversary,
            occurrence,
            daysAway,
            // The age or count reached ON the occurrence, not today's: a list headed "upcoming"
            // showing today's age would be off by one for every entry in it.
            Ages.YearsBetween(anniversary, occurrence));
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~Upcoming|FullyQualifiedName~AnniversaryOccurrence"`
Expected: PASS — 18 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports/UpcomingReport.cs src/FamilyTree.Application/Reports/AnniversaryOccurrence.cs src/FamilyTree.Application/Reports/UpcomingCalculator.cs tests/FamilyTree.Application.Tests/Reports/AnniversaryOccurrenceTests.cs tests/FamilyTree.Application.Tests/Reports/UpcomingCalculatorTests.cs
git commit -m "feat: add the upcoming dates report calculator

Birthdays for the living and anniversaries for the dated dead, inside a
30-day window. 29 February is observed on 1 March in a common year, and
ages are counted at the occurrence rather than today."
```

---

### Task 6: Recent activity report

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/ActivityReport.cs`
- Create: `src/FamilyTree.Application/Reports/ActivityCalculator.cs`
- Test: `tests/FamilyTree.Application.Tests/Reports/ActivityCalculatorTests.cs`

**Interfaces:**
- Consumes: `ReportLimits`, `MemberRef.From` (Task 1).
- Produces:
  - `record ActivityReport(int WindowDays, int AddedCount, int EditedCount, IReadOnlyList<ActivityEntry> Added, IReadOnlyList<ActivityEntry> Edited)`
  - `record ActivityEntry(MemberRef Member, DateTimeOffset At)`
  - `static ActivityReport ActivityCalculator.Calculate(IReadOnlyList<FamilyMember> members, DateTimeOffset now)`

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Application.Tests/Reports/ActivityCalculatorTests.cs`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class ActivityCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    /// <summary>Created at a chosen moment, so a member can be placed inside or outside the window.</summary>
    private static FamilyMember MemberCreatedAt(string name, DateTimeOffset createdAt) =>
        FamilyMember.Create(TenantId, TreeId, null, name, createdAt);

    private static ActivityReport Calculate(params FamilyMember[] members) =>
        ActivityCalculator.Calculate(members, Now);

    [Fact]
    public void An_empty_tree_has_no_activity()
    {
        var report = Calculate();

        report.Added.Should().BeEmpty();
        report.Edited.Should().BeEmpty();
        report.WindowDays.Should().Be(ReportLimits.ActivityWindowDays);
    }

    [Fact]
    public void A_member_created_inside_the_window_is_listed_as_added()
    {
        var report = Calculate(MemberCreatedAt("عمر", Now.AddDays(-3)));

        report.Added.Should().ContainSingle().Which.Member.Name.Should().Be("عمر");
        report.Edited.Should().BeEmpty();
    }

    [Fact]
    public void A_member_created_before_the_window_is_not_listed_as_added()
    {
        Calculate(MemberCreatedAt("عمر", Now.AddDays(-40))).Added.Should().BeEmpty();
    }

    [Fact]
    public void An_edit_to_a_member_that_already_existed_is_listed_as_edited()
    {
        var member = MemberCreatedAt("عمر", Now.AddDays(-40));
        member.Rename("عمر", Now.AddDays(-2));

        var report = Calculate(member);

        report.Edited.Should().ContainSingle().Which.Member.Name.Should().Be("عمر");
        report.Added.Should().BeEmpty();
    }

    /// <summary>
    /// Design §6. Testing UpdatedAt != CreatedAt instead would list this member twice in the
    /// same week's report; the arrival is the more informative fact, so Added wins.
    /// </summary>
    [Fact]
    public void A_member_added_and_edited_inside_the_window_appears_once_under_added()
    {
        var member = MemberCreatedAt("عمر", Now.AddDays(-5));
        member.Rename("عمر", Now.AddDays(-1));

        var report = Calculate(member);

        report.Added.Should().ContainSingle();
        report.Edited.Should().BeEmpty();
    }

    [Fact]
    public void An_untouched_old_member_appears_in_neither_list()
    {
        var report = Calculate(MemberCreatedAt("سليمان", Now.AddDays(-400)));

        report.Added.Should().BeEmpty();
        report.Edited.Should().BeEmpty();
    }

    [Fact]
    public void The_most_recent_change_is_listed_first()
    {
        var report = Calculate(
            MemberCreatedAt("خالد", Now.AddDays(-10)),
            MemberCreatedAt("عمر", Now.AddDays(-1)));

        report.Added.Select(e => e.Member.Name).Should().ContainInOrder("عمر", "خالد");
    }

    [Fact]
    public void An_added_list_longer_than_the_cap_is_truncated_but_keeps_its_true_count()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList + 1)
            .Select(i => MemberCreatedAt($"عضو {i}", Now.AddDays(-1)))
            .ToArray();

        var report = Calculate(members);

        report.AddedCount.Should().Be(ReportLimits.MaxMembersPerList + 1);
        report.Added.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~ActivityCalculatorTests"`
Expected: FAIL — `ActivityCalculator` does not exist.

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/ActivityReport.cs`:

```csharp
namespace FamilyTree.Contracts.Reports;

/// <summary>
/// A stand-in for audit history, not a substitute for it: this reads the current state of a
/// row's timestamps, so it cannot show deletions, cannot show who made a change, and shows
/// only the most recent edit of several. The real fix is the AuditLog entity, which does not
/// yet exist (design §9).
/// </summary>
public sealed record ActivityReport(
    int WindowDays,
    int AddedCount,
    int EditedCount,
    IReadOnlyList<ActivityEntry> Added,
    IReadOnlyList<ActivityEntry> Edited);

public sealed record ActivityEntry(MemberRef Member, DateTimeOffset At);
```

Create `src/FamilyTree.Application/Reports/ActivityCalculator.cs`:

```csharp
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class ActivityCalculator
{
    public static ActivityReport Calculate(IReadOnlyList<FamilyMember> members, DateTimeOffset now)
    {
        var since = now.AddDays(-ReportLimits.ActivityWindowDays);

        var added = members.Where(m => m.CreatedAt >= since).ToList();

        // Anchored on CreatedAt being OUTSIDE the window, not on UpdatedAt != CreatedAt:
        // Entity.InitializeTimestamps sets the two equal, so the weaker test would list a
        // member created on Monday and corrected on Tuesday under both headings. This way
        // "edited" means a change to a member that already existed, and the lists are
        // disjoint by construction (design §6).
        var edited = members.Where(m => m.UpdatedAt >= since && m.CreatedAt < since).ToList();

        return new ActivityReport(
            WindowDays: ReportLimits.ActivityWindowDays,
            AddedCount: added.Count,
            EditedCount: edited.Count,
            Added: Entries(added, m => m.CreatedAt),
            Edited: Entries(edited, m => m.UpdatedAt));
    }

    private static IReadOnlyList<ActivityEntry> Entries(
        IReadOnlyList<FamilyMember> members, Func<FamilyMember, DateTimeOffset> at) =>
        members
            .OrderByDescending(at)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Take(ReportLimits.MaxMembersPerList)
            .Select(m => new ActivityEntry(MemberRef.From(m), at(m)))
            .ToList();
}
```

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Application.Tests --filter "FullyQualifiedName~ActivityCalculatorTests"`
Expected: PASS — 8 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports/ActivityReport.cs src/FamilyTree.Application/Reports/ActivityCalculator.cs tests/FamilyTree.Application.Tests/Reports/ActivityCalculatorTests.cs
git commit -m "feat: add the recent activity report calculator

Added and Edited are disjoint by construction: Edited requires CreatedAt
outside the window, so a member created and corrected in the same week is
listed once. A stand-in for audit history until AuditLog exists."
```

---

### Task 7: Report service and DI wiring

**Files:**
- Create: `src/FamilyTree.Contracts/Reports/ReportsResponse.cs`
- Create: `src/FamilyTree.Application/Reports/IReportService.cs`
- Create: `src/FamilyTree.Infrastructure/Reports/ReportService.cs`
- Modify: `src/FamilyTree.Infrastructure/DependencyInjection.cs` (add the `using` lines and one `AddScoped`)
- Test: `tests/FamilyTree.Api.IntegrationTests/Reports/ReportServiceTests.cs`

**Interfaces:**
- Consumes: every calculator from Tasks 2–6, `GenerationIndex` (Task 1).
- Produces:
  - `record ReportsResponse(DateOnly GeneratedOn, StructureReport Structure, LifeStatusReport LifeStatus, CompletenessReport Completeness, UpcomingReport Upcoming, ActivityReport Activity)`
  - `interface IReportService { Task<ReportsResponse> GetAsync(CancellationToken ct = default); }`
  - `sealed class ReportService(ApplicationDbContext context, TimeProvider timeProvider) : IReportService`

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Reports/ReportServiceTests.cs`:

```csharp
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Application.Reports;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyTree.Api.IntegrationTests.Reports;

/// <summary>
/// Runs against real PostgreSQL because what is under test is the tenant query filter, not the
/// arithmetic — the calculators own that, and have their own fast unit suites.
/// </summary>
public sealed class ReportServiceTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private async Task<Guid> SeedTenantWithTreeAsync(string slug)
    {
        await using var context = ContextFor(Guid.Empty);
        var tenant = Tenant.Create($"Tenant {slug}", slug, Now);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        context.FamilyTrees.Add(FamilyTreeAggregate.Create(tenant.Id, $"Tree {slug}", Now));
        await context.SaveChangesAsync();

        return tenant.Id;
    }

    private static IFamilyMemberService MembersFor(ApplicationDbContext context, Guid tenantId) =>
        new FamilyMemberService(context, new StubTenantContext(tenantId, Guid.CreateVersion7()), Clock);

    private static IReportService ReportsFor(ApplicationDbContext context, TimeProvider clock) =>
        new ReportService(context, clock);

    [Fact]
    public async Task A_tenant_with_no_tree_is_told_so()
    {
        await using var context = ContextFor(Guid.CreateVersion7());

        var act = () => ReportsFor(context, Clock).GetAsync();

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Code.Should().Be("FAMILY_TREE_NOT_FOUND");
    }

    [Fact]
    public async Task An_empty_tree_reports_zeros_rather_than_failing()
    {
        var tenantId = await SeedTenantWithTreeAsync("reports-empty");
        await using var context = ContextFor(tenantId);

        var report = await ReportsFor(context, Clock).GetAsync();

        report.Structure.TotalMembers.Should().Be(0);
        report.LifeStatus.Longevity.Should().BeNull();
        report.Completeness.Issues.Should().OnlyContain(i => i.Count == 0);
    }

    [Fact]
    public async Task Members_are_counted_and_the_generation_walk_reaches_the_leaves()
    {
        var tenantId = await SeedTenantWithTreeAsync("reports-counts");
        await using var context = ContextFor(tenantId);
        var members = MembersFor(context, tenantId);

        var suleiman = await members.CreateAsync(new CreateFamilyMemberRequest("سليمان", null));
        var faris = await members.CreateAsync(new CreateFamilyMemberRequest("فارس", suleiman.Id));
        await members.CreateAsync(new CreateFamilyMemberRequest("محمود", faris.Id));

        var report = await ReportsFor(context, Clock).GetAsync();

        report.Structure.TotalMembers.Should().Be(3);
        report.Structure.Depth.Should().Be(3);
        report.Structure.Branches.Should().ContainSingle().Which.DescendantCount.Should().Be(2);
    }

    /// <summary>Design §10: another tenant's members must not reach any count or list.</summary>
    [Fact]
    public async Task Another_tenants_members_are_invisible()
    {
        var mine = await SeedTenantWithTreeAsync("reports-mine");
        var theirs = await SeedTenantWithTreeAsync("reports-theirs");

        await using (var theirContext = ContextFor(theirs))
        {
            var theirMembers = MembersFor(theirContext, theirs);
            await theirMembers.CreateAsync(new CreateFamilyMemberRequest("داوود", null));
            await theirMembers.CreateAsync(new CreateFamilyMemberRequest("خالد", null));
        }

        await using var myContext = ContextFor(mine);
        await MembersFor(myContext, mine).CreateAsync(new CreateFamilyMemberRequest("سليمان", null));

        var report = await ReportsFor(myContext, Clock).GetAsync();

        report.Structure.TotalMembers.Should().Be(1);
        report.Structure.Branches.Should().ContainSingle().Which.Name.Should().Be("سليمان");
    }

    /// <summary>
    /// The reference day is the server's, in UTC, and is returned so a client never re-derives
    /// "today" in its own zone and disagrees (design §5).
    /// </summary>
    [Fact]
    public async Task The_reference_day_is_the_servers_utc_day_and_is_returned()
    {
        var tenantId = await SeedTenantWithTreeAsync("reports-today");
        await using var context = ContextFor(tenantId);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 20, 23, 30, 0, TimeSpan.Zero));

        var report = await ReportsFor(context, clock).GetAsync();

        report.GeneratedOn.Should().Be(new DateOnly(2026, 12, 20));
    }
}
```

> `FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing`. Check whether the
> integration test project already references it:
> `grep -i timeprovider tests/FamilyTree.Api.IntegrationTests/FamilyTree.Api.IntegrationTests.csproj`
> If absent, add it with
> `dotnet add tests/FamilyTree.Api.IntegrationTests package Microsoft.Extensions.TimeProvider.Testing`
> and include the `.csproj` change in this task's commit.

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~ReportServiceTests"`
Expected: FAIL — `ReportService` does not exist. (Docker must be running.)

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Contracts/Reports/ReportsResponse.cs`:

```csharp
namespace FamilyTree.Contracts.Reports;

/// <summary>
/// All five reports in one payload. One request, computed from a single pass over the member
/// list, which is what makes the whole screen one round trip (design §4).
/// </summary>
/// <param name="GeneratedOn">
/// The UTC reference day every date rule was evaluated against. Returned so a client renders
/// what the server measured rather than re-deriving "today" in its own time zone.
/// </param>
public sealed record ReportsResponse(
    DateOnly GeneratedOn,
    StructureReport Structure,
    LifeStatusReport LifeStatus,
    CompletenessReport Completeness,
    UpcomingReport Upcoming,
    ActivityReport Activity);
```

Create `src/FamilyTree.Application/Reports/IReportService.cs`:

```csharp
using FamilyTree.Contracts.Reports;

namespace FamilyTree.Application.Reports;

public interface IReportService
{
    /// <summary>
    /// Throws NotFoundException("FAMILY_TREE_NOT_FOUND") when the caller's tenant has no tree.
    /// An empty tree is not an error: it reports zeros.
    /// </summary>
    Task<ReportsResponse> GetAsync(CancellationToken ct = default);
}
```

Create `src/FamilyTree.Infrastructure/Reports/ReportService.cs`:

```csharp
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
        // windowed query later changes only this method, never the contract.
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
```

Modify `src/FamilyTree.Infrastructure/DependencyInjection.cs` — add the two `using` lines
alongside the existing ones, and register the service next to `IFamilyTreeService`:

```csharp
using FamilyTree.Application.Reports;
using FamilyTree.Infrastructure.Reports;
```

```csharp
        services.AddScoped<IFamilyTreeService, FamilyTreeService>();
        services.AddScoped<IReportService, ReportService>();
```

> `TimeProvider` is already resolvable — `FamilyTreeService` takes it. If the build reports it
> is unregistered, add `services.AddSingleton(TimeProvider.System);` rather than newing one up.

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~ReportServiceTests"`
Expected: PASS — 5 tests.

- [x] **Step 5: Commit**

```bash
git add src/FamilyTree.Contracts/Reports/ReportsResponse.cs src/FamilyTree.Application/Reports/IReportService.cs src/FamilyTree.Infrastructure/Reports tests/FamilyTree.Api.IntegrationTests/Reports src/FamilyTree.Infrastructure/DependencyInjection.cs
git commit -m "feat: assemble the five reports behind IReportService

One load of the member list feeds every calculator, and one UTC reference
day is derived per request, so no two sections of a response can disagree
about the tree or the day they describe."
```

---

### Task 8: The reports endpoint

**Files:**
- Create: `src/FamilyTree.Api/Endpoints/Reports/ReportEndpoints.cs`
- Modify: `src/FamilyTree.Api/Program.cs:83` (add `app.MapReportEndpoints();` after `app.MapRoleEndpoints();`)
- Test: `tests/FamilyTree.Api.IntegrationTests/Endpoints/ReportEndpointsTests.cs`

**Interfaces:**
- Consumes: `IReportService` (Task 7), `RequirePermission` (existing, `FamilyTree.Api.Authorization.EndpointExtensions`).
- Produces: `GET /api/v1/reports` returning `ReportsResponse` as JSON; `IEndpointRouteBuilder.MapReportEndpoints()`.

- [x] **Step 1: Write the failing test**

Create `tests/FamilyTree.Api.IntegrationTests/Endpoints/ReportEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Auth;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.Authorization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class ReportEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private void AuthenticateWith(params string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokens.CreateAccessToken(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "someone@example.com", permissions,
            mustChangePassword: false).Value;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var response = await _client.GetAsync("/api/v1/reports");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Design §4: reports reuse FamilyTree.View. A token carrying an unrelated permission must
    /// not open them.
    /// </summary>
    [Fact]
    public async Task A_token_without_family_tree_view_is_forbidden()
    {
        AuthenticateWith(Permissions.Audit.View);

        var response = await _client.GetAsync("/api/v1/reports");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_carrying_family_tree_view_is_admitted()
    {
        AuthenticateWith(Permissions.FamilyTree.View);

        var response = await _client.GetAsync("/api/v1/reports");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_response_carries_all_five_sections_and_the_reference_day()
    {
        AuthenticateWith(Permissions.FamilyTree.View);

        var report = await _client.GetFromJsonAsync<ReportsResponse>("/api/v1/reports");

        report.Should().NotBeNull();
        report!.Structure.Should().NotBeNull();
        report.LifeStatus.Should().NotBeNull();
        report.Completeness.Should().NotBeNull();
        report.Upcoming.Should().NotBeNull();
        report.Activity.Should().NotBeNull();
        report.GeneratedOn.Should().NotBe(default);
    }

    /// <summary>The fixed windows are contract, so a client can label the screen from them.</summary>
    [Fact]
    public async Task The_windows_are_reported_so_a_client_need_not_hardcode_them()
    {
        AuthenticateWith(Permissions.FamilyTree.View);

        var report = await _client.GetFromJsonAsync<ReportsResponse>("/api/v1/reports");

        report!.Upcoming.WindowDays.Should().Be(30);
        report.Activity.WindowDays.Should().Be(30);
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~ReportEndpointsTests"`
Expected: FAIL — 404 on `/api/v1/reports`, since the route is not mapped.

- [x] **Step 3: Write the implementation**

Create `src/FamilyTree.Api/Endpoints/Reports/ReportEndpoints.cs`:

```csharp
using FamilyTree.Api.Authorization;
using FamilyTree.Application.Reports;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Reports;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports");

        // Guarded by FamilyTree.View, not a new permission: every figure here is an aggregate
        // over data that permission already exposes, so a separate code would add a lockout
        // surface for the last-administrator guard to reason about without adding protection.
        // Same reasoning as GET /api/v1/family-tree/export.pdf (design §4).
        //
        // No query parameters: the windows and caps are fixed constants in ReportLimits, which
        // keeps the response one cacheable shape with no validation surface.
        group.MapGet("/", async (IReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.GetAsync(ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        return app;
    }
}
```

Modify `src/FamilyTree.Api/Program.cs` — add the `using` beside the other endpoint namespaces
and the mapping call after `app.MapRoleEndpoints();`:

```csharp
using FamilyTree.Api.Endpoints.Reports;
```

```csharp
app.MapRoleEndpoints();
app.MapReportEndpoints();
```

- [x] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/FamilyTree.Api.IntegrationTests --filter "FullyQualifiedName~ReportEndpointsTests"`
Expected: PASS — 5 tests.

- [x] **Step 5: Run the whole backend suite**

Run: `dotnet test`
Expected: PASS — every project, no regressions.

- [x] **Step 6: Commit**

```bash
git add src/FamilyTree.Api/Endpoints/Reports src/FamilyTree.Api/Program.cs tests/FamilyTree.Api.IntegrationTests/Endpoints/ReportEndpointsTests.cs
git commit -m "feat: expose GET /api/v1/reports

Guarded by FamilyTree.View rather than a new permission, following the
PDF export: the reports aggregate data that permission already exposes."
```

---

### Task 9: Frontend data layer

**Files:**
- Create: `frontend/src/features/reports/types.ts`
- Create: `frontend/src/features/reports/reportsApi.ts`
- Create: `frontend/src/features/reports/useReports.ts`
- Test: `frontend/src/features/reports/reportsApi.test.ts`

**Interfaces:**
- Consumes: `apiFetch` from `frontend/src/services/apiClient`.
- Produces:
  - types `ReportsResponse`, `StructureReport`, `LifeStatusReport`, `CompletenessReport`, `UpcomingReport`, `ActivityReport`, `MemberRef`, and their members
  - `reportsApi.get(): Promise<ReportsResponse>`
  - `reportKeys.all`, `useReportsQuery()`

- [x] **Step 1: Write the failing test**

Create `frontend/src/features/reports/reportsApi.test.ts`:

```typescript
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from '../../services/apiClient'
import { reportsApi } from './reportsApi'

vi.mock('../../services/apiClient')

describe('reportsApi', () => {
  beforeEach(() => {
    vi.mocked(apiFetch).mockReset()
    vi.mocked(apiFetch).mockResolvedValue({} as never)
  })

  it('requests the single aggregate endpoint', async () => {
    await reportsApi.get()

    expect(apiFetch).toHaveBeenCalledWith('/api/v1/reports')
  })

  // The endpoint takes no parameters by design: the windows and caps are server-side
  // constants, so there is nothing for a client to tune.
  it('sends no query string', async () => {
    await reportsApi.get()

    expect(vi.mocked(apiFetch).mock.calls[0][0]).not.toContain('?')
  })
})
```

- [x] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx vitest run src/features/reports/reportsApi.test.ts`
Expected: FAIL — cannot resolve `./reportsApi`.

- [x] **Step 3: Write the implementation**

Create `frontend/src/features/reports/types.ts`:

```typescript
/**
 * Mirrors FamilyTree.Contracts.Reports. Dates arrive as ISO strings: `DateOnly` serialises to
 * `YYYY-MM-DD` and `DateTimeOffset` to a full ISO timestamp.
 */

/** A member as a report row identifies one. The lineage is composed client-side — see fullName.ts. */
export interface MemberRef {
  id: string
  name: string
  parentId: string | null
}

export interface GenerationCount {
  generation: number
  count: number
}

export interface BranchSummary {
  id: string
  name: string
  descendantCount: number
  depth: number
}

export interface StructureReport {
  totalMembers: number
  depth: number
  generations: GenerationCount[]
  branches: BranchSummary[]
  membersWithChildren: number
  leafMembers: number
  averageChildrenPerParent: number
}

export interface GenerationLifeStatus {
  generation: number
  living: number
  deceased: number
}

export interface AgeBracketCount {
  bracket: string
  count: number
}

export interface LongevityStats {
  count: number
  minYears: number
  maxYears: number
  medianYears: number
}

export interface LifeStatusReport {
  living: number
  deceased: number
  byGeneration: GenerationLifeStatus[]
  livingAges: AgeBracketCount[]
  livingWithoutBirthDate: number
  /** Null when no deceased member holds both dates — not measurable, as distinct from zero. */
  longevity: LongevityStats | null
}

/** `count` is every affected member; `members` is capped. Render the count, never members.length. */
export interface CompletenessIssue {
  code: string
  count: number
  members: MemberRef[]
}

export interface CompletenessReport {
  totalMembers: number
  completeRecords: number
  issues: CompletenessIssue[]
}

export interface UpcomingBirthday {
  member: MemberRef
  dateOfBirth: string
  occurrence: string
  daysAway: number
  turningAge: number
}

export interface UpcomingAnniversary {
  member: MemberRef
  dateOfDeath: string
  occurrence: string
  daysAway: number
  years: number
}

export interface UpcomingReport {
  windowDays: number
  birthdayCount: number
  anniversaryCount: number
  birthdays: UpcomingBirthday[]
  anniversaries: UpcomingAnniversary[]
}

export interface ActivityEntry {
  member: MemberRef
  at: string
}

export interface ActivityReport {
  windowDays: number
  addedCount: number
  editedCount: number
  added: ActivityEntry[]
  edited: ActivityEntry[]
}

export interface ReportsResponse {
  /** The server's UTC reference day, `YYYY-MM-DD`. Never re-derive "today" locally. */
  generatedOn: string
  structure: StructureReport
  lifeStatus: LifeStatusReport
  completeness: CompletenessReport
  upcoming: UpcomingReport
  activity: ActivityReport
}
```

Create `frontend/src/features/reports/reportsApi.ts`:

```typescript
import { apiFetch } from '../../services/apiClient'
import type { ReportsResponse } from './types'

const REPORTS = '/api/v1/reports'

export const reportsApi = {
  /** One request for all five sections — the windows and caps are server-side constants. */
  get: (): Promise<ReportsResponse> => apiFetch<ReportsResponse>(REPORTS),
}
```

Create `frontend/src/features/reports/useReports.ts`:

```typescript
import { useQuery } from '@tanstack/react-query'
import { reportsApi } from './reportsApi'
import type { ReportsResponse } from './types'

export const reportKeys = {
  all: ['reports'] as const,
}

/**
 * Not nested under the members key: reports are derived from members, but they are recomputed
 * server-side per request, so a member mutation should refetch them rather than patch a cache.
 * Invalidating 'members' does not touch this key, which is why the screen refetches on mount.
 */
export const useReportsQuery = () =>
  useQuery<ReportsResponse>({ queryKey: reportKeys.all, queryFn: () => reportsApi.get() })
```

- [x] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx vitest run src/features/reports/reportsApi.test.ts`
Expected: PASS — 2 tests.

- [x] **Step 5: Commit**

```bash
git add frontend/src/features/reports
git commit -m "feat: add the reports data layer

Types mirroring the contracts, a single-endpoint client, and the query
hook. No parameters: windows and caps are server-side constants."
```

---

### Task 10: Reports screen — structure and life status

Delivers a working `/reports` route with the two count-only sections. Task 11 adds the three list sections.

**Files:**
- Create: `frontend/src/features/reports/ReportsPage.tsx`
- Create: `frontend/src/features/reports/StructureSection.tsx`
- Create: `frontend/src/features/reports/LifeStatusSection.tsx`
- Modify: `frontend/src/routes/AppRoutes.tsx` (add the `/reports` route)
- Modify: `frontend/src/app/AppShell.tsx` (replace the inert Dashboard placeholder with a live Reports link)
- Modify: `frontend/src/i18n/locales/en.json`, `frontend/src/i18n/locales/ar.json`
- Test: `frontend/src/features/reports/ReportsPage.test.tsx`
- Test: `frontend/src/routes/AppRoutes.test.tsx` (add one case)

**Interfaces:**
- Consumes: `useReportsQuery` (Task 9).
- Produces: `ReportsPage`, `StructureSection`, `LifeStatusSection`; the route `/reports`.

- [x] **Step 1: Write the failing test**

Create `frontend/src/features/reports/ReportsPage.test.tsx`:

```typescript
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { ReportsPage } from './ReportsPage'
import { reportsApi } from './reportsApi'
import type { ReportsResponse } from './types'

vi.mock('./reportsApi')

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => true,
    logout: vi.fn(),
  }),
}))

const report = (over: Partial<ReportsResponse> = {}): ReportsResponse => ({
  generatedOn: '2026-08-22',
  structure: {
    totalMembers: 5,
    depth: 3,
    generations: [
      { generation: 1, count: 2 },
      { generation: 2, count: 2 },
      { generation: 3, count: 1 },
    ],
    branches: [{ id: 'a', name: 'سليمان', descendantCount: 3, depth: 3 }],
    membersWithChildren: 2,
    leafMembers: 3,
    averageChildrenPerParent: 1.5,
  },
  lifeStatus: {
    living: 4,
    deceased: 1,
    byGeneration: [{ generation: 1, living: 1, deceased: 1 }],
    livingAges: [
      { bracket: '0-17', count: 1 },
      { bracket: '18-29', count: 0 },
      { bracket: '30-44', count: 2 },
      { bracket: '45-59', count: 0 },
      { bracket: '60-74', count: 0 },
      { bracket: '75+', count: 0 },
    ],
    livingWithoutBirthDate: 1,
    longevity: { count: 1, minYears: 80, maxYears: 80, medianYears: 80 },
  },
  completeness: { totalMembers: 5, completeRecords: 3, issues: [] },
  upcoming: {
    windowDays: 30,
    birthdayCount: 0,
    anniversaryCount: 0,
    birthdays: [],
    anniversaries: [],
  },
  activity: { windowDays: 30, addedCount: 0, editedCount: 0, added: [], edited: [] },
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ReportsPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('ReportsPage', () => {
  beforeEach(() => {
    vi.mocked(reportsApi.get).mockReset()
    vi.mocked(reportsApi.get).mockResolvedValue(report())
  })

  it('shows how deep the tree runs', async () => {
    renderPage()

    expect(await screen.findByTestId('structure-depth')).toHaveTextContent('3')
  })

  it('shows the headline member count', async () => {
    renderPage()

    expect(await screen.findByTestId('structure-total')).toHaveTextContent('5')
  })

  it('lists a row per generation', async () => {
    renderPage()

    expect(await screen.findAllByTestId('generation-row')).toHaveLength(3)
  })

  it('lists a row per branch with its descendant count', async () => {
    renderPage()

    const branch = await screen.findByTestId('branch-row')
    expect(branch).toHaveTextContent('سليمان')
    expect(branch).toHaveTextContent('3')
  })

  it('shows the living and deceased split', async () => {
    renderPage()

    expect(await screen.findByTestId('living-count')).toHaveTextContent('4')
    expect(await screen.findByTestId('deceased-count')).toHaveTextContent('1')
  })

  // Design §5: the histogram must not imply a population it did not measure.
  it('discloses living members whose age is unknown', async () => {
    renderPage()

    expect(await screen.findByTestId('living-without-birth-date')).toHaveTextContent('1')
  })

  // Null longevity means "not measurable" — showing zeros would read as "measured, and zero".
  it('says longevity is unmeasurable rather than showing zeros', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({ lifeStatus: { ...report().lifeStatus, longevity: null } }),
    )

    renderPage()

    expect(await screen.findByTestId('longevity-unavailable')).toBeInTheDocument()
  })

  it('reports a failure instead of rendering an empty screen', async () => {
    vi.mocked(reportsApi.get).mockRejectedValue(new Error('boom'))

    renderPage()

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
```

Add one case to `frontend/src/routes/AppRoutes.test.tsx` — the mock beside the existing ones,
and the assertion beside the other route tests:

```typescript
vi.mock('../features/reports/ReportsPage', () => ({ ReportsPage: () => <p>reports screen</p> }))
```

```typescript
  it('serves the reports screen at /reports', async () => {
    renderAt('/reports')

    expect(await screen.findByText('reports screen')).toBeInTheDocument()
  })
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/reports src/routes/AppRoutes.test.tsx`
Expected: FAIL — cannot resolve `./ReportsPage`.

- [x] **Step 3: Add the i18n keys**

Add to `frontend/src/i18n/locales/en.json`, at the top level beside the existing sections:

```json
  "reports": {
    "title": "Reports",
    "generatedOn": "As of {{date}}",
    "loadFailed": "The reports could not be loaded.",
    "structure": {
      "title": "Family structure",
      "totalMembers": "Members",
      "depth": "Generations deep",
      "leafMembers": "Without children",
      "membersWithChildren": "With children",
      "averageChildren": "Children per parent",
      "generation": "Generation {{number}}",
      "branches": "Branches",
      "descendants": "Descendants"
    },
    "lifeStatus": {
      "title": "Living and deceased",
      "living": "Living",
      "deceased": "Deceased",
      "ages": "Ages of living members",
      "unknownAge": "Living, birth date unknown",
      "longevity": "Lifespan of deceased members",
      "longevityRange": "{{min}} to {{max}} years",
      "longevityMedian": "Median {{years}} years",
      "longevityUnavailable": "No deceased member has both a birth and a death date recorded."
    }
  },
```

Add the Arabic counterpart to `frontend/src/i18n/locales/ar.json`, with the identical key
structure — `locales.test.ts` compares the two key sets and fails on any divergence:

```json
  "reports": {
    "title": "التقارير",
    "generatedOn": "حتى تاريخ {{date}}",
    "loadFailed": "تعذّر تحميل التقارير.",
    "structure": {
      "title": "بنية العائلة",
      "totalMembers": "الأفراد",
      "depth": "عدد الأجيال",
      "leafMembers": "بلا أبناء",
      "membersWithChildren": "لديهم أبناء",
      "averageChildren": "معدل الأبناء لكل والد",
      "generation": "الجيل {{number}}",
      "branches": "الفروع",
      "descendants": "الذرية"
    },
    "lifeStatus": {
      "title": "الأحياء والمتوفون",
      "living": "على قيد الحياة",
      "deceased": "متوفّون",
      "ages": "أعمار الأحياء",
      "unknownAge": "أحياء بتاريخ ميلاد غير معروف",
      "longevity": "أعمار المتوفين",
      "longevityRange": "من {{min}} إلى {{max}} سنة",
      "longevityMedian": "الوسيط {{years}} سنة",
      "longevityUnavailable": "لا يوجد متوفٍّ مسجَّل له تاريخا ميلاد ووفاة معًا."
    }
  },
```

Also add the nav label beside the existing `nav.*` keys — `nav.reports`: `"Reports"` in
English, `"التقارير"` in Arabic.

- [x] **Step 4: Write the components**

Create `frontend/src/features/reports/StructureSection.tsx`:

```typescript
import { useTranslation } from 'react-i18next'
import type { StructureReport } from './types'

/** A labelled figure. The building block of every count-only section. */
const Stat = ({ label, value, testId }: { label: string; value: string; testId?: string }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
    <span style={{ fontSize: 11, color: 'var(--text-3)' }}>{label}</span>
    <strong data-testid={testId} style={{ fontSize: 22 }}>
      {value}
    </strong>
  </div>
)

export const StructureSection = ({ report }: { report: StructureReport }) => {
  const { t, i18n } = useTranslation()
  // Arabic-Indic digits where the locale calls for them, rather than toString().
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  return (
    <section aria-labelledby="structure-heading">
      <h2 id="structure-heading">{t('reports.structure.title')}</h2>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 24 }}>
        <Stat
          label={t('reports.structure.totalMembers')}
          value={number(report.totalMembers)}
          testId="structure-total"
        />
        <Stat
          label={t('reports.structure.depth')}
          value={number(report.depth)}
          testId="structure-depth"
        />
        <Stat
          label={t('reports.structure.membersWithChildren')}
          value={number(report.membersWithChildren)}
        />
        <Stat label={t('reports.structure.leafMembers')} value={number(report.leafMembers)} />
        <Stat
          label={t('reports.structure.averageChildren')}
          value={number(report.averageChildrenPerParent)}
        />
      </div>

      <ul style={{ listStyle: 'none', padding: 0, marginBlockStart: 16 }}>
        {report.generations.map((generation) => (
          <li
            key={generation.generation}
            data-testid="generation-row"
            style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}
          >
            <span>{t('reports.structure.generation', { number: generation.generation })}</span>
            <span>{number(generation.count)}</span>
          </li>
        ))}
      </ul>

      <h3>{t('reports.structure.branches')}</h3>
      <ul style={{ listStyle: 'none', padding: 0 }}>
        {report.branches.map((branch) => (
          <li
            key={branch.id}
            data-testid="branch-row"
            style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}
          >
            <span>{branch.name}</span>
            <span>
              {t('reports.structure.descendants')} {number(branch.descendantCount)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
```

Create `frontend/src/features/reports/LifeStatusSection.tsx`:

```typescript
import { useTranslation } from 'react-i18next'
import type { LifeStatusReport } from './types'

export const LifeStatusSection = ({ report }: { report: LifeStatusReport }) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  return (
    <section aria-labelledby="life-status-heading">
      <h2 id="life-status-heading">{t('reports.lifeStatus.title')}</h2>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 24 }}>
        <div>
          <span style={{ fontSize: 11, color: 'var(--text-3)' }}>
            {t('reports.lifeStatus.living')}
          </span>
          <strong data-testid="living-count" style={{ display: 'block', fontSize: 22 }}>
            {number(report.living)}
          </strong>
        </div>
        <div>
          <span style={{ fontSize: 11, color: 'var(--text-3)' }}>
            {t('reports.lifeStatus.deceased')}
          </span>
          <strong data-testid="deceased-count" style={{ display: 'block', fontSize: 22 }}>
            {number(report.deceased)}
          </strong>
        </div>
      </div>

      <h3>{t('reports.lifeStatus.ages')}</h3>
      <ul style={{ listStyle: 'none', padding: 0 }}>
        {report.livingAges.map((bracket) => (
          <li
            key={bracket.bracket}
            style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}
          >
            <span>{bracket.bracket}</span>
            <span>{number(bracket.count)}</span>
          </li>
        ))}
      </ul>

      {/* Disclosed rather than folded into a bracket: the histogram must not imply a
          population it did not measure (design §5). */}
      <p>
        {t('reports.lifeStatus.unknownAge')}{' '}
        <strong data-testid="living-without-birth-date">
          {number(report.livingWithoutBirthDate)}
        </strong>
      </p>

      <h3>{t('reports.lifeStatus.longevity')}</h3>
      {report.longevity === null ? (
        // "Not measurable", never zeros — zeros would read as a measured result.
        <p data-testid="longevity-unavailable">{t('reports.lifeStatus.longevityUnavailable')}</p>
      ) : (
        <p>
          {t('reports.lifeStatus.longevityRange', {
            min: number(report.longevity.minYears),
            max: number(report.longevity.maxYears),
          })}
          {' · '}
          {t('reports.lifeStatus.longevityMedian', {
            years: number(report.longevity.medianYears),
          })}
        </p>
      )}
    </section>
  )
}
```

Create `frontend/src/features/reports/ReportsPage.tsx`:

```typescript
import { useTranslation } from 'react-i18next'
import { AppShell } from '../../app/AppShell'
import { LifeStatusSection } from './LifeStatusSection'
import { StructureSection } from './StructureSection'
import { useReportsQuery } from './useReports'

export const ReportsPage = () => {
  const { t, i18n } = useTranslation()
  const { data, isPending, isError } = useReportsQuery()

  return (
    <AppShell>
      <h1>{t('reports.title')}</h1>

      {isError && <p role="alert">{t('reports.loadFailed')}</p>}

      {isPending && !isError && <p>{t('common.loading')}</p>}

      {data !== undefined && (
        <>
          {/* The server's reference day, shown rather than re-derived: a client in another
              time zone must not disagree with the figures it is labelling (design §5). */}
          <p style={{ fontSize: 11, color: 'var(--text-3)' }}>
            {t('reports.generatedOn', {
              date: new Intl.DateTimeFormat(i18n.language).format(new Date(data.generatedOn)),
            })}
          </p>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 32 }}>
            <StructureSection report={data.structure} />
            <LifeStatusSection report={data.lifeStatus} />
          </div>
        </>
      )}
    </AppShell>
  )
}
```

> Check how `MembersPage` composes with `AppShell` and how it keys its loading text
> (`grep -n "AppShell\|loading" frontend/src/features/members/MembersPage.tsx`). Match it
> exactly — if `MembersPage` does not wrap itself in `AppShell`, drop the wrapper here and let
> the route provide it, and use whatever loading key already exists instead of `common.loading`.

- [x] **Step 5: Register the route and the nav entry**

In `frontend/src/routes/AppRoutes.tsx`, add the import beside the others and the route beside
`/members`:

```typescript
import { ReportsPage } from '../features/reports/ReportsPage'
```

```typescript
    <Route
      path="/reports"
      element={
        <ProtectedRoute>
          <ReportsPage />
        </ProtectedRoute>
      }
    />
```

In `frontend/src/app/AppShell.tsx`, replace the inert `PendingNavItem` labelled
`t('nav.dashboard')` with a live link, keeping its grid icon exactly as it is:

```typescript
          <Link to="/reports" style={navItemStyle(pathname === '/reports', true)}>
            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
              <rect x="4" y="4" width="7" height="7" rx="1.5" />
              <rect x="13" y="4" width="7" height="7" rx="1.5" />
              <rect x="4" y="13" width="7" height="7" rx="1.5" />
              <rect x="13" y="13" width="7" height="7" rx="1.5" />
            </svg>
            {t('nav.reports')}
          </Link>
```

The reports screen is what that placeholder was standing in for, so this replaces it rather
than adding a seventh item. If nothing else references `nav.dashboard` afterwards, remove the
key from both locale files — `locales.test.ts` checks parity between the two, not that every
key is used, so an orphaned key would rot unnoticed.

- [x] **Step 6: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/reports src/routes src/i18n src/app`
Expected: PASS — the new page tests, the route test, the locale parity test, and the shell tests.

- [x] **Step 7: Commit**

```bash
git add frontend/src/features/reports frontend/src/routes frontend/src/app/AppShell.tsx frontend/src/i18n/locales
git commit -m "feat: add the reports screen with structure and life status

Takes over the inert Dashboard nav placeholder, which is what it was
standing in for. Unmeasurable longevity says so rather than showing
zeros, and living members of unknown age are disclosed beside the
histogram rather than folded into a bracket."
```

---

### Task 11: Reports screen — completeness, upcoming, and activity

**Files:**
- Create: `frontend/src/features/reports/CompletenessSection.tsx`
- Create: `frontend/src/features/reports/UpcomingSection.tsx`
- Create: `frontend/src/features/reports/ActivitySection.tsx`
- Modify: `frontend/src/features/reports/ReportsPage.tsx` (render the three sections)
- Modify: `frontend/src/i18n/locales/en.json`, `frontend/src/i18n/locales/ar.json`
- Test: `frontend/src/features/reports/ReportsPage.test.tsx` (extend)

**Interfaces:**
- Consumes: `CompletenessReport`, `UpcomingReport`, `ActivityReport` (Task 9); `fullName` and
  `indexById` from `frontend/src/features/members/fullName` (existing); `useMembersQuery` from
  `frontend/src/features/members/useMembers` (existing).
- Produces: `CompletenessSection`, `UpcomingSection`, `ActivitySection`.

**Naming:** report rows carry `MemberRef(id, name, parentId)`, so the lineage is composed with
the existing `fullName` helper against the member list — design §7. The page fetches that list
once via `useMembersQuery` and passes the index down; sections never compose names themselves.

- [x] **Step 1: Write the failing tests**

Extend `frontend/src/features/reports/ReportsPage.test.tsx`. Add the members mock beside the
existing mocks:

```typescript
vi.mock('../members/useMembers', () => ({
  useMembersQuery: () => ({
    data: [
      { id: 'a', name: 'سليمان', parentId: null },
      { id: 'b', name: 'فارس', parentId: 'a' },
    ],
  }),
}))
```

Add these cases inside `describe('ReportsPage')`:

```typescript
  it('lists each completeness issue with the true count, not the row count', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({
        completeness: {
          totalMembers: 60,
          completeRecords: 0,
          issues: [
            {
              code: 'MISSING_BIRTH_DATE',
              count: 60,
              members: [{ id: 'b', name: 'فارس', parentId: 'a' }],
            },
          ],
        },
      }),
    )

    renderPage()

    // 60 affected, 1 row returned: the screen must show the 60.
    expect(await screen.findByTestId('issue-count-MISSING_BIRTH_DATE')).toHaveTextContent('60')
  })

  it('links a completeness row to the member in the tree', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({
        completeness: {
          totalMembers: 1,
          completeRecords: 0,
          issues: [
            {
              code: 'MISSING_BIRTH_DATE',
              count: 1,
              members: [{ id: 'b', name: 'فارس', parentId: 'a' }],
            },
          ],
        },
      }),
    )

    renderPage()

    expect(await screen.findByRole('link', { name: /فارس/ })).toHaveAttribute(
      'href',
      '/?memberId=b',
    )
  })

  // Design §7: a bare given name identifies nobody; the lineage comes from the parent chain.
  it('shows a report row under its composed lineage name', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({
        completeness: {
          totalMembers: 1,
          completeRecords: 0,
          issues: [
            {
              code: 'MISSING_BIRTH_DATE',
              count: 1,
              members: [{ id: 'b', name: 'فارس', parentId: 'a' }],
            },
          ],
        },
      }),
    )

    renderPage()

    expect(await screen.findByRole('link', { name: 'فارس سليمان' })).toBeInTheDocument()
  })

  it('lists an upcoming birthday with the age being reached', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({
        upcoming: {
          windowDays: 30,
          birthdayCount: 1,
          anniversaryCount: 0,
          birthdays: [
            {
              member: { id: 'b', name: 'فارس', parentId: 'a' },
              dateOfBirth: '1990-09-01',
              occurrence: '2026-09-01',
              daysAway: 10,
              turningAge: 36,
            },
          ],
          anniversaries: [],
        },
      }),
    )

    renderPage()

    expect(await screen.findByTestId('birthday-row')).toHaveTextContent('36')
  })

  it('says when nothing falls inside the upcoming window', async () => {
    renderPage()

    expect(await screen.findByTestId('upcoming-empty')).toBeInTheDocument()
  })

  it('lists recently added members', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({
        activity: {
          windowDays: 30,
          addedCount: 1,
          editedCount: 0,
          added: [{ member: { id: 'b', name: 'فارس', parentId: 'a' }, at: '2026-08-21T12:00:00Z' }],
          edited: [],
        },
      }),
    )

    renderPage()

    expect(await screen.findByTestId('activity-added-row')).toBeInTheDocument()
  })
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/reports/ReportsPage.test.tsx`
Expected: FAIL — the new test ids are not rendered.

- [x] **Step 3: Add the i18n keys**

Add to the `reports` object in `frontend/src/i18n/locales/en.json`:

```json
    "completeness": {
      "title": "Records needing attention",
      "complete": "{{complete}} of {{total}} records are complete",
      "showingSome": "Showing the first {{shown}}",
      "MISSING_BIRTH_DATE": "No birth date recorded",
      "DECEASED_WITHOUT_DEATH_DATE": "Recorded as deceased with no death date",
      "nothingToFix": "Every record is complete."
    },
    "upcoming": {
      "title": "Next {{days}} days",
      "birthdays": "Birthdays",
      "anniversaries": "Anniversaries",
      "turningAge": "turning {{age}}",
      "yearsSince": "{{years}} years",
      "today": "Today",
      "inDays": "In {{days}} days",
      "empty": "Nothing falls in the next {{days}} days."
    },
    "activity": {
      "title": "Last {{days}} days",
      "added": "Added",
      "edited": "Edited",
      "empty": "Nothing has changed in the last {{days}} days.",
      "note": "Taken from record timestamps, so deletions are not shown."
    }
```

Add the Arabic counterpart with the identical key structure to `ar.json`:

```json
    "completeness": {
      "title": "سجلات تحتاج إلى استكمال",
      "complete": "{{complete}} من {{total}} سجلًا مكتملة",
      "showingSome": "عرض أول {{shown}}",
      "MISSING_BIRTH_DATE": "لا يوجد تاريخ ميلاد",
      "DECEASED_WITHOUT_DEATH_DATE": "مسجَّل متوفّى بلا تاريخ وفاة",
      "nothingToFix": "جميع السجلات مكتملة."
    },
    "upcoming": {
      "title": "الأيام الـ{{days}} القادمة",
      "birthdays": "أعياد الميلاد",
      "anniversaries": "ذكرى الوفاة",
      "turningAge": "يُتمّ {{age}}",
      "yearsSince": "{{years}} سنة",
      "today": "اليوم",
      "inDays": "بعد {{days}} يومًا",
      "empty": "لا شيء خلال الأيام الـ{{days}} القادمة."
    },
    "activity": {
      "title": "آخر {{days}} يومًا",
      "added": "أُضيفوا",
      "edited": "عُدِّلوا",
      "empty": "لم يتغيّر شيء خلال آخر {{days}} يومًا.",
      "note": "مستخرج من طوابع زمن السجلات، لذا لا تظهر عمليات الحذف."
    }
```

- [x] **Step 4: Write the components**

Create `frontend/src/features/reports/CompletenessSection.tsx`:

```typescript
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import type { CompletenessReport, MemberRef } from './types'

interface Props {
  report: CompletenessReport
  /** The member index the lineage is composed from — see design §7. */
  byId: Map<string, NamedNode>
}

export const CompletenessSection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  // The lineage, not the bare given name: `فارس` alone identifies nobody in this model.
  const display = (member: MemberRef) => fullName(member, byId)

  const outstanding = report.issues.filter((issue) => issue.count > 0)

  return (
    <section aria-labelledby="completeness-heading">
      <h2 id="completeness-heading">{t('reports.completeness.title')}</h2>

      <p>
        {t('reports.completeness.complete', {
          complete: number(report.completeRecords),
          total: number(report.totalMembers),
        })}
      </p>

      {outstanding.length === 0 && <p>{t('reports.completeness.nothingToFix')}</p>}

      {outstanding.map((issue) => (
        <div key={issue.code}>
          <h3>
            {t(`reports.completeness.${issue.code}`)}{' '}
            {/* The true count, never members.length: the list is capped at 50 and a client
                that reported the row count would understate the work outstanding. */}
            <span data-testid={`issue-count-${issue.code}`}>{number(issue.count)}</span>
          </h3>

          {issue.count > issue.members.length && (
            <p style={{ fontSize: 11, color: 'var(--text-3)' }}>
              {t('reports.completeness.showingSome', { shown: number(issue.members.length) })}
            </p>
          )}

          <ul style={{ listStyle: 'none', padding: 0 }}>
            {issue.members.map((member) => (
              <li key={member.id}>
                {/* Links into the tree so the worklist is actionable — TreePage reads
                    ?memberId= and preselects (design §8). */}
                <Link to={`/?memberId=${member.id}`}>{display(member)}</Link>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </section>
  )
}
```

Create `frontend/src/features/reports/UpcomingSection.tsx`:

```typescript
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import type { MemberRef, UpcomingReport } from './types'

interface Props {
  report: UpcomingReport
  byId: Map<string, NamedNode>
}

export const UpcomingSection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)
  const display = (member: MemberRef) => fullName(member, byId)

  const when = (daysAway: number) =>
    daysAway === 0
      ? t('reports.upcoming.today')
      : t('reports.upcoming.inDays', { days: number(daysAway) })

  const empty = report.birthdays.length === 0 && report.anniversaries.length === 0

  return (
    <section aria-labelledby="upcoming-heading">
      <h2 id="upcoming-heading">
        {t('reports.upcoming.title', { days: number(report.windowDays) })}
      </h2>

      {empty && (
        <p data-testid="upcoming-empty">
          {t('reports.upcoming.empty', { days: number(report.windowDays) })}
        </p>
      )}

      {report.birthdays.length > 0 && (
        <>
          <h3>{t('reports.upcoming.birthdays')}</h3>
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {report.birthdays.map((birthday) => (
              <li key={birthday.member.id} data-testid="birthday-row">
                <Link to={`/?memberId=${birthday.member.id}`}>{display(birthday.member)}</Link>{' '}
                {/* The age reached on the occurrence, not today's — the server computed it. */}
                <span>
                  {t('reports.upcoming.turningAge', { age: number(birthday.turningAge) })}
                </span>{' '}
                <span>{when(birthday.daysAway)}</span>
              </li>
            ))}
          </ul>
        </>
      )}

      {report.anniversaries.length > 0 && (
        <>
          <h3>{t('reports.upcoming.anniversaries')}</h3>
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {report.anniversaries.map((anniversary) => (
              <li key={anniversary.member.id} data-testid="anniversary-row">
                <Link to={`/?memberId=${anniversary.member.id}`}>
                  {display(anniversary.member)}
                </Link>{' '}
                <span>
                  {t('reports.upcoming.yearsSince', { years: number(anniversary.years) })}
                </span>{' '}
                <span>{when(anniversary.daysAway)}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}
```

Create `frontend/src/features/reports/ActivitySection.tsx`:

```typescript
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import type { ActivityEntry, ActivityReport } from './types'

interface Props {
  report: ActivityReport
  byId: Map<string, NamedNode>
}

export const ActivitySection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)
  const when = (iso: string) => new Intl.DateTimeFormat(i18n.language).format(new Date(iso))

  const rows = (entries: ActivityEntry[], testId: string) => (
    <ul style={{ listStyle: 'none', padding: 0 }}>
      {entries.map((entry) => (
        <li key={entry.member.id} data-testid={testId}>
          <Link to={`/?memberId=${entry.member.id}`}>{fullName(entry.member, byId)}</Link>{' '}
          <span>{when(entry.at)}</span>
        </li>
      ))}
    </ul>
  )

  const empty = report.added.length === 0 && report.edited.length === 0

  return (
    <section aria-labelledby="activity-heading">
      <h2 id="activity-heading">
        {t('reports.activity.title', { days: number(report.windowDays) })}
      </h2>

      {empty ? (
        <p>{t('reports.activity.empty', { days: number(report.windowDays) })}</p>
      ) : (
        <>
          {report.added.length > 0 && (
            <>
              <h3>
                {t('reports.activity.added')} {number(report.addedCount)}
              </h3>
              {rows(report.added, 'activity-added-row')}
            </>
          )}

          {report.edited.length > 0 && (
            <>
              <h3>
                {t('reports.activity.edited')} {number(report.editedCount)}
              </h3>
              {rows(report.edited, 'activity-edited-row')}
            </>
          )}
        </>
      )}

      {/* Stated plainly rather than left to be discovered: this reads record timestamps, so a
          deleted member leaves no trace here. The real fix is AuditLog (design §9). */}
      <p style={{ fontSize: 11, color: 'var(--text-3)' }}>{t('reports.activity.note')}</p>
    </section>
  )
}
```

Modify `frontend/src/features/reports/ReportsPage.tsx` — index the members once and pass it
to the three list sections. Add the imports:

```typescript
import { indexById } from '../members/fullName'
import { useMembersQuery } from '../members/useMembers'
import { ActivitySection } from './ActivitySection'
import { CompletenessSection } from './CompletenessSection'
import { UpcomingSection } from './UpcomingSection'
```

Inside the component, above the return:

```typescript
  // Report rows carry (id, name, parentId); the lineage is composed here with the helper the
  // members screen already uses, so the naming rule lives in one place (design §7).
  const { data: members } = useMembersQuery()
  const byId = indexById(members ?? [])
```

And inside the `data !== undefined` block, after `LifeStatusSection`:

```typescript
            <CompletenessSection report={data.completeness} byId={byId} />
            <UpcomingSection report={data.upcoming} byId={byId} />
            <ActivitySection report={data.activity} byId={byId} />
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/reports src/i18n`
Expected: PASS — the page suite and the locale parity test.

- [x] **Step 6: Commit**

```bash
git add frontend/src/features/reports frontend/src/i18n/locales
git commit -m "feat: add the completeness, upcoming and activity sections

Rows show the composed lineage name and link into the tree, so the
completeness list is a worklist rather than a list of problems. Capped
lists render the true count and say how many rows they are showing."
```

---

### Task 12: Preselecting a member in the tree from a link

Without this the links added in Task 11 land on the tree with nothing selected.

**Files:**
- Modify: `frontend/src/features/tree/TreePage.tsx:53` (seed `selectedId` from the search parameter)
- Test: `frontend/src/features/tree/TreePage.test.tsx` (add cases)

**Interfaces:**
- Consumes: `useSearchParams` from `react-router-dom`.
- Produces: `/?memberId=<id>` preselects that member. Absent parameter behaves exactly as before.

- [x] **Step 1: Write the failing test**

Read `frontend/src/features/tree/TreePage.test.tsx` first and reuse its fixture ids, its render
helper, and its existing assertions style. The suite renders inside `MemoryRouter`; add a
helper that accepts an initial path if one does not already exist:

```typescript
const renderPageAt = (path: string) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <TreePage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}
```

Then add these three cases, substituting the id and name of a member from the suite's own
fixture for `<memberId>` and `<memberName>`:

```typescript
  // Report rows link here with ?memberId=, so the member must be selected on arrival.
  it('preselects the member named by the memberId parameter', async () => {
    renderPageAt('/?memberId=<memberId>')

    expect(await screen.findByText('<memberName>')).toBeInTheDocument()
  })

  it('ignores a memberId matching no member rather than failing', async () => {
    renderPageAt('/?memberId=00000000-0000-0000-0000-000000000000')

    // The tree still renders; nothing is selected.
    expect(await screen.findByText('<a name always present in the tree>')).toBeInTheDocument()
  })

  it('selects nothing when the parameter is absent, as before', async () => {
    renderPageAt('/')

    expect(await screen.findByText('<a name always present in the tree>')).toBeInTheDocument()
  })
```

> Assert on the member panel the way the existing suite already does — by the member's name, or
> by whatever role or label its tests already use. Do not add `data-testid` attributes to
> `TreePage` for this task.

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/tree/TreePage.test.tsx`
Expected: FAIL — the member is not selected on arrival.

- [x] **Step 3: Write the implementation**

In `frontend/src/features/tree/TreePage.tsx`, add the import:

```typescript
import { useSearchParams } from 'react-router-dom'
```

Replace the initialiser on line 53:

```typescript
  // Seeded from the URL so a report row can link straight to a member (design §8). A lazy
  // initialiser, not an effect: the parameter is the starting selection, not a binding — a
  // later click must be free to select something else without the URL fighting it back.
  const [searchParams] = useSearchParams()
  const [selectedId, setSelectedId] = useState<string | null>(() => searchParams.get('memberId'))
```

An id matching no member needs no special handling: `findNode` on line 96 already returns
`undefined`, and every consumer guards on that — which is what makes a stale link degrade to
the plain tree instead of an error.

- [x] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/tree`
Expected: PASS — the new cases and the whole existing tree suite.

- [x] **Step 5: Commit**

```bash
git add frontend/src/features/tree
git commit -m "feat: preselect a tree member from ?memberId=

Gives report rows somewhere to link to. A lazy initialiser rather than an
effect, so a later click is not fought back by the URL, and an id
matching nothing degrades to the plain tree."
```

---

### Task 13: Full verification and documentation

**Files:**
- Modify: `README.md` (one section after the screens paragraph)

- [x] **Step 1: Run every test**

Run: `dotnet test`
Expected: PASS — all four test projects. (Docker must be running.)

Run: `cd frontend && npm test`
Expected: PASS — the whole component suite.

- [x] **Step 2: Run the linter and the type check**

Run: `cd frontend && npm run lint && npx tsc --noEmit`
Expected: no errors. Fix anything reported before continuing.

> If either script does not exist, check `frontend/package.json` for the actual names and run
> those instead.

- [x] **Step 3: Document the screen**

Add to `README.md`, after the paragraph describing the members screen and the tree outline:

```markdown
The reports screen is at `/reports`. It answers questions the tree cannot: how deep and how
balanced the family is, how the living and deceased divide, which records still need a date,
whose birthday or death anniversary falls in the next 30 days, and what changed in the last 30.
Everything is computed on request from the members already stored — there is no reporting
table and no scheduled job.

`GET /api/v1/reports` returns all five sections in one payload and takes no parameters: the
windows (30 days) and list caps (50) are server-side constants. Every capped list carries its
untruncated count alongside, so a client must render that count and never `members.length`.
The endpoint is guarded by `FamilyTree.View`, the same permission as the tree and the PDF
export — the reports aggregate data that permission already exposes.

Recent activity is derived from record timestamps, not from an audit log, so it cannot show
deletions or attribute a change to a user. `Audit.View` exists in the permission catalog but
no `AuditLog` entity does; a real audit report is blocked on that.
```

- [x] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: describe the reports screen and endpoint

Records the two things a client can get wrong: the untruncated counts
beside every capped list, and that recent activity is timestamp-derived
rather than an audit log."
```

---

## Plan Self-Review

**Spec coverage.** Every section of the design maps to a task: §3 architecture → the file layout in Tasks 1–8; §4 endpoint and permission → Task 8; §5 contracts → Tasks 1–7, each record created in the task that first needs it; §6 computation rules → Tasks 1–6, each rule carrying the test that pins it; §7 member identity → `MemberRef` in Task 1, the composition decision in Task 11; §8 frontend → Tasks 9–12, with `?memberId=` isolated in Task 12; §9 audit gap → stated in `ActivityReport`, surfaced in the UI note in Task 11, and recorded in the README in Task 13; §10 testing → every task's test step, with the named edge cases distributed to the calculator that owns each.

**Type consistency.** `MemberRef(Id, Name, ParentId)` is used unchanged by completeness, upcoming, and activity. `ReportLimits.MaxMembersPerList` caps all five member-bearing lists. `Ages.YearsBetween` serves both living ages and occurrence ages. `GenerationIndex.Build` returns `IReadOnlyDictionary<Guid,int>`, which is the second parameter of both `StructureCalculator.Calculate` and `LifeStatusCalculator.Calculate`. The frontend `types.ts` mirrors each record field-for-field in camelCase.

**Verification points**, flagged inline in the task that depends on each rather than assumed: whether `FamilyTree.Contracts` references `FamilyTree.Domain` (Task 1), whether the integration test project has `FakeTimeProvider` (Task 7), whether `TimeProvider` is registered (Task 7), how `MembersPage` composes with `AppShell` and names its loading key (Task 10), and the existing fixtures and assertion style in `TreePage.test.tsx` (Task 12).
