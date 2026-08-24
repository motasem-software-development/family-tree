using FamilyTree.Application.Countries;
using FamilyTree.Contracts.Countries;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Countries;

/// <summary>
/// Reference data, so no tenant filter applies and none is wanted — every tenant sees the same
/// list. Ordered by English name server-side; the client re-sorts for Arabic, where collation
/// differs from the byte order Postgres would give.
/// </summary>
public sealed class CountryService(ApplicationDbContext context) : ICountryService
{
    public async Task<IReadOnlyList<CountryResponse>> ListAsync(CancellationToken ct = default) =>
        await context.Countries
            .AsNoTracking()
            .OrderBy(c => c.NameEn)
            .Select(c => new CountryResponse(c.Id, c.Code, c.NameAr, c.NameEn, c.DialCode))
            .ToListAsync(ct);
}
