using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// A safe, managed wrapper around the unmanaged <see cref="UnSafeVanEmdeBoas"/> tree.
/// Implements <see cref="IEnumerable{T}"/> so callers can iterate over all
/// stored integers (in ascending order) using LINQ or foreach without
/// touching any unsafe code.
/// </summary>
/// <remarks>
/// This class is <b>not</b> thread-safe. Inserting elements while an
/// enumeration is in progress will produce undefined results.
/// </remarks>
public sealed class VanEmdeBoas : IEnumerable<int>, IDisposable
{
    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private unsafe UnSafeVanEmdeBoas* _tree;
    private bool _disposed;

    // Version counter incremented on every mutation; used by the enumerator
    // to detect modification during iteration.
    private int _version;

    // -----------------------------------------------------------------------
    // Constructor / Destructor
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a new VEB tree that can store integers in [0, 2^<paramref name="universeBits"/>).
    /// </summary>
    /// <param name="universeBits">
    /// Number of bits in the universe (2-30).
    /// Elements must satisfy 0 &lt;= element &lt; 2^universeBits.
    /// </param>
    public unsafe VanEmdeBoas(int universeBits = 20)
    {
        _tree = UnSafeVanEmdeBoas.Create(universeBits);
    }

    /// <summary>
    /// Takes ownership of an existing unmanaged <see cref="UnSafeVanEmdeBoas"/> tree.
    /// The tree will be destroyed when this instance is disposed.
    /// </summary>
    /// <param name="tree">Pointer to an allocated <see cref="UnSafeVanEmdeBoas"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    internal unsafe VanEmdeBoas(UnSafeVanEmdeBoas* tree)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));
        _tree = tree;
    }

    ~VanEmdeBoas() => Dispose(false);

    // -----------------------------------------------------------------------
    // Properties
    // -----------------------------------------------------------------------

    /// <summary>Minimum element stored in the tree, or -1 if empty.</summary>
    public unsafe int Min
    {
        get
        {
            ThrowIfDisposed();
            return _tree->Min;
        }
    }

    /// <summary>Maximum element stored in the tree, or -1 if empty.</summary>
    public unsafe int Max
    {
        get
        {
            ThrowIfDisposed();
            return _tree->Max;
        }
    }

    /// <summary>
    /// Returns true when the tree contains no elements.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    public unsafe bool IsEmpty
    {
        get
        {
            ThrowIfDisposed();
            return _tree->Min == -1;
        }
    }

    // -----------------------------------------------------------------------
    // Mutation
    // -----------------------------------------------------------------------

    /// <summary>Inserts <paramref name="key"/> into the tree.</summary>
    public unsafe void Insert(int key)
    {
        ThrowIfDisposed();
        UnSafeVanEmdeBoas.Insert(_tree, key);
        _version++;
    }

    // -----------------------------------------------------------------------
    // Query
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the smallest element strictly greater than <paramref name="x"/>,
    /// or -1 if no such element exists.
    /// </summary>
    public unsafe int Successor(int x)
    {
        ThrowIfDisposed();
        return UnSafeVanEmdeBoas.Successor(_tree, x);
    }

    // -----------------------------------------------------------------------
    // IEnumerable<int>
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public IEnumerator<int> GetEnumerator()
    {
        ThrowIfDisposed();
        return new Enumerator(this);
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private unsafe void Dispose(bool disposing)
    {
        if (_disposed) return;
        UnSafeVanEmdeBoas.Destroy(_tree);
        _tree = null;
        _disposed = true;
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VanEmdeBoas));
    }

    // -----------------------------------------------------------------------
    // Nested Enumerator
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enumerates all elements of a <see cref="VanEmdeBoas"/> in ascending
    /// order by repeatedly calling <see cref="VanEmdeBoas.Successor"/>.
    /// All unsafe operations are encapsulated inside this type.
    /// </summary>
    private sealed class Enumerator : IEnumerator<int>
    {
        private readonly VanEmdeBoas _owner;
        // _current uses -1 as a sentinel for "not yet started / finished".
        // The field must be explicitly initialised because the default int value
        // is 0, which is a valid element value.
        private int _current;
        private bool _started;
        private readonly int _version;

        internal Enumerator(VanEmdeBoas owner)
        {
            _owner = owner;
            _current = -1;
            _started = false;
            _version = owner._version;
        }

        /// <inheritdoc/>
        public int Current
        {
            get
            {
                if (!_started || _current == -1)
                    throw new InvalidOperationException(
                        "Enumeration has either not started or has already finished.");
                return _current;
            }
        }

        object IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            _owner.ThrowIfDisposed();

            if (_owner._version != _version)
                throw new InvalidOperationException(
                    "Collection was modified; enumeration operation may not execute.");

            if (!_started)
            {
                // First call: start from the minimum element.
                _started = true;
                _current = _owner.Min;
            }
            else if (_current != -1)
            {
                // Subsequent calls: advance to the next element.
                _current = _owner.Successor(_current);
            }

            return _current != -1;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _current = -1;
            _started = false;
        }

        /// <inheritdoc/>
        public void Dispose() { /* nothing to release */ }
    }
}
