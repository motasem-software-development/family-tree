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
