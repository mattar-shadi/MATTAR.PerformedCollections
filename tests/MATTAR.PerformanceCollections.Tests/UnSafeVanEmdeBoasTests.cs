using FluentAssertions;
using Xunit;

namespace MATTAR.PerformanceCollections.Tests;

/// <summary>
/// Unit tests for the internal <see cref="UnSafeVanEmdeBoas"/> unsafe struct.
///
/// Key constraints enforced by the implementation:
/// <list type="bullet">
///   <item>Dynamic (Cuckoo) mode requires keys whose high part at every recursion
///         depth is non-zero.  For universeBits=4 (ClusterBits=2) that means
///         key &gt;= 4 (so hi = key&gt;&gt;2 &gt;= 1).  Smaller positive keys cause
///         the recursive CuckooHashTable to misinterpret them as empty slots.</item>
///   <item>Static (PerfectTable) mode has no such restriction; even key 0 works
///         because it is stored as the VEB Min and never placed inside a cluster.</item>
/// </list>
/// </summary>
public sealed unsafe class UnSafeVanEmdeBoasTests
{
    // ------------------------------------------------------------------
    // Create / initial state (dynamic / Cuckoo mode)
    // ------------------------------------------------------------------

    [Fact]
    public void Create_DynamicMode_InitialStateIsEmpty()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            v->Min.Should().Be(-1);
            v->Max.Should().Be(-1);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Create_UniverseBitsExceedMax_ThrowsArgumentException()
    {
        Action act = () => { UnSafeVanEmdeBoas.Create(universeBits: UnSafeVanEmdeBoas.MAX_UNIVERSE_BITS + 1); };
        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------
    // Destroy
    // ------------------------------------------------------------------

    [Fact]
    public void Destroy_NullPointer_DoesNotThrow()
    {
        var act = () => UnSafeVanEmdeBoas.Destroy(null);
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // Insert – dynamic mode
    // ------------------------------------------------------------------

    [Fact]
    public void Insert_SingleElement_MinAndMaxEqualElement()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            UnSafeVanEmdeBoas.Insert(v, 64);

            v->Min.Should().Be(64);
            v->Max.Should().Be(64);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Insert_TwoElements_MinAndMaxCorrect()
    {
        // universeBits=8, ClusterBits=4 → hi = key>>4
        // keys 64 (hi=4) and 80 (hi=5): hi >= 1 at every level.
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            UnSafeVanEmdeBoas.Insert(v, 64);
            UnSafeVanEmdeBoas.Insert(v, 80);

            v->Min.Should().Be(64);
            v->Max.Should().Be(80);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Insert_OutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 4);
        try
        {
            // Universe [0, 16): key 16 is out of range.
            var act = () => UnSafeVanEmdeBoas.Insert(v, 16);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Insert_NegativeKey_ThrowsArgumentOutOfRangeException()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            var act = () => UnSafeVanEmdeBoas.Insert(v, -1);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    // ------------------------------------------------------------------
    // Successor – dynamic mode
    // ------------------------------------------------------------------

    [Fact]
    public void Successor_OnNullPointer_ReturnsMinusOne()
    {
        int s = UnSafeVanEmdeBoas.Successor(null, 5);
        s.Should().Be(-1);
    }

    [Fact]
    public void Successor_EmptyTree_ReturnsMinusOne()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            UnSafeVanEmdeBoas.Successor(v, 0).Should().Be(-1);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Successor_MultipleElements_ReturnsCorrectNext()
    {
        // universeBits=8: keys 64, 80, 96 → hi = 4, 5, 6 (non-zero at every level).
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            UnSafeVanEmdeBoas.Insert(v, 64);
            UnSafeVanEmdeBoas.Insert(v, 80);
            UnSafeVanEmdeBoas.Insert(v, 96);

            UnSafeVanEmdeBoas.Successor(v, 64).Should().Be(80);
            UnSafeVanEmdeBoas.Successor(v, 80).Should().Be(96);
            UnSafeVanEmdeBoas.Successor(v, 96).Should().Be(-1);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Successor_QueryBeforeMin_ReturnsMin()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            UnSafeVanEmdeBoas.Insert(v, 64);
            UnSafeVanEmdeBoas.Insert(v, 80);

            // Any x < min should return min.
            UnSafeVanEmdeBoas.Successor(v, 0).Should().Be(64);
            UnSafeVanEmdeBoas.Successor(v, 63).Should().Be(64);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Successor_QueryOnMax_ReturnsMinusOne()
    {
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            UnSafeVanEmdeBoas.Insert(v, 64);
            UnSafeVanEmdeBoas.Insert(v, 96);

            UnSafeVanEmdeBoas.Successor(v, 96).Should().Be(-1);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    // ------------------------------------------------------------------
    // Static (PerfectTable) mode – basic usage
    // ------------------------------------------------------------------

    [Fact]
    public void Create_StaticMode_RequiresPresetKeys()
    {
        // PerfectTable mode without preset keys must throw.
        Action act = () => { UnSafeVanEmdeBoas.Create(universeBits: 8, useCuckoo: false, presetKeys: null); };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_StaticMode_EmptyPresetKeys_ThrowsArgumentException()
    {
        Action act = () => { UnSafeVanEmdeBoas.Create(universeBits: 8, useCuckoo: false, presetKeys: []); };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_StaticMode_BasicSuccessor_Works()
    {
        // universeBits=4: keys 4, 8, 12 → safe for both Cuckoo and PerfectTable modes.
        int[] keys = [4, 8, 12];
        var v = UnSafeVanEmdeBoas.Create(universeBits: 4, useCuckoo: false, presetKeys: keys);
        try
        {
            v->Min.Should().Be(4);
            v->Max.Should().Be(12);

            UnSafeVanEmdeBoas.Successor(v, 4).Should().Be(8);
            UnSafeVanEmdeBoas.Successor(v, 8).Should().Be(12);
            UnSafeVanEmdeBoas.Successor(v, 12).Should().Be(-1);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Create_StaticMode_KeyZero_IsStoredAsMin()
    {
        // Key 0 is stored as the VEB Min (never placed in a cluster), so there
        // is no sentinel conflict in PerfectTable mode.
        int[] keys = [0, 4, 8];
        var v = UnSafeVanEmdeBoas.Create(universeBits: 4, useCuckoo: false, presetKeys: keys);
        try
        {
            v->Min.Should().Be(0);
            v->Max.Should().Be(8);
            UnSafeVanEmdeBoas.Successor(v, 0).Should().Be(4);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    [Fact]
    public void Insert_IntoStaticMode_ThrowsInvalidOperationException()
    {
        int[] keys = [4, 8, 12];
        var v = UnSafeVanEmdeBoas.Create(universeBits: 4, useCuckoo: false, presetKeys: keys);
        try
        {
            var act = () => UnSafeVanEmdeBoas.Insert(v, 5);
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }

    // ------------------------------------------------------------------
    // Larger universe – smoke test
    // ------------------------------------------------------------------

    [Fact]
    public void DynamicMode_LargerUniverse_SuccessorConsistent()
    {
        // universeBits=8, ClusterBits=4 → hi = key>>4; Summary.ClusterBits=2 → summary_hi = hi>>2.
        // Keys [64, 80, 128, 160] produce hi values {4, 5, 8, 10} → summary_hi {1, 1, 2, 2}.
        // Only 2 distinct summary_hi values ensures the universeBits=2 base-case (which tracks
        // only min+max) is not overloaded, and all Successor results are exact.
        int[] keys = [64, 80, 128, 160];
        var v = UnSafeVanEmdeBoas.Create(universeBits: 8);
        try
        {
            foreach (var k in keys)
                UnSafeVanEmdeBoas.Insert(v, k);

            v->Min.Should().Be(64);
            v->Max.Should().Be(160);

            UnSafeVanEmdeBoas.Successor(v, 64).Should().Be(80);
            UnSafeVanEmdeBoas.Successor(v, 80).Should().Be(128);
            UnSafeVanEmdeBoas.Successor(v, 128).Should().Be(160);
            UnSafeVanEmdeBoas.Successor(v, 160).Should().Be(-1);
        }
        finally
        {
            UnSafeVanEmdeBoas.Destroy(v);
        }
    }
}
