using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PerfectHashTable
{
    internal int N;
    internal int TableSize;
    internal Bucket* Buckets;

    internal ulong HashA;
    internal ulong HashB;
    internal int HashShift;

    private const int MaxAttemptsBeforeGrowth = 100;
    private const int MaxSubTableSize = 1 << 20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Bucket
    {
        internal int Count;
        internal int SubTableSize;
        internal Entry* SubTable;
        internal ulong SubHashA;
        internal ulong SubHashB;
        internal int SubHashShift;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Entry
    {
        internal int Key;
        internal int Value;
        internal void* Data;
    }

    internal static PerfectHashTable* Create(int[] keys, int[] values, void** data = null)
    {
        int n = keys.Length;
        if (n == 0) return null;

        // Validate that values array length matches keys array length.
        if (values.Length != n)
            throw new ArgumentException($"values.Length ({values.Length}) must equal keys.Length ({n}).");

        // Detect duplicate keys early and reject the reserved zero sentinel.
        var seen = new HashSet<int>(n);
        for (int i = 0; i < n; i++)
        {
            if (keys[i] == 0)
                throw new ArgumentException("Key 0 is reserved as an empty-slot sentinel and cannot be stored.");
            if (!seen.Add(keys[i]))
                throw new ArgumentException($"Duplicate key {keys[i]}");
        }

        int tableSize = NativeHelpers.NextPowerOfTwo(Math.Max(1, n));

        var table = (PerfectHashTable*)NativeHelpers.AlignedAlloc((nuint)sizeof(PerfectHashTable));
        *table = new PerfectHashTable
        {
            N = n,
            TableSize = tableSize,
            HashA = NativeHelpers.RandomOddULong(),
            HashB = NativeHelpers.RandomULong(),
            HashShift = tableSize == 1 ? 0 : 64 - NativeHelpers.Log2((uint)tableSize)
        };

        try
        {
            // Bucket array must be zero-initialised so that empty buckets have null SubTable.
            table->Buckets = (Bucket*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Bucket) * tableSize));
            NativeHelpers.Clear(table->Buckets, (nuint)(sizeof(Bucket) * tableSize));

            // Assign each key to a first-level bucket and count occupancy.
            int[] bucketOf = new int[n];
            int[] counts = new int[tableSize];
            for (int i = 0; i < n; i++)
            {
                int h1 = table->Hash1(keys[i]);
                bucketOf[i] = h1;
                counts[h1]++;
            }

            // Allocate sub-tables for non-empty buckets.
            // Use long arithmetic to avoid int overflow when count is large.
            for (int b = 0; b < tableSize; b++)
            {
                if (counts[b] == 0) continue;
                int count = counts[b];
                long subSizeLong = count <= 3 ? 4L : (long)count * count;
                if (subSizeLong > MaxSubTableSize)
                    throw new InvalidOperationException(
                        $"Bucket {b} has {count} colliding keys; initial sub-table size {subSizeLong} exceeds MaxSubTableSize ({MaxSubTableSize}).");
                int subSize = (int)subSizeLong;
                ref Bucket bucket = ref table->Buckets[b];
                bucket.Count = count;
                bucket.SubTableSize = subSize;
                bucket.SubTable = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * subSize));
                NativeHelpers.Clear(bucket.SubTable, (nuint)(sizeof(Entry) * subSize));
            }

            // Group key indices by bucket so each bucket can be built atomically.
            int[][] bucketIndices = new int[tableSize][];
            for (int b = 0; b < tableSize; b++)
                if (counts[b] > 0)
                    bucketIndices[b] = new int[counts[b]];

            int[] fillPos = new int[tableSize];
            for (int i = 0; i < n; i++)
                bucketIndices[bucketOf[i]][fillPos[bucketOf[i]]++] = i;

            // Build each bucket: find hash params that place all bucket keys without collision.
            for (int b = 0; b < tableSize; b++)
            {
                if (counts[b] == 0) continue;
                ref Bucket bucket = ref table->Buckets[b];
                int[] indices = bucketIndices[b];

                bucket.SubHashA = NativeHelpers.RandomOddULong();
                bucket.SubHashB = NativeHelpers.RandomULong();
                bucket.SubHashShift = bucket.SubTableSize == 1 ? 0 : 64 - NativeHelpers.Log2((uint)bucket.SubTableSize);

                int attempts = 0;
                while (true)
                {
                    NativeHelpers.Clear(bucket.SubTable, (nuint)(sizeof(Entry) * bucket.SubTableSize));

                    bool ok = true;
                    for (int j = 0; j < indices.Length; j++)
                    {
                        int idx = indices[j];
                        if (!TryInsert(ref bucket, keys[idx], values[idx], data != null ? data[idx] : null))
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok) break;

                    if (++attempts > MaxAttemptsBeforeGrowth)
                    {
                        // Fallback: grow the sub-table to reduce collision probability.
                        long newSizeLong = (long)bucket.SubTableSize * 2;
                        if (newSizeLong > MaxSubTableSize)
                            throw new InvalidOperationException($"Cannot build perfect hash - bucket {b} exceeds maximum sub-table size");
                        int newSize = NativeHelpers.NextPowerOfTwo((int)newSizeLong);
                        NativeHelpers.AlignedFree(bucket.SubTable);
                        bucket.SubTableSize = newSize;
                        bucket.SubTable = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * newSize));
                        NativeHelpers.Clear(bucket.SubTable, (nuint)(sizeof(Entry) * newSize));
                        attempts = 0;
                    }

                    bucket.SubHashA = NativeHelpers.RandomOddULong();
                    bucket.SubHashB = NativeHelpers.RandomULong();
                    bucket.SubHashShift = bucket.SubTableSize == 1 ? 0 : 64 - NativeHelpers.Log2((uint)bucket.SubTableSize);
                }
            }
        }
        catch
        {
            // Free any already-allocated native memory to avoid leaks on construction failure.
            Destroy(table);
            throw;
        }

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Hash1(int key) =>
        (int)(((HashA * (ulong)key + HashB) >> HashShift) & ((ulong)TableSize - 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Hash2(in Bucket bucket, int key) =>
        bucket.SubTableSize == 0 ? -1 :
        (int)(((bucket.SubHashA * (ulong)key + bucket.SubHashB) >> bucket.SubHashShift) & ((ulong)bucket.SubTableSize - 1));

    private static bool TryInsert(ref Bucket bucket, int key, int value, void* data)
    {
        if (bucket.SubTableSize == 0) return false;
        int h = Hash2(bucket, key);
        ref Entry e = ref bucket.SubTable[h];

        if (e.Key != 0 && e.Key != key)
            return false;

        e.Key = key;
        e.Value = value;
        e.Data = data;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Entry* Find(PerfectHashTable* table, int key)
    {
        if (table == null) return null;
        int h1 = table->Hash1(key);
        ref Bucket b = ref table->Buckets[h1];
        if (b.SubTableSize == 0) return null;

        int h2 = Hash2(b, key);
        Entry* e = &b.SubTable[h2];
        return e->Key == key ? e : null;
    }

    internal static void Destroy(PerfectHashTable* table)
    {
        if (table == null) return;
        if (table->Buckets != null)
        {
            for (int i = 0; i < table->TableSize; i++)
                if (table->Buckets[i].SubTable != null)
                    NativeHelpers.AlignedFree(table->Buckets[i].SubTable);
            NativeHelpers.AlignedFree(table->Buckets);
        }
        NativeHelpers.AlignedFree(table);
    }
}
