using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct CuckooHashTable
{
    public int Size;
    public int Count;
    public int TombstoneCount;

    public Entry* Table1;
    public Entry* Table2;

    public ulong Hash1A, Hash1B;
    public ulong Hash2A, Hash2B;
    public int HashShift;

    public const double MAX_LOAD_FACTOR = 0.45;
    public const double MAX_TOMBSTONE_RATIO = 0.25;
    public const int MAX_KICKOUT = 64;

    [StructLayout(LayoutKind.Sequential)]
    public struct Entry
    {
        public int Key;
        public int Value;
        public void* Data;
        public bool IsTombstone;
    }

    public static CuckooHashTable* Create(int capacity)
    {
        int size = NativeHelpers.NextPowerOfTwo((int)(capacity / MAX_LOAD_FACTOR + 1));
        if (size < 4) size = 4;

        var table = (CuckooHashTable*)NativeHelpers.AlignedAlloc((nuint)sizeof(CuckooHashTable));
        *table = new CuckooHashTable
        {
            Size = size,
            Count = 0,
            TombstoneCount = 0,
            Table1 = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * size)),
            Table2 = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * size)),
            Hash1A = NativeHelpers.RandomOddULong(),
            Hash1B = NativeHelpers.RandomULong(),
            Hash2A = NativeHelpers.RandomOddULong(),
            Hash2B = NativeHelpers.RandomULong(),
            HashShift = 64 - NativeHelpers.Log2((uint)size)
        };

        NativeHelpers.Clear(table->Table1, (nuint)(sizeof(Entry) * size));
        NativeHelpers.Clear(table->Table2, (nuint)(sizeof(Entry) * size));

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(CuckooHashTable* t, ulong a, ulong b, int key) =>
        (int)(((a * (ulong)key + b) >> t->HashShift) & ((ulong)t->Size - 1));

    public static bool Insert(CuckooHashTable* table, int key, int value, void* data = null)
    {
        if ((double)(table->Count + 1) / (table->Size * 2) > MAX_LOAD_FACTOR)
        {
            GrowAndRehash(table);
        }

        if ((double)table->TombstoneCount / (table->Size * 2) > MAX_TOMBSTONE_RATIO)
        {
            CleanRehash(table);
        }

        int newKey = key, newValue = value;
        void* newData = data;
        bool useTable1 = true;

        for (int kicks = 0; kicks < MAX_KICKOUT; kicks++)
        {
            int idx;
            Entry* entry;

            if (useTable1)
            {
                idx = Hash(table, table->Hash1A, table->Hash1B, newKey);
                entry = &table->Table1[idx];
            }
            else
            {
                idx = Hash(table, table->Hash2A, table->Hash2B, newKey);
                entry = &table->Table2[idx];
            }

            if (entry->Key == 0 || entry->IsTombstone)
            {
                if (entry->IsTombstone) table->TombstoneCount--;
                entry->Key = newKey;
                entry->Value = newValue;
                entry->Data = newData;
                entry->IsTombstone = false;
                table->Count++;
                return true;
            }

            if (entry->Key == newKey)
            {
                entry->Value = newValue;
                entry->Data = newData;
                return true;
            }

            (newKey, entry->Key) = (entry->Key, newKey);
            (newValue, entry->Value) = (entry->Value, newValue);
            void* tempData = entry->Data;
            entry->Data = newData;
            newData = tempData;

            useTable1 = !useTable1;
        }

        // Échec après MAX_KICKOUT → on grossit la table plutôt que de seulement changer les hash
        GrowAndRehash(table);
        return Insert(table, newKey, newValue, newData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entry* Find(CuckooHashTable* table, int key)
    {
        if (table == null) return null;

        int idx1 = Hash(table, table->Hash1A, table->Hash1B, key);
        if (table->Table1[idx1].Key == key && !table->Table1[idx1].IsTombstone)
            return &table->Table1[idx1];

        int idx2 = Hash(table, table->Hash2A, table->Hash2B, key);
        if (table->Table2[idx2].Key == key && !table->Table2[idx2].IsTombstone)
            return &table->Table2[idx2];

        return null;
    }

    public static bool Delete(CuckooHashTable* table, int key)
    {
        int idx1 = Hash(table, table->Hash1A, table->Hash1B, key);
        if (table->Table1[idx1].Key == key && !table->Table1[idx1].IsTombstone)
        {
            table->Table1[idx1].IsTombstone = true;
            table->Count--;
            table->TombstoneCount++;
            return true;
        }

        int idx2 = Hash(table, table->Hash2A, table->Hash2B, key);
        if (table->Table2[idx2].Key == key && !table->Table2[idx2].IsTombstone)
        {
            table->Table2[idx2].IsTombstone = true;
            table->Count--;
            table->TombstoneCount++;
            return true;
        }

        return false;
    }

    private static void GrowAndRehash(CuckooHashTable* table)
    {
        var oldT1 = table->Table1;
        var oldT2 = table->Table2;
        int oldSize = table->Size;

        table->Size *= 2;
        table->Table1 = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * table->Size));
        table->Table2 = (Entry*)NativeHelpers.AlignedAlloc((nuint)(sizeof(Entry) * table->Size));
        NativeHelpers.Clear(table->Table1, (nuint)(sizeof(Entry) * table->Size));
        NativeHelpers.Clear(table->Table2, (nuint)(sizeof(Entry) * table->Size));

        table->Hash1A = NativeHelpers.RandomOddULong();
        table->Hash1B = NativeHelpers.RandomULong();
        table->Hash2A = NativeHelpers.RandomOddULong();
        table->Hash2B = NativeHelpers.RandomULong();
        table->HashShift = 64 - NativeHelpers.Log2((uint)table->Size);

        table->Count = 0;
        table->TombstoneCount = 0;

        for (int i = 0; i < oldSize; i++)
        {
            if (oldT1[i].Key != 0 && !oldT1[i].IsTombstone)
                Insert(table, oldT1[i].Key, oldT1[i].Value, oldT1[i].Data);
            if (oldT2[i].Key != 0 && !oldT2[i].IsTombstone)
                Insert(table, oldT2[i].Key, oldT2[i].Value, oldT2[i].Data);
        }

        NativeHelpers.AlignedFree(oldT1);
        NativeHelpers.AlignedFree(oldT2);
    }

    private static void CleanRehash(CuckooHashTable* table) => GrowAndRehash(table);

    public static void Destroy(CuckooHashTable* table)
    {
        if (table == null) return;
        NativeHelpers.AlignedFree(table->Table1);
        NativeHelpers.AlignedFree(table->Table2);
        NativeHelpers.AlignedFree(table);
    }
}
