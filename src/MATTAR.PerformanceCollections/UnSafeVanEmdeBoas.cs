using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

internal unsafe struct UnSafeVanEmdeBoas
{
    internal int UniverseBits;
    internal int ClusterBits;
    internal int Min;
    internal int Max;

    internal bool UseCuckoo;
    internal CuckooHashTable* CuckooTable;
#pragma warning disable CS0649 // Field is never assigned to (PerfectTable path is reserved for future use)
    internal PerfectHashTable* PerfectTable;
#pragma warning restore CS0649

    internal UnSafeVanEmdeBoas* Summary;

    internal const int MIN_BITS = 2;
    internal const int MAX_UNIVERSE_BITS = 30;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int High(UnSafeVanEmdeBoas* v, int x) => x >> v->ClusterBits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Low(UnSafeVanEmdeBoas* v, int x) => x & ((1 << v->ClusterBits) - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Index(UnSafeVanEmdeBoas* v, int high, int low)
    {
        int result = (high << v->ClusterBits) | low;
        if (result >= (1 << v->UniverseBits) || result < 0)
            throw new OverflowException("Index calculation overflow");
        return result;
    }

    internal static UnSafeVanEmdeBoas* Create(int universeBits, bool useCuckoo = true, int[]? presetKeys = null)
    {
        if (universeBits > MAX_UNIVERSE_BITS)
            throw new ArgumentException($"Universe too large (max 2^{MAX_UNIVERSE_BITS})", nameof(universeBits));

        if (!useCuckoo && (presetKeys == null || presetKeys.Length == 0))
            throw new ArgumentException(
                "PerfectTable mode requires a non-empty presetKeys array.", nameof(presetKeys));

        if (universeBits < MIN_BITS) universeBits = MIN_BITS;

        if (!useCuckoo)
            return CreateStatic(universeBits, presetKeys!);

        var v = (UnSafeVanEmdeBoas*)NativeHelpers.AlignedAlloc((nuint)sizeof(UnSafeVanEmdeBoas));
        *v = new UnSafeVanEmdeBoas
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

    /// <summary>
    /// Recursively builds a static VEB tree from a sorted, deduplicated set of keys.
    /// Clusters are indexed via PerfectHashTable using stored key = hi + 1 to avoid the
    /// Key == 0 empty-slot sentinel.
    /// </summary>
    private static UnSafeVanEmdeBoas* CreateStatic(int universeBits, int[] keys)
    {
        var sortedKeys = keys.Distinct().Where(k => k >= 0 && k < (1 << universeBits))
                             .OrderBy(k => k).ToArray();

        var v = (UnSafeVanEmdeBoas*)NativeHelpers.AlignedAlloc((nuint)sizeof(UnSafeVanEmdeBoas));
        *v = new UnSafeVanEmdeBoas
        {
            UniverseBits = universeBits,
            ClusterBits = universeBits >> 1,
            Min = -1,
            Max = -1,
            UseCuckoo = false
        };

        if (sortedKeys.Length == 0) return v;

        v->Min = sortedKeys[0];
        v->Max = sortedKeys[^1];

        if (universeBits <= MIN_BITS) return v;

        int clusterBits = v->ClusterBits;
        int clusterMask = (1 << clusterBits) - 1;

        // Group keys[1..] (all except the VEB Min) by their high part.
        var groups = new Dictionary<int, List<int>>();
        for (int i = 1; i < sortedKeys.Length; i++)
        {
            int k = sortedKeys[i];
            int hi = k >> clusterBits;
            int lo = k & clusterMask;
            if (!groups.TryGetValue(hi, out var list))
            {
                list = new List<int>();
                groups[hi] = list;
            }
            list.Add(lo);
        }

        if (groups.Count == 0) return v;

        // Build one cluster per unique hi value.
        int numClusters = groups.Count;
        var storedKeys = new int[numClusters]; // hi + 1 to dodge the Key == 0 sentinel
        var values     = new int[numClusters]; // unused; required by PerfectHashTable.Create
        var clusterPtrs = new void*[numClusters];

        int idx = 0;
        foreach (var kvp in groups)
        {
            int hi = kvp.Key;
            storedKeys[idx] = hi + 1;
            clusterPtrs[idx] = CreateStatic(clusterBits, kvp.Value.ToArray());
            idx++;
        }

        // Build PerfectHashTable mapping (hi + 1) → cluster pointer.
        fixed (void** dataPtr = clusterPtrs)
        {
            v->PerfectTable = PerfectHashTable.Create(storedKeys, values, dataPtr);
        }

        // Build Summary: static VEB containing the hi values of all non-empty clusters.
        var sortedHiValues = groups.Keys.OrderBy(h => h).ToArray();
        v->Summary = CreateStatic(clusterBits, sortedHiValues);

        return v;
    }

    internal static void Insert(UnSafeVanEmdeBoas* v, int key)
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
            UnSafeVanEmdeBoas* cluster;

            if (entry == null)
            {
                cluster = Create(v->ClusterBits, true);
                cluster->Min = cluster->Max = lo;
                CuckooHashTable.Insert(v->CuckooTable, hi, 0, cluster);
                Insert(v->Summary, hi);
            }
            else
            {
                cluster = (UnSafeVanEmdeBoas*)entry->Data;
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
            throw new InvalidOperationException(
                "Cannot insert into a static PerfectTable VEB tree. The structure is immutable after construction.");
        }

        if (key > v->Max) v->Max = key;
    }

    internal static int Successor(UnSafeVanEmdeBoas* v, int x)
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

        UnSafeVanEmdeBoas* cluster = null;
        if (v->UseCuckoo)
        {
            var e = CuckooHashTable.Find(v->CuckooTable, hi);
            if (e != null) cluster = (UnSafeVanEmdeBoas*)e->Data;
        }
        else
        {
            var e = PerfectHashTable.Find(v->PerfectTable, hi + 1);
            if (e != null) cluster = (UnSafeVanEmdeBoas*)e->Data;
        }

        if (cluster != null && lo < cluster->Max)
        {
            int s = Successor(cluster, lo);
            if (s != -1) return Index(v, hi, s);
        }

        int succHi = Successor(v->Summary, hi);
        if (succHi == -1) return -1;

        UnSafeVanEmdeBoas* next = null;
        if (v->UseCuckoo)
        {
            var e = CuckooHashTable.Find(v->CuckooTable, succHi);
            if (e != null) next = (UnSafeVanEmdeBoas*)e->Data;
        }
        else
        {
            var e = PerfectHashTable.Find(v->PerfectTable, succHi + 1);
            if (e != null) next = (UnSafeVanEmdeBoas*)e->Data;
        }

        if (next == null || next->Min == -1) return -1;
        return Index(v, succHi, next->Min);
    }

    internal static void Destroy(UnSafeVanEmdeBoas* v)
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
                        Destroy((UnSafeVanEmdeBoas*)e1->Data);
                    }

                    var e2 = &v->CuckooTable->Table2[i];
                    if (e2->Key != 0 && !e2->IsTombstone && e2->Data != null)
                    {
                        Destroy((UnSafeVanEmdeBoas*)e2->Data);
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
                                Destroy((UnSafeVanEmdeBoas*)b.SubTable[j].Data);
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
