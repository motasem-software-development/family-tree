using FamilyTree.Application.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class BranchPaletteTests
{
    [Fact]
    public void The_first_four_hues_are_the_measured_reference_colours_in_order()
    {
        BranchPalette.Default.ColorAt(0).Should().Be("#518CD8");
        BranchPalette.Default.ColorAt(1).Should().Be("#FD6D5A");
        BranchPalette.Default.ColorAt(2).Should().Be("#6DC354");
        BranchPalette.Default.ColorAt(3).Should().Be("#FEB40B");
    }

    [Fact]
    public void The_palette_cycles_beyond_its_length()
    {
        BranchPalette.Default.ColorAt(12).Should().Be(BranchPalette.Default.ColorAt(0));
        BranchPalette.Default.ColorAt(13).Should().Be(BranchPalette.Default.ColorAt(1));
    }

    [Fact]
    public void Every_hue_is_distinct()
    {
        BranchPalette.Default.Colors.Should().OnlyHaveUniqueItems();
        BranchPalette.Default.Colors.Should().HaveCount(12);
    }

    // Spec §3.1: these documents get printed on mono printers, so neighbouring branches must
    // stay separable without colour.
    [Fact]
    public void Adjacent_hues_stay_separable_in_greyscale()
    {
        var luminances = BranchPalette.Default.Colors.Select(RelativeLuminance).ToList();

        for (var i = 0; i < luminances.Count - 1; i++)
            Math.Abs(luminances[i + 1] - luminances[i]).Should()
                .BeGreaterThanOrEqualTo(0.03, "hues {0} and {1} must differ in print", i, i + 1);
    }

    private static double RelativeLuminance(string hex)
    {
        double Channel(int offset)
        {
            var c = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
