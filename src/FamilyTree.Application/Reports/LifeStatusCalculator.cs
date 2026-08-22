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
