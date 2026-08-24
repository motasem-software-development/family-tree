using FamilyTree.Domain.Countries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");
        builder.HasKey(x => x.Id);

        // Identity rather than a client-assigned value: the seeder never supplies an id, and a
        // reference row's identity carries no meaning beyond being stable.
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.Code).IsRequired().HasMaxLength(2);
        builder.Property(x => x.NameAr).IsRequired().HasMaxLength(Country.MaxNameLength);
        builder.Property(x => x.NameEn).IsRequired().HasMaxLength(Country.MaxNameLength);
        builder.Property(x => x.DialCode).IsRequired().HasMaxLength(8);

        // The seeder's idempotency key. Unique so a concurrent double-seed fails loudly rather
        // than silently producing two rows for one country.
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
