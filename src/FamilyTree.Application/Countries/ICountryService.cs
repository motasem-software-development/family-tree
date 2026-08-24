using FamilyTree.Contracts.Countries;

namespace FamilyTree.Application.Countries;

public interface ICountryService
{
    /// <summary>Every seeded country, ordered by English name. Never tenant-filtered.</summary>
    Task<IReadOnlyList<CountryResponse>> ListAsync(CancellationToken ct = default);
}
