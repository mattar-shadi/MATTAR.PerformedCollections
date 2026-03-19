using System;
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

        int tableSize = NativeHelpers.NextPowerOfTwo(Math.Max(1, n));

        var table = (PerfectHashTable*)NativeHelpers.AlignedAlloc((nuint)sizeof(PerfectHashTable));
        *table = new PerfectHashTable
        {
            N = n,
            TableSize = tableSize,
            HashA = NativeHelpers.RandomOddULong(),
            HashB = NativeHelpers.RandomULong(),
            HashShift = 64 - NativeHelpers.Log2((uint)tableSize)
        };

        // Record the bucket (Hash1) index for each key.
        var h1s = new int[n];
        int* counts = stackalloc int[tableSize];
        for (int i = 0; i < n; i++)
        {
            h1s[i] = table->Hash1(keys[i]);
            counts[h1s[i]]++;
        }

        // Allocate and zero-initialize buckets so empty ones have SubTable = null.
        table->Buckets = (Bucket*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Bucket) * tableSize));
        NativeHelpers.Clear(table->Buckets, (nuint)(sizeof(Bucket) * tableSize));

        for (int i = 0; i < tableSize; i++)
        {
            if (counts[i] == 0) continue;
            ref Bucket b = ref table->Buckets[i];
            b.Count = counts[i];
            b.SubTableSize = counts[i] <= 3 ? 4 : counts[i] * counts[i];
            b.SubTable = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * b.SubTableSize));
            NativeHelpers.Clear(b.SubTable, (nuint)(sizeof(Entry) * b.SubTableSize));
        }

        // Build each bucket: find a collision-free sub-hash and insert ALL bucket keys
        // atomically.  Inserting one key at a time and retrying only for the current key
        // would silently lose previously inserted keys from the same bucket on each retry.
        for (int bi = 0; bi < tableSize; bi++)
        {
            ref Bucket b = ref table->Buckets[bi];
            if (b.Count == 0) continue;

            for (int attempt = 0; ; attempt++)
            {
                if (attempt > 300)
                    throw new InvalidOperationException(
                        $"Cannot build perfect hash - bucket {bi} after {attempt} tries");

                b.SubHashA = NativeHelpers.RandomOddULong();
                b.SubHashB = NativeHelpers.RandomULong();
                b.SubHashShift = 64 - NativeHelpers.Log2((uint)b.SubTableSize);
                NativeHelpers.Clear(b.SubTable, (nuint)(sizeof(Entry) * b.SubTableSize));

                bool collision = false;
                for (int i = 0; i < n; i++)
                {
                    if (h1s[i] != bi) continue;
                    int h2 = Hash2(b, keys[i]);
                    ref Entry e = ref b.SubTable[h2];
                    if (e.Key != 0) { collision = true; break; }
                    e.Key = keys[i];
                    e.Value = values[i];
                    e.Data = data != null ? data[i] : null;
                }

                if (!collision) break;
            }
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
        for (int i = 0; i < table->TableSize; i++)
            if (table->Buckets[i].SubTable != null)
                NativeHelpers.AlignedFree(table->Buckets[i].SubTable);
        NativeHelpers.AlignedFree(table->Buckets);
        NativeHelpers.AlignedFree(table);
    }
}
