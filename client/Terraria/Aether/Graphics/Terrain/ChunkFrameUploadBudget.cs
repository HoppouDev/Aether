using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
namespace Aether.Graphics.Terrain;

public sealed class ChunkFrameUploadBudget
{
    public const int Limit = 1024 * 1024;
    public int Used { get; private set; }
    public int Remaining => Limit - Used;
    public void BeginFrame() => Used = 0;
    public bool TryReserve(int bytes)
    {
        if (bytes < 0 || bytes > Remaining) return false;
        Used += bytes; return true;
    }
}
