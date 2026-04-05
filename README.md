# MATTAR.PerformanceCollections

[![NuGet](https://img.shields.io/nuget/v/MATTAR.PerformanceCollections.svg)](https://www.nuget.org/packages/MATTAR.PerformanceCollections/)

High-performance .NET collections built on native allocations and `unsafe` blocks.  
It provides a Cuckoo hash table, a perfect hash table, and a van Emde Boas tree—three speed-oriented data structures designed for scenarios where latency matters.

---

## ✨ Features

| Data structure | Description |
|---|---|
| **CuckooHashTable** | Cuckoo displacement hash table. Amortized **O(1)** inserts and lookups with controlled load factor (≤ **45%**). |
| **PerfectHashTable** | Two-level perfect hash table (FKS). Built from a **static** set of keys; strict **O(1)** lookups. |
| **VanEmdeBoas** | van Emde Boas tree. Two modes: **dynamic** (Cuckoo-based, inserts on the fly) and **static** (PerfectHashTable-based, built from a known key set). |

**Highlights**
- Aligned native allocation (via `NativeMemory`) — zero GC pressure.
- `unsafe` blocks + aggressive inlining (`AggressiveInlining`) for maximum performance.
- Targets **.NET 8**.

---

## 📦 Installation

The package is available on NuGet: [MATTAR.PerformanceCollections](https://www.nuget.org/packages/MATTAR.PerformanceCollections/)

```bash
dotnet add package MATTAR.PerformanceCollections
```

---

## 🚀 Usage

> **Note:** these data structures are `unsafe struct`s allocated natively.  
> Don’t forget to enable `<AllowUnsafeBlocks>true</AllowUnsafeBlocks)>` in your `.csproj`, and call `Destroy` to free native memory.

### CuckooHashTable

```csharp
unsafe
{
    // Create (initial capacity: 64 entries)
    CuckooHashTable* table = CuckooHashTable.Create(64);

    // Insert (key != 0 — 0 is the "empty" sentinel)
    CuckooHashTable.Insert(table, key: 42, value: 100);

    // Lookup
    CuckooHashTable.Entry* entry = CuckooHashTable.Find(table, key: 42);
    if (entry != null)
        Console.WriteLine(entry->Value); // 100

    // Delete
    CuckooHashTable.Delete(table, key: 42);

    // Free
    CuckooHashTable.Destroy(table);
}
```

### PerfectHashTable

```csharp
unsafe
{
    int[] keys   = { 1, 7, 42, 100 };
    int[] values = { 10, 70, 420, 1000 };

    // Build from a static key set
    PerfectHashTable* table = PerfectHashTable.Create(keys, values);

    // Strict O(1) lookup
    PerfectHashTable.Entry* entry = PerfectHashTable.Find(table, key: 42);
    if (entry != null)
        Console.WriteLine(entry->Value); // 420

    PerfectHashTable.Destroy(table);
}
```

### VanEmdeBoas – dynamic mode (Cuckoo)

```csharp
unsafe
{
    // Universe size: 2^20 (~1 million possible values)
    UnSafeVanEmdeBoas* veb = UnSafeVanEmdeBoas.Create(universeBits: 20);

    UnSafeVanEmdeBoas.Insert(veb, 3);
    UnSafeVanEmdeBoas.Insert(veb, 17);
    UnSafeVanEmdeBoas.Insert(veb, 42);

    // Successor in O(log log U)
    int next = UnSafeVanEmdeBoas.Successor(veb, x: 10); // → 17

    UnSafeVanEmdeBoas.Destroy(veb);
}
```

### VanEmdeBoas – static mode (PerfectTable)

Static mode builds the tree in a single pass from a known key set. Clusters are indexed using `PerfectHashTable` (FKS) instead of `CuckooHashTable`, which enables faster and more compact access when the set is fixed.

> **Static mode limitations**
> - The tree is **immutable** after construction: `Insert` throws `InvalidOperationException`.
> - The builder requires a non-empty key array; duplicates and out-of-universe values are silently ignored.

```csharp
// Managed API (recommended)
int[] keys = { 100, 200, 500, 1000, 5000 };

using var veb = VanEmdeBoas.CreateStatic(keys, universeBits: 16);

Console.WriteLine(veb.Min);            // 100
Console.WriteLine(veb.Successor(200)); // 500
Console.WriteLine(veb.Max);            // 5000

// The tree is immutable: any insertion attempt throws.
// veb.Insert(42); // → InvalidOperationException
```

```csharp
// Unsafe API (low-level)
unsafe
{
    int[] keys = { 100, 200, 500, 1000, 5000 };

    UnSafeVanEmdeBoas* veb = UnSafeVanEmdeBoas.Create(
        universeBits: 16,
        useCuckoo: false,
        presetKeys: keys);

    int next = UnSafeVanEmdeBoas.Successor(veb, x: 200); // → 500

    UnSafeVanEmdeBoas.Destroy(veb);
}
```

---

## 🧪 Tests

Unit tests live in `tests/MATTAR.PerformanceCollections.Tests` and cover `CuckooHashTable`, `PerfectHashTable`, `VanEmdeBoas` (dynamic and static modes), and the low-level `UnSafeVanEmdeBoas` API.

Run the full suite:

```bash
dotnet test tests/MATTAR.PerformanceCollections.Tests/MATTAR.PerformanceCollections.Tests.csproj
```

Tests are also executed automatically on every push and pull-request to `main` via the **Build** GitHub Actions workflow.

---

## 📊 Benchmarks

Comparative benchmarks are available in `benchmarks/` and measure `CuckooHashTable` and `PerfectHashTable` against standard .NET collections (`Dictionary`, `HashSet`, etc.).

Quick run:

```bash
dotnet run -c Release --project benchmarks/MATTAR.PerformanceCollections.Benchmarks
```

Filter by collection:

```bash
dotnet run -c Release --project benchmarks/MATTAR.PerformanceCollections.Benchmarks -- --filter *CuckooVsDictionary*
```

See [BENCHMARKS.md](BENCHMARKS.md) for full documentation (scenarios, parameters, and how to interpret results).

---

## 📌 Roadmap

- [x] Cuckoo hash table
- [x] Perfect hash table (FKS)
- [x] van Emde Boas tree
- [x] Official NuGet release
- [x] `PerfectTable` mode for static vEB
- [x] Benchmarks (BenchmarkDotNet)
- [x] Complete unit test coverage

---

## 📄 License

This project is distributed under the **MIT** license. See [LICENSE](LICENSE) for details.