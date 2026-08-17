using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Seeds the human-reviewed family tree reconstructed from the source PDF (Phase 2.5 Task
    /// 7; see docs/import/family-tree.json, 349 members, single root داوود, max depth 9). This
    /// REPLACES the 14-member demo family that Phase 2 used for smoke testing (human decision,
    /// recorded in the task-7 brief) — the demo set was scaffolding, not real data.
    ///
    /// Ordering: the migration inserts one generation at a time, root first, so that
    /// fk_member_parent (parent_id, family_tree_id) -> (id, family_tree_id) is satisfied row by
    /// row as each INSERT runs — the composite self-FK is intentionally invisible to EF's
    /// change tracker (see FamilyMemberConfiguration), so EF cannot topologically order a
    /// parent before its child within one SaveChanges. Raw SQL sidesteps that entirely.
    ///
    /// The tenant and family tree rows use fixed, deterministic GUIDs and the same slug/name
    /// defaults as SEED_TENANT_SLUG / SEED_FAMILY_TREE_NAME (see .env.example, docker-compose.yml).
    /// DatabaseSeeder's SeedTenantAsync/SeedFamilyTreeAsync look up by slug/tenant respectively
    /// and are idempotent, so when it runs after this migration it finds these rows already
    /// present and reuses them rather than creating new ones — no domain code changes needed.
    /// Member ids are deterministic (derived from the artifact's integer id), so the migration
    /// is reproducible and safe to re-run against a freshly migrated database (as every
    /// integration test does).
    /// </summary>
    public partial class SeedImportedFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE
    v_tenant_id uuid;
    v_tree_id uuid;
    v_now timestamptz := '2026-08-17 00:00:00+00';
BEGIN
    -- Reuse the tenant/family tree DatabaseSeeder already created if this migration
    -- runs against a database seeded before this migration existed (idempotent lookup
    -- by slug/tenant, same as DatabaseSeeder.SeedTenantAsync/SeedFamilyTreeAsync). Fall
    -- back to fixed, deterministic ids on a fresh database so DatabaseSeeder's later,
    -- idempotent run finds and reuses these same rows instead of creating new ones.
    SELECT id INTO v_tenant_id FROM tenants WHERE slug = 'al-saqqa';
    IF v_tenant_id IS NULL THEN
        v_tenant_id := '11111111-1111-4111-8111-111111111111';
        INSERT INTO tenants (id, name, slug, is_active, created_at, updated_at)
        VALUES (v_tenant_id, 'Al-Saqqa Family', 'al-saqqa', true, v_now, v_now);
    END IF;

    SELECT id INTO v_tree_id FROM family_trees WHERE tenant_id = v_tenant_id;
    IF v_tree_id IS NULL THEN
        v_tree_id := '22222222-2222-4222-8222-222222222222';
        INSERT INTO family_trees (id, tenant_id, name, is_active, created_at, updated_at)
        VALUES (v_tree_id, v_tenant_id, 'عائلة السقا', true, v_now, v_now);
    END IF;

    -- Replace whatever this tree already holds (Phase 2 demo family or a prior run of
    -- this migration) with the reviewed import. Safe as one statement: it removes the
    -- whole set at once rather than deleting parents while children still reference them.
    DELETE FROM family_members WHERE family_tree_id = v_tree_id;

    -- Generation 0: 1 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000015d', v_tenant_id, v_tree_id, NULL, 'داوود', 1, v_now, v_now);

    -- Generation 1: 4 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f6', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015d', 'طالب', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f7', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015d', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000fa', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015d', 'سليمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000159', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015d', 'سلمان', 1, v_now, v_now);

    -- Generation 2: 9 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f8', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f7', 'ابراهيم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f9', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f7', 'حسن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000fb', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fa', 'حسن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000fc', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fa', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000fd', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fa', 'سلمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000156', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fa', 'داوود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000015a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000159', 'أمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000015c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000159', 'فارس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000015b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000159', 'ممد', 1, v_now, v_now);

    -- Generation 3: 7 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fd', 'اسماعيل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000125', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fd', 'عبدالعزيز', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000013c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fd', 'يحي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000142', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fd', 'زكريا', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000001', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015a', 'علي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ba', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015c', 'يونس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000bb', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015c', 'يوسف', 1, v_now, v_now);

    -- Generation 4: 27 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ff', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'سعدالدين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000109', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'عزمي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000114', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'عوني', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000118', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'موسى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000119', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'جواد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000123', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000126', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'هاني', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000129', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'مازن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000012e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'منذر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000133', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'منير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000136', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'سلمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000139', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000013d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013c', 'إياد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000140', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013c', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000143', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'شوقي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000146', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'داوود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000014a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'أسامة', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000014e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000152', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'سليمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000153', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'عيسى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000002', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000001', 'مصطفى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000071', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000001', 'عثمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000072', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000001', 'هاشم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000073', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000001', 'خليل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000074', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000001', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000bc', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bb', 'ديب', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c7', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bb', 'سعيد', 1, v_now, v_now);

    -- Generation 5: 56 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000100', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ff', 'إسماعيل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000106', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ff', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000108', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ff', 'موسى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000010a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000109', 'إياد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000010d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000109', 'موسى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000110', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000109', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000115', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000114', 'أشرف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000117', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000114', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000011a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000119', 'هاني', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000011d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000119', 'سامي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000011f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000119', 'رامي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000121', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000119', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000122', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000119', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000124', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000123', 'نبيل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000127', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000126', 'عبدالعزيز', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000128', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000126', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000012a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000129', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000012b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000129', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000012c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000129', 'عبدالعزيز', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000012d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000129', 'يوسف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000012f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012e', 'فيصل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000130', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012e', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000131', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012e', 'طلال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000132', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012e', 'عبدالإله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000134', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000133', 'سعيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000135', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000133', 'عبدالعزيز', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000137', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000136', 'سلطان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000138', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000136', 'ريان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000013a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000139', 'سعود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000013b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000139', 'نواف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000013e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013d', 'يحي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000013f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013d', 'هتان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000141', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000140', 'سلمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000144', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000143', 'زكريا', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000145', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000143', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000147', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000146', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000148', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000146', 'آدم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000149', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000146', 'نوح', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000014b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014a', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000014c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014a', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000014d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014a', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000014f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014e', 'يوسف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000150', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014e', 'عبدالعزيز', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000151', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014e', 'تميم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000154', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000153', 'أمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000155', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000153', 'زين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000003', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000002', 'عمر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000033', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000002', 'درويش', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000075', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000074', 'علي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000008f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000074', 'عبدالخالق', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b3', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000074', 'عايش', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000bd', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bc', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c7', 'رمضان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000da', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c7', 'شعبان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e8', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c7', 'رفيق', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c7', 'عيد', 1, v_now, v_now);

    -- Generation 6: 58 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000101', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000100', 'سعدالدين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000102', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000100', 'حسام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000103', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000100', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000104', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000100', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000105', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000100', 'زين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000107', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000106', 'تيم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000010b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010a', 'عزمي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000010c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010a', 'كريم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000010e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010d', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000010f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010d', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000111', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000110', 'أيهم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000112', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000110', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000113', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000110', 'كنان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000116', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000115', 'عوني', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000011b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011a', 'عبدالجواد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000011c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011a', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000011e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011d', 'براء', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000120', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011f', 'إلياس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000004', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000003', 'سليم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000005', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000003', 'خليل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000001c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000003', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000028', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000003', 'رفيق', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000034', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'مصطفى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000041', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'هاشم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'عبدالكريم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000063', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'عبدالمجيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000006a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'عبدالناصر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000006d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000076', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000075', 'وصفي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000008b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000075', 'عثمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000090', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008f', 'طالب', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000097', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008f', 'عباس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a3', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008f', 'تيسير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ad', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008f', 'تحسين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b4', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b3', 'أكرم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b6', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b3', 'نعيم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000be', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bd', 'ديب', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000bf', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bd', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c3', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bd', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c6', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bd', 'يوسف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c9', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'ناض', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ca', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'رائد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000cc', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'سائد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d0', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'سعيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d2', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d6', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d8', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000db', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000da', 'نبيل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000df', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000da', 'صلاح', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e2', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000da', 'سعيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e4', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000da', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e9', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e8', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ea', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e8', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ee', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed', 'سعيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f0', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed', 'حازم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f1', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f4', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f5', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed', 'يوسف', 1, v_now, v_now);

    -- Generation 7: 90 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000006', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'عماد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000000b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'سمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000000f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'إبراهيم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000013', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'وسام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000016', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'حسام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000018', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000001d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001c', 'طارق', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000021', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001c', 'ناض', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000022', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001c', 'عمر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000026', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001c', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000029', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000028', 'اياد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000002c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000028', 'رائد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000002d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000028', 'ياسر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000030', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000028', 'ايهاب', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000032', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000028', 'زياد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000035', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000034', 'درويش', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000003b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000034', 'فرج', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000003c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000034', 'فضل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000042', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'هشام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000004a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'عصام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000053', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'بسام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000055', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'أيمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000057', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000058', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'عبدالشافي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000005c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', 'حازم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000005d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', 'عادل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000005e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000005f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000062', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000064', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000063', 'معتصم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000066', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000063', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000069', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000063', 'مؤمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000006b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006a', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000006c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006a', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000006e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006d', 'براء', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000006f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006d', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000070', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006d', 'آدم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000077', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000076', 'جمال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000007e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000076', 'علاء', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000007f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000076', 'نعمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000082', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000076', 'عامر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000008c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008b', 'علي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000008e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008b', 'عاطف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000091', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000090', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000094', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000090', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000098', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'عبدالخالق', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000009e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'حيدر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000009f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a0', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a1', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'سليم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a2', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'عيسى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a4', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a3', 'نائل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a8', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a3', 'سامح', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ab', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a3', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ae', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ad', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b0', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ad', 'بلال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b2', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ad', 'طه', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b5', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b4', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b7', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b6', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b8', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b6', 'بيان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b9', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b6', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c0', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bf', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c1', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bf', 'مؤمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c2', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bf', 'يامن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c4', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c3', 'لؤي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000c5', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c3', 'قصي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000cb', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ca', 'أمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000cd', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cc', 'رمضان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ce', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cc', 'بلال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000cf', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cc', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d1', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d0', 'فارس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d3', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d2', 'سعيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d4', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d2', 'جود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d5', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d2', 'يزن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d7', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d6', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000d9', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d8', 'أسامة', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000dc', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000db', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000dd', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000db', 'يوسف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000de', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000db', 'ريان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e0', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000df', 'أنس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e1', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000df', 'أمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e3', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e2', 'سيف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e5', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e4', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e6', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e4', 'اسيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000e7', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e4', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000eb', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ea', 'براء', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ec', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ea', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ef', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ee', 'عيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f2', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f1', 'صهيب', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f3', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f1', 'سراج', 1, v_now, v_now);

    -- Generation 8: 83 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000007', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000006', 'خليل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000009', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000006', 'قاسم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000000a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000006', 'عبدالرؤوف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000000c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000b', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000000d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000b', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000000e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000b', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000010', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000f', 'بشار', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000011', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000f', 'بيان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000012', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000f', 'زين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000014', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000013', 'هيثم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000015', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000013', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000017', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000016', 'بلال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000019', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000018', 'قصي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000001a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000018', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000001b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000018', 'لؤي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000001e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001d', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000001f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001d', 'هشام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000020', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001d', 'أمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000023', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000022', 'سعيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000024', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000022', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000025', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000022', 'يزن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000027', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000026', 'وليد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000157', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000026', 'يامن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000158', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000026', 'تميم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000002a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000029', 'رفيق', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000002b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000029', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000002e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002d', 'اسلام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000002f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002d', 'مؤيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000031', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000030', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000036', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000035', 'مصطفى', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000038', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000035', 'منير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000039', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000035', 'سمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000003a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000035', 'بدر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000003d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003c', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000003f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003c', 'ساري', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000040', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003c', 'عبدالله', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000043', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000042', 'عمرو', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000046', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000042', 'حاتم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000047', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000042', 'حسان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000048', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000042', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000004b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004a', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000004f', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004a', 'هاشم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000052', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004a', 'يوسف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000054', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000053', 'هاشم', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000056', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000055', 'أسامة', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000059', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000058', 'أنس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000005a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000058', 'حمزة', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000060', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005f', 'بلال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000061', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005f', 'إلياس', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000065', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000064', 'عبدالمجيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000067', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000066', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000068', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000066', 'نورالدين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000078', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000077', 'وصفي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000007a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000077', 'حسين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000007c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000077', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000007d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000077', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000080', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007f', 'محيالدين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000081', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007f', 'سامي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000083', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'وحيد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000085', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000086', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000087', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000088', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'محمود', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000089', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'عمر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000008a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'أمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000008d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008c', 'عثمان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000092', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000091', 'مهند', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000093', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000091', 'وليد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000095', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000094', 'ريان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000096', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000094', 'هتان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000099', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000098', 'مهند', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000009a', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000098', 'وليد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000009b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000098', 'سراج', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000009c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000098', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000009d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000098', 'أمير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a5', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a4', 'يوسف', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a6', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a4', 'علي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a7', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a4', 'حمزة', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000a9', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a8', 'عدي', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000aa', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a8', 'محمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ac', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ab', 'تيسير', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000af', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ae', 'تحسين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000b1', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b0', 'مالك', 1, v_now, v_now);

    -- Generation 9: 14 member(s)
    INSERT INTO family_members (id, tenant_id, family_tree_id, parent_id, name, version, created_at, updated_at)
    VALUES
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000008', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000007', 'عماد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000037', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000036', 'سامر', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000003e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003d', 'فضل', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000044', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000043', 'حسان', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000045', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000043', 'عزالدين', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000049', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000048', 'هشام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000004c', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004b', 'براء', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000004d', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004b', 'سراج', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000004e', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004b', 'أحمد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000050', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004f', 'عصام', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000051', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004f', 'عبدالرحمن', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000079', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000078', 'جمال', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-00000000007b', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007a', 'خالد', 1, v_now, v_now),
        ('aaaaaaaa-bbbb-4ccc-8ddd-000000000084', v_tenant_id, v_tree_id, 'aaaaaaaa-bbbb-4ccc-8ddd-000000000083', 'عامر', 1, v_now, v_now);

