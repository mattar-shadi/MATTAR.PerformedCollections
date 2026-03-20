using System;
using FluentAssertions;
using Xunit;

namespace MATTAR.PerformanceCollections.Tests;

public sealed unsafe class PerfectHashTableTests
{
    [Fact]
    public void Create_EmptyKeys_ReturnsNull()
    {
        var table = PerfectHashTable.Create([], []);
        ((nint)table).Should().Be(0);
    }

    [Fact]
    public void Create_SingleKey_FindReturnsEntry()
    {
        int[] keys = [42];
        int[] values = [99];

        var table = PerfectHashTable.Create(keys, values);
        try
        {
            var entry = PerfectHashTable.Find(table, 42);
            ((nint)entry).Should().NotBe(0);
            entry->Key.Should().Be(42);
            entry->Value.Should().Be(99);
        }
        finally
        {
            PerfectHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Create_MultipleKeys_FindReturnsCorrectEntries()
    {
        int[] keys = [1, 2, 3, 5, 8, 13, 21, 34];
        int[] values = [10, 20, 30, 50, 80, 130, 210, 340];

        var table = PerfectHashTable.Create(keys, values);
        try
        {
            for (int i = 0; i < keys.Length; i++)
            {
                var entry = PerfectHashTable.Find(table, keys[i]);
                ((nint)entry).Should().NotBe(0, $"key {keys[i]} should be found");
                entry->Key.Should().Be(keys[i]);
                entry->Value.Should().Be(values[i]);
            }
        }
        finally
        {
            PerfectHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Create_DuplicateKeys_ThrowsArgumentException()
    {
        int[] keys = [1, 2, 3, 2];
        int[] values = [10, 20, 30, 40];

        Action act = () => PerfectHashTable.Create(keys, values);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Duplicate key 2*");
    }

    [Fact]
    public void Create_DuplicateFirstKey_ThrowsArgumentException()
    {
        int[] keys = [5, 5];
        int[] values = [1, 2];

        Action act = () => PerfectHashTable.Create(keys, values);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Duplicate key 5*");
    }

    [Fact]
    public void Find_MissingKey_ReturnsNull()
    {
        int[] keys = [10, 20, 30];
        int[] values = [1, 2, 3];

        var table = PerfectHashTable.Create(keys, values);
        try
        {
            var entry = PerfectHashTable.Find(table, 99);
            ((nint)entry).Should().Be(0);
        }
        finally
        {
            PerfectHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Find_NullTable_ReturnsNull()
    {
        var entry = PerfectHashTable.Find(null, 1);
        ((nint)entry).Should().Be(0);
    }

    [Fact]
    public void Destroy_NullTable_DoesNotThrow()
    {
        Action act = () => PerfectHashTable.Destroy(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_TableSizeOne_NoShiftBy64()
    {
        // n=1 → tableSize=1 → HashShift would be 64-Log2(1)=64 without the fix.
        // With the fix HashShift=0. Verify the table is functional.
        int[] keys = [7];
        int[] values = [77];

        var table = PerfectHashTable.Create(keys, values);
        try
        {
            table->TableSize.Should().Be(1);
            table->HashShift.Should().Be(0);

            var entry = PerfectHashTable.Find(table, 7);
            ((nint)entry).Should().NotBe(0);
            entry->Value.Should().Be(77);
        }
        finally
        {
            PerfectHashTable.Destroy(table);
        }
    }

    [Fact]
    public void Create_ZeroKey_ThrowsArgumentException()
    {
        int[] keys = [0, 1, 2];
        int[] values = [10, 20, 30];

        Action act = () => PerfectHashTable.Create(keys, values);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Key 0 is reserved*");
    }

    [Fact]
    public void Create_MismatchedArrayLengths_ThrowsArgumentException()
    {
        int[] keys = [1, 2, 3];
        int[] values = [10, 20];

        Action act = () => PerfectHashTable.Create(keys, values);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*values.Length*");
    }

    [Fact]
    public void Create_LargeKeySet_AllKeysFound()
    {
        const int count = 200;
        int[] keys = new int[count];
        int[] values = new int[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = i + 1; // start at 1 to avoid key==0 sentinel
            values[i] = (i + 1) * 10;
        }

        var table = PerfectHashTable.Create(keys, values);
        try
        {
            for (int i = 0; i < count; i++)
            {
                var entry = PerfectHashTable.Find(table, keys[i]);
                ((nint)entry).Should().NotBe(0, $"key {keys[i]} should be found");
                entry->Value.Should().Be(values[i]);
            }
        }
        finally
        {
            PerfectHashTable.Destroy(table);
        }
    }
}
