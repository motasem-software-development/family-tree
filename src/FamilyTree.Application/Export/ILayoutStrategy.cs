using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

public interface ILayoutStrategy
{
    string Name { get; }

    TreeScene Build(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure);
}
