using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using GameVector=Microsoft.Xna.Framework.Vector2;
using GameColor=Microsoft.Xna.Framework.Color;

namespace Aether.Graphics.Terrain;

public sealed partial class ChunkedTerrainRenderer
{
    private Slot? plainSlot;
    private Material? plainMaterial;
    private int plainMaterialType=-1;
    private Command? lastTileCommand,preparedCommand;
    private int preparedX,preparedY;
    private long preparedStamp;
    private void ResetFastBindings()
    {plainSlot=null;plainMaterial=null;plainMaterialType=-1;lastTileCommand=preparedCommand=null;}
    public bool CanPreparePlainTiles => ChunkFastDraw.Enabled && !passDisabled;
    public bool TryPreparePlainTile(int x,int y,Tile tile,GameColor tileLight,GameVector delta,out Texture2D texture)
    {
        preparedCommand=null;texture=null!;
        ushort type=tile.type;
        if (!ChunkFastDraw.Enabled || passDisabled || !ChunkFastDraw.IsPlain(type,tile.sTileHeader,tile.bTileHeader3) ||
            Main.tileFrame[type]!=0 || Main.tileGlowMask[type]!=-1 || !IsOrdinaryType(type) ||
            TileID.Sets.HasOutlines[type] || TileID.Sets.BasicChest[type] || Main.shimmerAlpha>0) return false;
        // Do not spend freshness/upload budget on dark cells which the legacy
        // path would stop before queuing any terrain geometry.
        if (tileLight.R==0 && tileLight.G==0 && tileLight.B==0 &&
            !(tile.wall>0 && (tile.wall==318 || tile.fullbrightWall()))) return false;
        var key=ChunkKey.FromTile(x,y);
        if (!TerrainSnapshotPump.TryGetValidated(key,out var prepared) || prepared?.Geometry is null ||
            !prepared.Geometry.MatchesPlain(x-key.X*64,y-key.Y*64,type,tile.frameX,tile.frameY)) return false;
        var slot=plainSlot;
        if(slot is null || slot.Result.Key!=key || slot.Result.Version!=prepared.Version)
        {if(!slots.TryGetValue(key,out slot))return false;plainSlot=slot;}
        if(slot.Result.Version!=prepared.Version || !slot.Ready(type))return false;
        var source=TextureAssets.Tile[type].Value;
        var extent=prepared.Geometry.TextureExtent(type);
        if(extent.X>source.Width || extent.Y>source.Height)return false;
        var material=plainMaterial;
        if(material is null || plainMaterialType!=type || !ReferenceEquals(material.Source,source))
        {
            if(!materials.TryGetValue(type,out material) || !ReferenceEquals(material.Source,source))return false;
            plainMaterial=material;plainMaterialType=type;
        }
        int lx=x-left,ly=y-top;
        if(lx<1 || ly<1 || lx>=width-1 || ly>=height-1)return false;
        if(!lightReady && !PrepareLighting()){passDisabled=true;return false;}
        var command=GetTileCommand(slot,type,0,material,delta);
        if(command is null)return false;
        preparedCommand=command;preparedX=x;preparedY=y;preparedStamp=TerrainSnapshotPump.ValidationStamp;
        texture=material.Source;
        return true;
    }
    public bool TryQueuePreparedPlainTile(int x,int y,Tile tile,ushort type,GameColor tileLight,GameColor finalColor)
    {
        var command=preparedCommand;preparedCommand=null;
        if(command is null || x!=preparedX || y!=preparedY || passDisabled || Failure.Length!=0 ||
            !TerrainSnapshotPump.CanReuseBinding(preparedStamp) || !ChunkFastDraw.IsPlain(type,tile.sTileHeader,tile.bTileHeader3) ||
            finalColor!=tileLight || Main.shimmerAlpha>0)
        {return false;}
        lightPixels[(y-top)*width+x-left].W=-(float)((tileLight.PackedValue&0xffffffu)+1u);
        command.Cells.Add(new Cell(x,y,tileLight.PackedValue));command.Fast=true;

        return true;
    }

}
