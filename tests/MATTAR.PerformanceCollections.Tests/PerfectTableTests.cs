using FluentAssertions;
using Xunit;

namespace MATTAR.PerformanceCollections.Tests;

public sealed class PerfectTableTests
{
    // ------------------------------------------------------------------
    // Basic construction and Successor
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_BasicSuccessor_Works()
    {
        // universeBits=4, ClusterBits=2: hi values 1, 2, 3 (all non-zero)
        int[] keys = { 4, 8, 12 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Min.Should().Be(4);
        tree.Max.Should().Be(12);
        tree.Successor(4).Should().Be(8);
        tree.Successor(8).Should().Be(12);
        tree.Successor(12).Should().Be(-1);
    }

    [Fact]
    public void CreateStatic_SuccessorBeforeMin_ReturnsMin()
    {
        int[] keys = { 4, 8, 12 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Successor(0).Should().Be(4);
        tree.Successor(3).Should().Be(4);
    }

    [Fact]
    public void CreateStatic_SuccessorAfterMax_ReturnsMinusOne()
    {
        int[] keys = { 4, 8, 12 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Successor(12).Should().Be(-1);
        tree.Successor(15).Should().Be(-1);
    }

    // ------------------------------------------------------------------
    // Edge cases: single element
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_SingleElement_MinMaxEqual()
    {
        using var tree = VanEmdeBoas.CreateStatic([7], universeBits: 4);

        tree.Min.Should().Be(7);
        tree.Max.Should().Be(7);
        tree.Successor(7).Should().Be(-1);
        tree.Successor(0).Should().Be(7);
    }

    // ------------------------------------------------------------------
    // Edge cases: null / empty input
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_NullKeys_ThrowsArgumentNullException()
    {
        var act = () => VanEmdeBoas.CreateStatic(null!, universeBits: 4);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateStatic_EmptyKeys_ThrowsArgumentException()
    {
        var act = () => VanEmdeBoas.CreateStatic([], universeBits: 4);
        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------
    // Insert is forbidden (static / immutable structure)
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_Insert_ThrowsInvalidOperationException()
    {
        int[] keys = { 4, 8, 12 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        var act = () => tree.Insert(5);
        act.Should().Throw<InvalidOperationException>();
    }

    // ------------------------------------------------------------------
    // Enumeration order
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_Enumerate_YieldsElementsInAscendingOrder()
    {
        int[] keys = { 12, 4, 8 }; // unsorted input
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Should().BeInAscendingOrder();
        tree.Should().Equal(4, 8, 12);
    }

    // ------------------------------------------------------------------
    // hi == 0 sentinel: keys whose high part is 0
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_WithHiEqualToZero_Works()
    {
        // universeBits=4, ClusterBits=2 → hi = key>>2
        // keys 1,2,3 all have hi=0 (stored as hi+1=1 in PerfectHashTable)
        int[] keys = { 1, 2, 3 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Min.Should().Be(1);
        tree.Max.Should().Be(3);
        tree.Successor(1).Should().Be(2);
        tree.Successor(2).Should().Be(3);
        tree.Successor(3).Should().Be(-1);
    }

    // ------------------------------------------------------------------
    // key == 0 is a valid element (it becomes the VEB Min, never in a cluster)
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_WithKeyZero_Works()
    {
        // key=0 is stored as Min (not inside any cluster), so no sentinel conflict.
        int[] keys = { 0, 4, 8 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Min.Should().Be(0);
        tree.Max.Should().Be(8);
        tree.Successor(0).Should().Be(4);
        tree.Successor(4).Should().Be(8);
        tree.Successor(8).Should().Be(-1);
    }

    // ------------------------------------------------------------------
    // Duplicate keys are handled silently
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_DuplicateKeys_AreIgnored()
    {
        int[] keys = { 4, 4, 8, 8, 12 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        tree.Should().Equal(4, 8, 12);
    }

    // ------------------------------------------------------------------
    // Larger universe
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_LargeUniverse_Works()
    {
        int[] keys = { 100, 200, 500, 1000, 5000 };
        using var tree = VanEmdeBoas.CreateStatic(keys, universeBits: 16);

        tree.Min.Should().Be(100);
        tree.Max.Should().Be(5000);
        tree.Successor(100).Should().Be(200);
        tree.Successor(200).Should().Be(500);
        tree.Successor(500).Should().Be(1000);
        tree.Successor(1000).Should().Be(5000);
        tree.Successor(5000).Should().Be(-1);
    }

    // ------------------------------------------------------------------
    // Comparison against Cuckoo mode (regression guard)
    // ------------------------------------------------------------------

    [Fact]
    public void CreateStatic_MatchesCuckooMode_OnSameKeys()
    {
        // universeBits=4: hi values 1, 2, 3 (non-zero), same as existing smoke tests
        int[] keys = { 4, 8, 12 };

        using var cuckoo = new VanEmdeBoas(universeBits: 4);
        foreach (var k in keys) cuckoo.Insert(k);

        using var perfect = VanEmdeBoas.CreateStatic(keys, universeBits: 4);

        perfect.Min.Should().Be(cuckoo.Min);
        perfect.Max.Should().Be(cuckoo.Max);
        foreach (var k in keys)
            perfect.Successor(k).Should().Be(cuckoo.Successor(k));
    }

    [Fact]
    public void CreateStatic_MatchesCuckooMode_LargerSet()
    {
        // universeBits=8, ClusterBits=4, Summary.UniverseBits=4, Summary.ClusterBits=2.
        // To avoid the pre-existing CuckooHashTable Key==0 sentinel limitation at every
        // recursive level, choose keys where:
        //   - parent hi = key>>4 >= 1  → key >= 16
        //   - summary hi = (key>>4)>>2 >= 1  → key>>4 >= 4 → key >= 64
        int[] keys = { 64, 80, 96, 160, 240 }; // hi = 4, 5, 6, 10, 15; summary_hi = 1, 1, 1, 2, 3
        using var cuckoo = new VanEmdeBoas(universeBits: 8);
        foreach (var k in keys) cuckoo.Insert(k);

        using var perfect = VanEmdeBoas.CreateStatic(keys, universeBits: 8);

        perfect.Min.Should().Be(cuckoo.Min);
        perfect.Max.Should().Be(cuckoo.Max);
        foreach (var k in keys)
            perfect.Successor(k).Should().Be(cuckoo.Successor(k));
    }
}