END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-000000000008', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000037', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000044', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000045', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000049', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000050', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000051', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000079', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007b', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000084');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-000000000007', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000009', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000010', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000011', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000012', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000014', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000015', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000017', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000019', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000020', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000023', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000024', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000025', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000027', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000157', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000158', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000031', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000036', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000038', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000039', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000040', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000043', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000046', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000047', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000048', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000052', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000054', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000056', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000059', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005a', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000060', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000061', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000065', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000067', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000068', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000078', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000080', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000081', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000083', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000085', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000086', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000087', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000088', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000089', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000092', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000093', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000095', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000096', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000099', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000009a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000009b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000009c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000009d', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a5', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a6', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a7', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a9', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000aa', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ac', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000af', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b1');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-000000000006', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000000f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000013', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000016', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000018', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000021', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000022', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000026', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000029', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000002d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000030', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000032', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000035', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000003c', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000042', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000004a', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000053', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000055', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000057', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000058', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000062', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000064', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000066', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000069', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000070', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000077', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000007f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000082', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000091', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000094', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000098', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000009e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000009f', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a0', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a1', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a2', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a4', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a8', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ab', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ae', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b0', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b2', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b5', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b7', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b8', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b9', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c0', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c1', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c2', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c4', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c5', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cb', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cd', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ce', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cf', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d1', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d3', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d4', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d5', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d7', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d9', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000dc', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000dd', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000de', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e0', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e1', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e3', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e5', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e6', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e7', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000eb', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ec', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ef', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f2', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f3');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-000000000101', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000102', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000103', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000104', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000105', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000107', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000111', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000112', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000113', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000116', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000120', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000004', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000005', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000001c', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000028', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000034', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000041', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000005b', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000063', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000006d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000076', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008b', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000090', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000097', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000a3', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ad', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b4', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b6', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000be', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bf', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c3', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c6', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c9', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ca', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000cc', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d0', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d2', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d6', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000d8', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000db', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000df', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e2', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e4', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e9', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ea', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ee', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f0', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f1', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f4', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f5');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-000000000100', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000106', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000108', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000010d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000110', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000115', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000117', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000011f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000121', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000122', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000124', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000127', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000128', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000130', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000131', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000132', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000134', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000135', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000137', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000138', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013e', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000141', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000144', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000145', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000147', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000148', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000149', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014b', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014d', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014f', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000150', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000151', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000154', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000155', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000003', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000033', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000075', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000008f', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000b3', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bd', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c8', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000da', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000e8', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ed');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000ff', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000109', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000114', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000118', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000119', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000123', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000126', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000129', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000012e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000133', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000136', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000139', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013d', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000140', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000143', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000146', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000014e', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000152', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000153', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000002', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000071', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000072', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000073', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000074', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bc', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000c7');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000fe', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000125', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000013c', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000142', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000001', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000ba', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000bb');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f8', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f9', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fb', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fc', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fd', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000156', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015a', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015c', 'aaaaaaaa-bbbb-4ccc-8ddd-00000000015b');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-0000000000f6', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000f7', 'aaaaaaaa-bbbb-4ccc-8ddd-0000000000fa', 'aaaaaaaa-bbbb-4ccc-8ddd-000000000159');
DELETE FROM family_members WHERE id IN ('aaaaaaaa-bbbb-4ccc-8ddd-00000000015d');
");
        }
    }
}
