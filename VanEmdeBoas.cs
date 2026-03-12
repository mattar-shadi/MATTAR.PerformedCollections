using System;
using System.Runtime.CompilerServices;

public unsafe struct VanEmdeBoas
{
    public int UniverseBits;
    public int ClusterBits;
    public int Min;
    public int Max;

    public bool UseCuckoo;
    public CuckooHashTable* CuckooTable;
    public PerfectHashTable* PerfectTable;

    public VanEmdeBoas* Summary;

    public const int MIN_BITS = 2;
    public const int MAX_UNIVERSE_BITS = 30;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int High(VanEmdeBoas* v, int x) => x >> v->ClusterBits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Low(VanEmdeBoas* v, int x) => x & ((1 << v->ClusterBits) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(VanEmdeBoas* v, int high, int low)
    {
        int result = (high << v->ClusterBits) | low;
        if (result >= (1 << v->UniverseBits) || result < 0)
            throw new OverflowException("Index calculation overflow");
        return result;
    }

    public static VanEmdeBoas* Create(int universeBits, bool useCuckoo = true, int[]? presetKeys = null)
    {
        if (universeBits > MAX_UNIVERSE_BITS)
            throw new ArgumentException($"Universe too large (max 2^{MAX_UNIVERSE_BITS})", nameof(universeBits));

        if (universeBits < MIN_BITS) universeBits = MIN_BITS;

        var v = (VanEmdeBoas*)NativeHelpers.AlignedAlloc((nuint)sizeof(VanEmdeBoas));
        *v = new VanEmdeBoas
        {
            UniverseBits = universeBits,
            ClusterBits = universeBits >> 1,
            Min = -1,
            Max = -1,
            UseCuckoo = useCuckoo
        };

        if (universeBits > MIN_BITS)
        {
            if (useCuckoo)
            {
                v->CuckooTable = CuckooHashTable.Create(1 << v->ClusterBits);
            }
            // PerfectTable laissé null → à construire après si besoin

            v->Summary = Create(v->ClusterBits, useCuckoo);
        }

        return v;
    }

    public static void Insert(VanEmdeBoas* v, int key)
    {
        if (key < 0 || key >= (1 << v->UniverseBits))
            throw new ArgumentOutOfRangeException(nameof(key));

        if (v->Min == -1)
        {
            v->Min = v->Max = key;
            return;
        }

        if (key < v->Min) (v->Min, key) = (key, v->Min);
        if (key == v->Max) return;

        if (v->UniverseBits <= MIN_BITS)
        {
            v->Max = Math.Max(v->Max, key);
            return;
        }

        int hi = High(v, key);
        int lo = Low(v, key);

        if (v->UseCuckoo)
        {
            var entry = CuckooHashTable.Find(v->CuckooTable, hi);
            VanEmdeBoas* cluster;

            if (entry == null)
            {
                cluster = Create(v->ClusterBits, true);
                cluster->Min = cluster->Max = lo;
                CuckooHashTable.Insert(v->CuckooTable, hi, 0, cluster);
                Insert(v->Summary, hi);
            }
            else
            {
                cluster = (VanEmdeBoas*)entry->Data;
                if (cluster->Min == -1)
                {
                    cluster->Min = cluster->Max = lo;
                    Insert(v->Summary, hi);
                }
                else
                {
                    Insert(cluster, lo);
                }
            }
        }
        else
        {
            throw new NotImplementedException("Static perfect hashing vEB mode not implemented in this version");
        }

        if (key > v->Max) v->Max = key;
    }

    public static int Successor(VanEmdeBoas* v, int x)
    {
        if (v == null || v->Min == -1) return -1;
        if (x < v->Min) return v->Min;
        if (x >= v->Max) return -1;

        if (v->UniverseBits <= MIN_BITS)
        {
            return v->Max > x ? v->Max : -1;
        }

        int hi = High(v, x);
        int lo = Low(v, x);

        VanEmdeBoas* cluster = null;
        if (v->UseCuckoo)
        {
            var e = CuckooHashTable.Find(v->CuckooTable, hi);
            if (e != null) cluster = (VanEmdeBoas*)e->Data;
        }

        if (cluster != null && lo < cluster->Max)
        {
            int s = Successor(cluster, lo);
            if (s != -1) return Index(v, hi, s);
        }

        int succHi = Successor(v->Summary, hi);
        if (succHi == -1) return -1;

        VanEmdeBoas* next = null;
        if (v->UseCuckoo)
        {
            var e = CuckooHashTable.Find(v->CuckooTable, succHi);
            if (e != null) next = (VanEmdeBoas*)e->Data;
        }

        if (next == null || next->Min == -1) return -1;
        return Index(v, succHi, next->Min);
    }

    public static void Destroy(VanEmdeBoas* v)
    {
        if (v == null) return;

        if (v->UniverseBits > MIN_BITS)
        {
            if (v->UseCuckoo && v->CuckooTable != null)
            {
                for (int i = 0; i < v->CuckooTable->Size; i++)
                {
                    var e1 = &v->CuckooTable->Table1[i];
                    if (e1->Key != 0 && !e1->IsTombstone && e1->Data != null)
                    {
                        Destroy((VanEmdeBoas*)e1->Data);
                    }

                    var e2 = &v->CuckooTable->Table2[i];
                    if (e2->Key != 0 && !e2->IsTombstone && e2->Data != null)
                    {
                        Destroy((VanEmdeBoas*)e2->Data);
                    }
                }
                CuckooHashTable.Destroy(v->CuckooTable);
            }

            if (v->PerfectTable != null)
            {
                for (int i = 0; i < v->PerfectTable->TableSize; i++)
                {
                    ref var b = ref v->PerfectTable->Buckets[i];
                    if (b.SubTable != null)
                    {
                        for (int j = 0; j < b.SubTableSize; j++)
                        {
                            if (b.SubTable[j].Data != null)
                            {
                                Destroy((VanEmdeBoas*)b.SubTable[j].Data);
                            }
                        }
                    }
                }
                PerfectHashTable.Destroy(v->PerfectTable);
            }

            Destroy(v->Summary);
        }

        NativeHelpers.AlignedFree(v);
    }
}
