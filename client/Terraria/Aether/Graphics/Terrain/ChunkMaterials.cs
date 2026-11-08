using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;

namespace Aether.Graphics.Terrain;

// Explicit ordinary-block allowlist, not a claim that arbitrary tile materials
// have interchangeable draw behavior. Slots are compact; they are not tile IDs.
// Runtime admission still checks the actual draw rectangle, texture and flags.
public static class ChunkMaterials
{
    private static readonly ushort[] tileTypes = [
        0,1,2,23,25,40,53,57,59,60,70,109,112,116,117,123,
        147,161,163,164,199,200,203,224,225,229,230,234,
        367,368,396,397,398,399,400,401,402,403
    ];
    private static readonly byte[] slots = CreateSlots();
    public static int Count => tileTypes.Length;
    public static ushort[] TileTypes => tileTypes;
    public static ushort TileType(int slot) => tileTypes[slot];
    public static int Index(ushort type) => type < slots.Length ? slots[type] - 1 : -1;
    private static byte[] CreateSlots()
    {
        var result = new byte[tileTypes[tileTypes.Length - 1] + 1];
        for (int i = 0; i < tileTypes.Length; i++) result[tileTypes[i]] = checked((byte)(i + 1));
        return result;
    }
}
