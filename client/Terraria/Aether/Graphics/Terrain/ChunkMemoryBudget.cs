using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;

namespace Aether.Graphics.Terrain;

// Shared admission for queued snapshots, worst-case worker output and published
// CPU geometry. Worker-completion cleanup may release a reservation after disposal.
public sealed class ChunkMemoryBudget
{
    public const long Limit = 64L * 1024 * 1024;
    private readonly object sync = new();
    private long used;
    public long Used { get { lock(sync) return used; } }
    public bool TryReserve(long bytes)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (sync)
        {
            if (bytes > Limit - used) return false;
            used += bytes; return true;
        }
    }
    public void Release(long bytes)
    {
        lock (sync)
        {
            if (bytes <= 0 || bytes > used) throw new InvalidOperationException("Invalid chunk memory release");
            used -= bytes;
        }
    }
}
