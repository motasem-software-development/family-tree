namespace FamilyTree.Import.Tests;

public sealed class ReconstructTests
{
    private static Reconstruction Build() => TestPdf.Reconstruction();

    [Fact]
    public void Every_member_has_a_name()
    {
        // Assert.DoesNotContain instead of Assert.Empty(...Where...): the repo builds with
        // TreatWarningsAsErrors, and xUnit analyzer rule xUnit2029 rejects the latter.
        Assert.DoesNotContain(Build().Members, m => string.IsNullOrWhiteSpace(m.Name));
    }

    [Fact]
    public void Names_contain_only_Arabic_letters_and_spaces()
    {
        // Catches unmapped glyphs and un-normalised presentation forms (U+FB50..U+FEFF) alike.
        Assert.DoesNotContain(Build().Members, m => m.Name.Any(c => c != ' ' && (c < 'ؠ' || c > 'ي')));
    }

    [Fact]
    public void Decodes_known_names()
    {
        var names = Build().Members.Select(m => m.Name).ToHashSet();

        // Each of these exercises a letter that lives in a bfrange block.
        Assert.Contains("محمد", names);   // ح
        Assert.Contains("سليمان", names); // ل
        Assert.Contains("علي", names);    // ع
    }

    [Fact]
    public void Forms_a_single_tree()
    {
        // Assert.Single(collection, predicate) instead of Assert.Single(collection.Where(...)):
        // xUnit analyzer rule xUnit2031, enforced via TreatWarningsAsErrors.
        Assert.Single(Build().Members, m => m.ParentId is null);
    }

    [Fact]
    public void Has_no_orphans_or_cycles()
    {
        var members = Build().Members;
        var byId = members.ToDictionary(m => m.Id);

        foreach (var m in members)
        {
            var seen = new HashSet<int>();
            var cur = m;
            while (cur.ParentId is { } p)
            {
                Assert.True(byId.ContainsKey(p), $"{cur.Name} points at a missing parent.");
                Assert.True(seen.Add(cur.Id), $"{m.Name} sits on a cycle.");
                cur = byId[p];
            }
        }
    }

    [Fact]
    public void Names_fit_the_domain_limit()
    {
        Assert.DoesNotContain(Build().Members, m => m.Name.Length > 200);
    }
}
