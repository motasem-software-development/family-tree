namespace FamilyTree.Import.Tests;

public sealed class EmitTests
{
    [Fact]
    public void Indented_tree_has_one_line_per_member()
    {
        var r = TestPdf.Reconstruction();
        var lines = Emit.IndentedTree(r).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(r.Members.Count, lines.Length);
    }

    [Fact]
    public void Indentation_equals_generation_depth()
    {
        var lines = Emit.IndentedTree(TestPdf.Reconstruction())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // xUnit2013 wants Assert.Empty for a bare Count()==0 check, but this is Count() of a
        // char sequence from TakeWhile (an indent-width measurement), not a collection-size
        // assertion -- suppressed rather than reworded away from the brief's exact test text.
#pragma warning disable xUnit2013
        Assert.Equal(0, lines[0].TakeWhile(char.IsWhiteSpace).Count()); // the root
#pragma warning restore xUnit2013
        Assert.All(lines, l => Assert.Equal(0, l.TakeWhile(char.IsWhiteSpace).Count() % 2));
    }
}
