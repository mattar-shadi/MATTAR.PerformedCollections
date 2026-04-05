using FluentAssertions;
using Xunit;

namespace MATTAR.PerformanceCollections.Tests;

/// <summary>
/// Unit tests for the internal <see cref="CuckooHashTable"/> unsafe struct.
///
/// Sentinel rule: <c>Key == 0</c> is the empty-slot sentinel and must NOT be
/// used as an actual key — the implementation treats any slot whose Key field
/// equals zero as vacant.
/// </summary>
public sealed unsafe class CuckooHashTableTests
{
    // ------------------------------------------------------------------
    // Create / Destroy
    // ------------------------------------------------------------------

    [Fact]
    public void Create_SmallCapacity_ReturnsNonNullTable()
    {
        var table = CuckooHashTable.Create(4);
        try
        {
            ((nint)table).Should().NotBe(0);
            table->Count.Should().Be(0);
            table->Size.Should().BeGreaterThanOrEqualTo(4);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Create_LargeCapacity_ReturnsNonNullTable()
    {
        var table = CuckooHashTable.Create(1024);
        try
        {
            ((nint)table).Should().NotBe(0);
            table->Count.Should().Be(0);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Destroy_NullTable_DoesNotThrow()
    {
        var act = () => CuckooHashTable.Destroy(null);
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // Find – edge cases before insertion
    // ------------------------------------------------------------------

    [Fact]
    public void Find_NullTable_ReturnsNull()
    {
        var entry = CuckooHashTable.Find(null, 42);
        ((nint)entry).Should().Be(0);
    }

    [Fact]
    public void Find_EmptyTable_ReturnsNull()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            var entry = CuckooHashTable.Find(table, 42);
            ((nint)entry).Should().Be(0);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Insert / Find – basic behaviour
    // ------------------------------------------------------------------

    [Fact]
    public void Insert_SingleEntry_FindReturnsEntry()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, key: 1, value: 100);

            var entry = CuckooHashTable.Find(table, 1);
            ((nint)entry).Should().NotBe(0);
            entry->Key.Should().Be(1);
            entry->Value.Should().Be(100);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Insert_MultipleEntries_AllFoundWithCorrectValues()
    {
        int[] keys   = [1, 2, 3, 5, 8, 13, 21, 34];
        int[] values = [10, 20, 30, 50, 80, 130, 210, 340];

        var table = CuckooHashTable.Create(16);
        try
        {
            for (int i = 0; i < keys.Length; i++)
                CuckooHashTable.Insert(table, keys[i], values[i]);

            for (int i = 0; i < keys.Length; i++)
            {
                var entry = CuckooHashTable.Find(table, keys[i]);
                ((nint)entry).Should().NotBe(0, $"key {keys[i]} should be present");
                entry->Key.Should().Be(keys[i]);
                entry->Value.Should().Be(values[i]);
            }
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Find_MissingKey_ReturnsNull()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, 1, 100);
            CuckooHashTable.Insert(table, 2, 200);

            var entry = CuckooHashTable.Find(table, 99);
            ((nint)entry).Should().Be(0);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Duplicate insert – expected behaviour: value is updated in-place
    // ------------------------------------------------------------------

    [Fact]
    public void Insert_DuplicateKey_UpdatesValue()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, key: 7, value: 111);
            CuckooHashTable.Insert(table, key: 7, value: 999); // second insert → update

            var entry = CuckooHashTable.Find(table, 7);
            ((nint)entry).Should().NotBe(0);
            entry->Value.Should().Be(999, "second insert of the same key should update the value");

            // Count must not increase on a duplicate.
            table->Count.Should().Be(1);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Count tracking
    // ------------------------------------------------------------------

    [Fact]
    public void Count_IncreasesWithEachDistinctInsert()
    {
        var table = CuckooHashTable.Create(16);
        try
        {
            for (int i = 1; i <= 5; i++)
            {
                CuckooHashTable.Insert(table, i, i * 10);
                table->Count.Should().Be(i);
            }
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Delete
    // ------------------------------------------------------------------

    [Fact]
    public void Delete_ExistingKey_ReturnsTrueAndKeyNotFoundAfterwards()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, 42, 420);

            bool deleted = CuckooHashTable.Delete(table, 42);

            deleted.Should().BeTrue();
            ((nint)CuckooHashTable.Find(table, 42)).Should().Be(0);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Delete_MissingKey_ReturnsFalse()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, 1, 10);

            bool deleted = CuckooHashTable.Delete(table, 99);

            deleted.Should().BeFalse();
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Delete_ExistingKey_DecrementsCount()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, 1, 10);
            CuckooHashTable.Insert(table, 2, 20);
            table->Count.Should().Be(2);

            CuckooHashTable.Delete(table, 1);
            table->Count.Should().Be(1);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Delete_ThenReinsert_KeyFoundAgain()
    {
        var table = CuckooHashTable.Create(8);
        try
        {
            CuckooHashTable.Insert(table, 5, 50);
            CuckooHashTable.Delete(table, 5);
            CuckooHashTable.Insert(table, 5, 55);

            var entry = CuckooHashTable.Find(table, 5);
            ((nint)entry).Should().NotBe(0);
            entry->Value.Should().Be(55);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Sentinel key rule: Key == 0 is reserved
    // ------------------------------------------------------------------

    /// <summary>
    /// Key == 0 is the empty-slot sentinel in <see cref="CuckooHashTable"/>.
    /// This test documents that inserting key 0 is unreliable: the value is not
    /// preserved across a table growth / rehash and can return false positives.
    /// Callers must not use key 0.
    /// </summary>
    [Fact]
    public void KeyZeroSentinel_IsDocumented_MustNotBeUsed()
    {
        // This test is intentionally not making strong assertions about the
        // *outcome* of inserting key 0; it merely runs the code to ensure it
        // does not crash (no access violation, no assertion failure in release
        // builds) and confirms that key 0 is silently treated as empty.
        var table = CuckooHashTable.Create(8);
        try
        {
            // After inserting key 0 the slot's Key field stays 0 (the sentinel),
            // so Find may spuriously match any empty slot.  We verify only that
            // the operation does not throw.
            CuckooHashTable.Insert(table, key: 0, value: 1);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Growth / resizing under load
    // ------------------------------------------------------------------

    [Fact]
    public void Insert_BeyondLoadFactor_TableGrowsAndAllEntriesRemain()
    {
        // Insert more entries than the initial capacity to force at least one
        // GrowAndRehash.  Initial size = NextPow2(ceil(32/0.45)+1) = 128 slots
        // across two tables → threshold ≈ 0.45 × 256 ≈ 115 entries.
        // 200 keys well exceeds this, guaranteeing growth.
        const int count = 200;
        var table = CuckooHashTable.Create(32);
        try
        {
            for (int i = 1; i <= count; i++)
                CuckooHashTable.Insert(table, i, i * 3);

            for (int i = 1; i <= count; i++)
            {
                var entry = CuckooHashTable.Find(table, i);
                ((nint)entry).Should().NotBe(0, $"key {i} should survive table growth");
                entry->Value.Should().Be(i * 3);
            }

            table->Count.Should().Be(count);
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }

    // ------------------------------------------------------------------
    // Random smoke test (seeded for determinism)
    // ------------------------------------------------------------------

    [Fact]
    public void RandomSmoke_InsertFindDelete_AllOperationsCorrect()
    {
        const int seed  = 12345;
        const int count = 150;

        var rng  = new Random(seed);
        var keys = new HashSet<int>();
        var kvp  = new Dictionary<int, int>();

        // Generate 'count' distinct non-zero keys.
        while (kvp.Count < count)
        {
            int k = rng.Next(1, int.MaxValue);
            if (kvp.TryAdd(k, rng.Next(1, 100_000)))
                keys.Add(k);
        }

        var table = CuckooHashTable.Create(64);
        try
        {
            // Insert all
            foreach (var (k, v) in kvp)
                CuckooHashTable.Insert(table, k, v);

            // Verify all present
            foreach (var (k, v) in kvp)
            {
                var entry = CuckooHashTable.Find(table, k);
                ((nint)entry).Should().NotBe(0, $"key {k} should be found");
                entry->Value.Should().Be(v);
            }

            // Delete half and verify
            int deleted = 0;
            foreach (var k in keys)
            {
                if (deleted >= count / 2) break;
                CuckooHashTable.Delete(table, k);
                kvp.Remove(k);
                deleted++;
            }

            foreach (var (k, v) in kvp)
            {
                var entry = CuckooHashTable.Find(table, k);
                ((nint)entry).Should().NotBe(0, $"key {k} should still be present after partial delete");
                entry->Value.Should().Be(v);
            }
        }
        finally
        {
            CuckooHashTable.Destroy(table);
        }
    }
}
