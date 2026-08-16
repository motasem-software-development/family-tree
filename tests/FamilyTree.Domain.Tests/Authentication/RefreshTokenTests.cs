using FluentAssertions;
using FamilyTree.Domain.Authentication;

namespace FamilyTree.Domain.Tests.Authentication;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private static RefreshToken Issue() =>
        RefreshToken.Issue(UserId, TenantId, "hash-of-token", Now, Lifetime);

    [Fact]
    public void Issue_sets_expiry_from_the_lifetime_and_leaves_the_token_active()
    {
        var token = Issue();

        token.UserId.Should().Be(UserId);
        token.TenantId.Should().Be(TenantId);
        token.TokenHash.Should().Be("hash-of-token");
        token.ExpiresAt.Should().Be(Now + Lifetime);
        token.RevokedAt.Should().BeNull();
        token.IsActive(Now).Should().BeTrue();
    }

    [Fact]
    public void A_token_is_inactive_once_it_expires()
    {
        var token = Issue();

        token.IsActive(Now + Lifetime).Should().BeFalse();
        token.IsActive(Now + Lifetime + TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Revoke_deactivates_the_token_and_records_its_replacement()
    {
        var token = Issue();
        var later = Now.AddHours(1);

        token.Revoke(later, "hash-of-next-token");

        token.RevokedAt.Should().Be(later);
        token.ReplacedByTokenHash.Should().Be("hash-of-next-token");
        token.IsActive(later).Should().BeFalse();
    }

    [Fact]
    public void Revoking_an_already_revoked_token_keeps_the_first_revocation_time()
    {
        var token = Issue();
        var first = Now.AddHours(1);

        token.Revoke(first, null);
        token.Revoke(Now.AddHours(5), null);

        token.RevokedAt.Should().Be(first);
    }
}
