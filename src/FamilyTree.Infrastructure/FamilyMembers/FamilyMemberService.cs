using FamilyTree.Application.Common;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Common;
using FamilyTree.Domain.FamilyMembers;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyTree.Infrastructure.FamilyMembers;

/// <summary>
/// Every query here runs through the tenant query filter, so "not found" and "belongs to
/// another tenant" are the same code path — which is what makes the uniform 404 in design
/// spec §4.4 true by construction rather than by discipline.
/// </summary>
public sealed class FamilyMemberService(
    ApplicationDbContext context,
    ITenantContext tenant,
    TimeProvider timeProvider) : IFamilyMemberService
{
    /// <summary>PostgreSQL SQLSTATE for a foreign key violation.</summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary>PostgreSQL SQLSTATE for a unique violation.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>The filtered unique index behind the per-tenant national ID rule.</summary>
    private const string NationalIdIndex = "ux_family_members_tenant_national_id";

    public async Task<FamilyMemberResponse> CreateAsync(
        CreateFamilyMemberRequest request, CancellationToken ct = default)
    {
        var tree = await context.FamilyTrees.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("FAMILY_TREE_NOT_FOUND", "This tenant has no family tree.");

        if (request.ParentId is { } parentId && parentId != Guid.Empty)
        {
            // Filtered lookup: a parent in another tenant is simply not there.
            var parentExists = await context.FamilyMembers
                .AnyAsync(m => m.Id == parentId && m.FamilyTreeId == tree.Id, ct);

            if (!parentExists)
                throw new DomainException("MEMBER_PARENT_NOT_FOUND", "The specified parent does not exist.");
        }

        var contact = await ResolveContactAsync(
            request.NationalId, request.MobileNumber, request.WhatsAppNumber, request.CountryId, ct);

        var member = FamilyMember.Create(
            tenant.TenantId, tree.Id, request.ParentId, request.Name, timeProvider.GetUtcNow(),
            request.DateOfBirth, request.DateOfDeath, request.IsDeceased, contact);

        context.FamilyMembers.Add(member);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: UniqueViolation } pg
                  && pg.ConstraintName == NationalIdIndex)
        {
            // Caught rather than pre-checked with a SELECT: check-then-insert races, and the
            // index is the only thing that actually holds the invariant. A ConflictException
            // (409) rather than a DomainException (400) because this depends on current state,
            // not on the request being malformed.
            throw new ConflictException(
                "MEMBER_NATIONAL_ID_DUPLICATE",
                "Another member already has this national ID.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ForeignKeyViolation })
        {
            // The pre-check above is check-then-act and can lose a race to a concurrent delete
            // of the parent. The raw fk_member_parent violation carries no code on its own, so
            // it must be mapped to the same code the pre-check emits (spec §26 / import races).
            throw new DomainException("MEMBER_PARENT_NOT_FOUND", "The specified parent does not exist.");
        }

        return await MapWithCountryAsync(member, ct);
    }

    public async Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return member is null ? null : await MapWithCountryAsync(member, ct);
    }

    public async Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default)
    {
        var rows = await context.FamilyMembers
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                Member = m,
                CountryCode = context.Countries
                    .Where(c => c.Id == m.CountryId)
                    .Select(c => c.Code)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.Select(row => Map(row.Member, row.CountryCode)).ToList();
    }

    public Task<FamilyMemberSearchResponse> SearchAsync(
        string query, int limit, int offset, CancellationToken ct = default) =>
        // The only read here that does NOT go through the tenant query filter, because it is
        // raw SQL. FamilyMemberSearchQuery re-establishes the guarantee with an explicit
        // predicate on every table reference — see the class comment there.
        FamilyMemberSearchQuery.ExecuteAsync(context, tenant.TenantId, query, limit, offset, ct);

    public async Task<FamilyMemberResponse> UpdateAsync(
        Guid id, UpdateFamilyMemberRequest request, CancellationToken ct = default)
    {
        if (request.ParentId is not null || request.TenantId is not null || request.FamilyTreeId is not null)
            throw new DomainException(
                "MEMBER_FIELD_NOT_UPDATABLE",
                "Parent, tenant, and family tree cannot be changed through this endpoint.");

        // Tracked (not AsNoTracking): SaveChanges needs the entity in the change tracker.
        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // Design spec §3.2, layer 2: an explicit ownership assertion in the application
        // service, independent of the EF global query filter (layer 1). Deliberately
        // redundant today — the filter already hides other tenants' rows — so that a future
        // change to layer 1 (e.g. an .IgnoreQueryFilters() lookup) cannot silently open a
        // cross-tenant write. Same exception, same code, same message as "no such member":
        // the two cases must stay indistinguishable (design spec §4.4).
        if (member.TenantId != tenant.TenantId)
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        var contact = await ResolveContactAsync(
            request.NationalId, request.MobileNumber, request.WhatsAppNumber, request.CountryId, ct);

        member.Update(
            request.Name,
            request.DateOfBirth,
            request.DateOfDeath,
            request.IsDeceased,
            contact,
            timeProvider.GetUtcNow());

        // Load-bearing. EF builds `UPDATE ... WHERE id = @id AND version = @original`, and
        // `@original` defaults to the value it just READ — which always matches, making the
        // concurrency token inert. Substituting the version the CLIENT held is what turns a
        // stale write into a conflict instead of a silent overwrite.
        context.Entry(member).Property(m => m.Version).OriginalValue = request.Version;

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "CONCURRENCY_CONFLICT", "This member was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: UniqueViolation } pg
                  && pg.ConstraintName == NationalIdIndex)
        {
            // Caught rather than pre-checked with a SELECT: check-then-insert races, and the
            // index is the only thing that actually holds the invariant. A ConflictException
            // (409) rather than a DomainException (400) because this depends on current state,
            // not on the request being malformed.
            throw new ConflictException(
                "MEMBER_NATIONAL_ID_DUPLICATE",
                "Another member already has this national ID.");
        }

        return await MapWithCountryAsync(member, ct);
    }

    public async Task<FamilyMemberResponse> MoveAsync(
        Guid id, MoveFamilyMemberRequest request, CancellationToken ct = default)
    {
        // One transaction for the check and the write, so the CTE reads the snapshot the write
        // lands on. Design spec §4.6 also puts an audit insert in here; there is no audit_logs
        // table yet, and the transaction exists so adding it later is one statement rather
        // than a restructuring.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        // Design §3.2: two moves can each be acyclic against their own snapshot and jointly
        // form a cycle. The lock is transaction-scoped and per-tenant, exactly as
        // AdministratorGuard.SerializeOnTenantAsync does it for the last-administrator rule.
        // The GUID is folded to a bigint because the advisory-lock namespace is one bigint; a
        // collision between two tenants costs contention, never a wrong answer.
        var lockKey = BitConverter.ToInt64(tenant.TenantId.ToByteArray(), 0);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", ct);

        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // Design spec §3.2, layer 2: see the identical assertion in UpdateAsync for rationale.
        if (member.TenantId != tenant.TenantId)
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        if (request.ParentId is { } targetId && targetId != Guid.Empty)
        {
            // Same tree as well as same tenant: cross-tree moves are out of scope, and this is
            // the check that keeps them out. Reported as MEMBER_NOT_FOUND rather than a
            // distinct code — from the client's side both mean "that id names nothing here".
            var targetExists = await context.FamilyMembers
                .AnyAsync(m => m.Id == targetId && m.FamilyTreeId == member.FamilyTreeId, ct);

            if (!targetExists)
                throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

            if (await CycleCheckQuery.WouldCreateCycleAsync(context, tenant.TenantId, id, targetId, ct))
                throw new ConflictException(
                    "MOVE_CREATES_CYCLE",
                    "This member cannot be moved under their own descendant.");
        }

        member.MoveTo(request.ParentId, timeProvider.GetUtcNow());

        // Load-bearing for the same reason as in UpdateAsync: without it EF compares the
        // version it just read, and the concurrency token is inert.
        context.Entry(member).Property(m => m.Version).OriginalValue = request.Version;

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "CONCURRENCY_CONFLICT", "This member was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ForeignKeyViolation })
        {
            // Check-then-act: the targetExists check above can lose a race to a concurrent
            // delete of the target between the check and this save. The raw fk_member_parent
            // violation carries no code of its own, so it must be mapped to the same
            // MEMBER_NOT_FOUND the pre-check would have thrown -- the target has vanished, and
            // the client must not be able to tell that apart from an id that never existed
            // (design spec section 4.4).
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");
        }

        await transaction.CommitAsync(ct);

        return await MapWithCountryAsync(member, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var member = await context.FamilyMembers.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // Design spec §3.2, layer 2: see the identical assertion in UpdateAsync for rationale.
        if (member.TenantId != tenant.TenantId)
            throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.");

        // The FK's OnDelete(Restrict) would also stop this, but a DbUpdateException carries no
        // stable code for the client. Checking first is what makes the 409 contractual
        // (technical specification §26).
        var hasChildren = await context.FamilyMembers.AnyAsync(m => m.ParentId == id, ct);
        if (hasChildren)
            throw new ConflictException(
                "MEMBER_HAS_CHILDREN", "This member cannot be deleted because they have children.");

        context.FamilyMembers.Remove(member);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: ForeignKeyViolation })
        {
            // The pre-check above raced a concurrent create of a child under this member. Map
            // the raw fk_member_parent violation to the same coded 409 the pre-check emits.
            throw new ConflictException(
                "MEMBER_HAS_CHILDREN", "This member cannot be deleted because they have children.");
        }
    }

    /// <summary>
    /// Resolves the contact details against the country list: the country must exist, and a
    /// supplied phone number must start with that country's dialing code.
    ///
    /// The dial-code check lives here rather than in the aggregate because it needs a row the
    /// aggregate cannot read (design §5.4, refined). It raises the same MEMBER_PHONE_INVALID
    /// code the aggregate's shape check does, so the split is invisible to clients.
    ///
    /// With no country selected there is nothing to check against, and the number is accepted on
    /// shape alone — a member living abroad may well keep a number from somewhere else.
    /// </summary>
    private async Task<ContactDetails> ResolveContactAsync(
        string? nationalId, string? mobile, string? whatsApp, int? countryId, CancellationToken ct)
    {
        if (countryId is not { } id)
            return new ContactDetails(nationalId, mobile, whatsApp, null);

        var dialCode = await context.Countries
            .Where(c => c.Id == id)
            .Select(c => c.DialCode)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException(
                "MEMBER_COUNTRY_NOT_FOUND", "The specified country does not exist.");

        EnsureDialCodeAgrees(mobile, dialCode);
        EnsureDialCodeAgrees(whatsApp, dialCode);

        return new ContactDetails(nationalId, mobile, whatsApp, id);
    }

    /// <summary>
    /// Separators are stripped here as well as in the aggregate, because this check runs first
    /// and a number written "+970 599 123 456" must compare against the same canonical form the
    /// aggregate will eventually store. <see cref="ContactDetails.NormalizePhone"/> is the single
    /// shared definition of "separator" so the two checks cannot drift apart.
    /// </summary>
    private static void EnsureDialCodeAgrees(string? phone, string dialCode)
    {
        // Blank is "not supplied"; the aggregate normalizes it to null.
        var normalized = ContactDetails.NormalizePhone(phone);
        if (normalized is null) return;

        if (!normalized.StartsWith(dialCode, StringComparison.Ordinal))
            throw new DomainException(
                "MEMBER_PHONE_INVALID",
                $"This phone number does not match the selected country's dialing code ({dialCode}).");
    }

    /// <summary>
    /// The country code for one member, or null when they have no country. A separate keyed
    /// lookup rather than a navigation property: the aggregate deliberately holds only
    /// CountryId, and one read of a 22-row table is not worth complicating the entity for.
    /// </summary>
    private async Task<FamilyMemberResponse> MapWithCountryAsync(
        FamilyMember member, CancellationToken ct)
    {
        if (member.CountryId is not { } id) return Map(member);

        var code = await context.Countries
            .Where(c => c.Id == id)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(ct);

        return Map(member, code);
    }

    internal static FamilyMemberResponse Map(FamilyMember member, string? countryCode = null) => new(
        member.Id, member.Name, member.ParentId, member.Version, member.CreatedAt, member.UpdatedAt,
        member.DateOfBirth, member.DateOfDeath, member.IsDeceased,
        member.NationalId, member.MobileNumber, member.WhatsAppNumber, member.CountryId, countryCode);
}
