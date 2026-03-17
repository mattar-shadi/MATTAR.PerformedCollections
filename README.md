# MATTAR.PerformanceCollections

[![NuGet](https://img.shields.io/nuget/v/MATTAR.PerformanceCollections.svg)](https://www.nuget.org/packages/MATTAR.PerformanceCollections/)

Bibliothèque .NET de collections hautes performances reposant sur des allocations natives et des blocs `unsafe`.  
Elle propose une table de hachage de Cuckoo, une table de hachage parfaite et un arbre de van Emde Boas — trois structures de données orientées vitesse, conçues pour les scénarios où la latence compte.

---

## ✨ Fonctionnalités

| Structure | Description |
|---|---|
| **CuckooHashTable** | Table de hachage à déplacement de coucou. Insertions et recherches en O(1) amorti avec un facteur de charge maîtrisé (≤ 45 %). |
| **PerfectHashTable** | Table de hachage parfaite à deux niveaux (FKS). Construite à partir d'un ensemble de clés statique ; recherches en O(1) strict. |
| **VanEmdeBoas** | Arbre de van Emde Boas dynamique. Successeur/prédécesseur en O(log log U) sur un univers de taille 2^U (U ≤ 30). |

**Points forts :**
- Allocation native alignée (via `NativeMemory`) — zéro pression sur le GC.
- Blocs `unsafe` et inlining agressif (`AggressiveInlining`) pour des performances maximales.
- Cible .NET 8.

---

## 📦 Installation

Le package est disponible sur NuGet : [MATTAR.PerformanceCollections](https://www.nuget.org/packages/MATTAR.PerformanceCollections/)

```bash
dotnet add package MATTAR.PerformanceCollections
```

---

## 🚀 Utilisation

> **Note :** les structures sont des `unsafe struct` allouées nativement. N'oubliez pas d'activer `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` dans votre `.csproj` et d'appeler `Destroy` pour libérer la mémoire.

### CuckooHashTable

```csharp
unsafe
{
    // Création (capacité initiale : 64 entrées)
    CuckooHashTable* table = CuckooHashTable.Create(64);

    // Insertion  (clé != 0 — 0 est la sentinelle "vide")
    CuckooHashTable.Insert(table, key: 42, value: 100);

    // Recherche
    CuckooHashTable.Entry* entry = CuckooHashTable.Find(table, key: 42);
    if (entry != null)
        Console.WriteLine(entry->Value); // 100

    // Suppression
    CuckooHashTable.Delete(table, key: 42);

    // Libération
    CuckooHashTable.Destroy(table);
}
```

### PerfectHashTable

```csharp
unsafe
{
    int[] keys   = { 1, 7, 42, 100 };
    int[] values = { 10, 70, 420, 1000 };

    // Construction à partir d'un ensemble statique de clés
    PerfectHashTable* table = PerfectHashTable.Create(keys, values);

    // Recherche en O(1) strict
    PerfectHashTable.Entry* entry = PerfectHashTable.Find(table, key: 42);
    if (entry != null)
        Console.WriteLine(entry->Value); // 420

    PerfectHashTable.Destroy(table);
}
```

### VanEmdeBoas (unsafe)

```csharp
unsafe
{
    // Univers de taille 2^20 (~1 million de valeurs possibles)
    UnSafeVanEmdeBoas* veb = UnSafeVanEmdeBoas.Create(universeBits: 20);

    UnSafeVanEmdeBoas.Insert(veb, 3);
    UnSafeVanEmdeBoas.Insert(veb, 17);
    UnSafeVanEmdeBoas.Insert(veb, 42);

    // Successeur en O(log log U)
    int next = UnSafeVanEmdeBoas.Successor(veb, x: 10); // → 17

    UnSafeVanEmdeBoas.Destroy(veb);
}
```

---

## 📌 Feuille de route

- [x] Table de hachage de Cuckoo
- [x] Table de hachage parfaite (FKS)
- [x] Arbre de van Emde Boas
- [x] Publication NuGet officielle
- [ ] Mode `PerfectTable` pour le vEB statique
- [ ] Benchmarks (BenchmarkDotNet)
- [ ] Tests unitaires complets

---

## 📄 Licence

Ce projet est distribué sous licence **MIT**. Voir le fichier [LICENSE](LICENSE) pour les détails.
