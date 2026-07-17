//=======================================================================
// RuntimeIdentityAllocator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace GridForge;

internal static class RuntimeIdentityAllocator
{
    internal static long Allocate(ref long counter)
    {
        while (true)
        {
            long current = Volatile.Read(ref counter);
            if (current < 0 || current == long.MaxValue)
                throw new InvalidOperationException("Runtime identity allocator exhausted.");

            long next = current + 1;
            if (Interlocked.CompareExchange(ref counter, next, current) == current)
                return next;
        }
    }
}
