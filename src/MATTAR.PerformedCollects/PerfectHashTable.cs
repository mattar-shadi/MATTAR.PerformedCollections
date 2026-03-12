using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfectHashTable
{
    public int N;
    public int TableSize;
    public Bucket* Buckets;

    public ulong HashA;
    public ulong HashB;
    public int HashShift;

    [StructLayout(LayoutKind.Sequential)]
    public struct Bucket
    {
        public int Count;
        public int SubTableSize;
        public Entry* SubTable;
        public ulong SubHashA;
        public ulong SubHashB;
        public int SubHashShift;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Entry
    {
        public int Key;
        public int Value;
        public void* Data;
    }

    public static PerfectHashTable* Create(int[] keys, int[] values, void** data = null)
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

        int* counts = stackalloc int[table->TableSize];
        for (int i = 0; i < n; i++)
            counts[table->Hash1(keys[i])]++;

        table->Buckets = (Bucket*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Bucket) * table->TableSize));

        for (int i = 0; i < table->TableSize; i++)
        {
            if (counts[i] == 0) continue;
            ref Bucket b = ref table->Buckets[i];
            b.Count = counts[i];
            b.SubTableSize = counts[i] <= 3 ? 4 : counts[i] * counts[i];
            b.SubTable = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * b.SubTableSize));
            NativeHelpers.Clear(b.SubTable, (nuint)(sizeof(Entry) * b.SubTableSize));
        }

        for (int i = 0; i < n; i++)
        {
            int k = keys[i];
            int h1 = table->Hash1(k);
            ref Bucket bucket = ref table->Buckets[h1];

            int attempts = 0;
            while (!TryInsert(ref bucket, k, values[i], data != null ? data[i] : null))
            {
                if (++attempts > 300)
                    throw new InvalidOperationException($"Cannot build perfect hash - bucket {h1} after {attempts} tries");

                bucket.SubHashA = NativeHelpers.RandomOddULong();
                bucket.SubHashB = NativeHelpers.RandomULong();
                bucket.SubHashShift = 64 - NativeHelpers.Log2((uint)bucket.SubTableSize);
                NativeHelpers.Clear(bucket.SubTable, (nuint)(sizeof(Entry) * bucket.SubTableSize));
            }
        }

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Hash1(int key) =>
        (int)(((HashA * (ulong)key + HashB) >> HashShift) & ((ulong)TableSize - 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash2(in Bucket bucket, int key) =>
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
    public static Entry* Find(PerfectHashTable* table, int key)
    {
        if (table == null) return null;
        int h1 = table->Hash1(key);
        ref Bucket b = ref table->Buckets[h1];
        if (b.SubTableSize == 0) return null;

        int h2 = Hash2(b, key);
        Entry* e = &b.SubTable[h2];
        return e->Key == key ? e : null;
    }

    public static void Destroy(PerfectHashTable* table)
    {
        if (table == null) return;
        for (int i = 0; i < table->TableSize; i++)
            if (table->Buckets[i].SubTable != null)
                NativeHelpers.AlignedFree(table->Buckets[i].SubTable);
        NativeHelpers.AlignedFree(table->Buckets);
        NativeHelpers.AlignedFree(table);
    }
}
