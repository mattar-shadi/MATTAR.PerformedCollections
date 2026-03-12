using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

internal static unsafe class NativeHelpers
{
    private static readonly ThreadLocal<Random> _threadRandom = new(() => new Random());

    public static void* AlignedAlloc(nuint size, nuint alignment = 64)
        => NativeMemory.AlignedAlloc(size, alignment);

    public static void AlignedFree(void* ptr)
        => NativeMemory.AlignedFree(ptr);

    public static void Clear(void* ptr, nuint size)
        => NativeMemory.Clear(ptr, size);

    public static ulong RandomULong() => (ulong)_threadRandom.Value!.NextInt64();

    public static ulong RandomOddULong() => ((ulong)_threadRandom.Value!.NextInt64() << 1) | 1UL;

    public static int NextPowerOfTwo(int n)
    {
        if (n <= 0) return 1;
        n--;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        return n + 1;
    }

    public static int Log2(uint value) => BitOperations.Log2(value);
}
