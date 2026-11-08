using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Aether.Graphics.Terrain;

public readonly record struct AtlasRegion(int Page, int X, int Y, int Width, int Height, int Padding);
// Pure deterministic shelf allocation. Main thread handles actual GPU copies;
// this allocator knows no graphics object or live tile/asset state.
public sealed class ChunkAtlasLayout
{
    public const int DefaultPageSize = 2048;
    public const int MaximumPages = 4;
    private readonly int size, maximum;
    private readonly List<Page> pages = new();
    public int PageCount => pages.Count;
    public ChunkAtlasLayout(int pageSize = DefaultPageSize, int maximumPages = MaximumPages)
    { if (pageSize < 4 || pageSize > 8192 || maximumPages < 1 || maximumPages > MaximumPages) throw new ArgumentOutOfRangeException(); size = pageSize; maximum = maximumPages; }
    public bool TryAllocate(int width, int height, out AtlasRegion region, int padding = 2)
    {
        region = default;
        if (width < 1 || height < 1 || padding < 1 || padding > 8 || width > size - padding*2 || height > size - padding*2) return false;
        for (int i=0;i<=pages.Count;i++)
        {
            if(i==pages.Count) { if(pages.Count==maximum)return false;pages.Add(new Page()); }
            var page=pages[i];int x=page.X,y=page.Y,row=page.Row;
            if(x+width+padding*2>size) {x=0;y+=row;row=0;}
            if(y+height+padding*2>size)continue;
            region=new(i,x+padding,y+padding,width,height,padding);
            page.X=x+width+padding*2;page.Y=y;page.Row=Math.Max(row,height+padding*2);return true;
        }
        return false;
    }
    private sealed class Page { public int X,Y,Row; }
}
