using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;

namespace Aether.Graphics.Terrain;

public readonly record struct ShapeQuad(int X,int Y,int U,int V,int Width,int Height);

public static class ChunkTileShape
{
    public const int Normal=255, Half=5, HalfEdge=6;
    public static int FromSnapshot(SnapshotTile tile,SnapshotTile below)
    {
        int slope=(tile.Header>>12)&7;
        if(slope>4 || (slope!=0 && (tile.Header&1024)!=0))return 0;
        if((tile.Header&1024)==0)return slope==0?Normal:slope;
        if((below.Header&32)==0 || (below.Header&1024)!=0)return HalfEdge;
        // Unknown solid/material tables stay owner-side. Do not guess below an
        // unsupported block; that half block remains on compatibility drawing.
        return ChunkMaterials.Index(below.Type)>=0?Half:0;
    }
    public static unsafe int WriteQuads(int shape,int frameX,int frameY,ShapeQuad* output)
    {
        if(shape==Normal){output[0]=new(0,0,frameX,frameY,16,16);return 1;}
        if(shape==Half){output[0]=new(0,8,frameX,frameY,16,8);return 1;}
        if(shape==HalfEdge)
        {output[0]=new(0,8,frameX,frameY,16,4);output[1]=new(0,12,144,66,16,4);return 2;}
        if(shape<1 || shape>4)throw new ArgumentOutOfRangeException(nameof(shape));
        int count=0;
        for(int i=0;i<8;i++)
        {
            int x=(shape==2 || shape==4)?14-i*2:i*2;
            int y=shape<=2?i*2:0,height=shape<=2?14-i*2:16-i*2;
            if(height>0)output[count++]=new(x,y,frameX+x,frameY+(shape<=2?0:i*2),2,height);
        }
        int edge=shape<=2?14:0;output[count++]=new(0,edge,frameX,frameY+edge,16,2);return count;
    }
}
