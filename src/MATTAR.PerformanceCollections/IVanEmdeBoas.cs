using System;
using System.Collections.Generic;

/// <summary>
/// Defines a safe, managed contract for a Van Emde Boas tree.
/// All members exposed by this interface are safe (no unsafe code visible to callers).
/// </summary>
public interface IVanEmdeBoas : IEnumerable<int>, IDisposable
{
    /// <summary>Minimum element stored in the tree, or -1 if empty.</summary>
    int Min { get; }

    /// <summary>Maximum element stored in the tree, or -1 if empty.</summary>
    int Max { get; }

    /// <summary>Returns <see langword="true"/> when the tree contains no elements.</summary>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    bool IsEmpty { get; }

    /// <summary>Inserts <paramref name="key"/> into the tree.</summary>
    /// <param name="key">The integer key to insert. Must be in [0, 2^universeBits).</param>
    void Insert(int key);

    /// <summary>
    /// Returns the smallest element strictly greater than <paramref name="x"/>,
    /// or -1 if no such element exists.
    /// </summary>
    /// <param name="x">The reference value.</param>
    int Successor(int x);
}
