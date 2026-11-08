using System;
using Microsoft.Xna.Framework;

namespace Aether.Graphics.EntityMotion;

// Immutable simulation samples are consumed by rendering; this type never
// updates an entity, consumes input, performs collision response or sends data.
public struct MotionHistory
{
    public Vector2 Previous { get; private set; }
    public Vector2 Current { get; private set; }
    public Vector2 Velocity { get; private set; }
    public bool HasPair { get; private set; }
    private bool valid;
    private long identity;
    public void Clear() => this=default;
    public bool Matches(Vector2 position,long key)=>valid&&identity==key&&Finite(position)&&Vector2.DistanceSquared(position,Current)<=.0001f;
    public bool MatchesIdentity(long key)=>valid&&identity==key;
    public void Sample(Vector2 position,Vector2 velocity,long key,float expectedTravel)
    {
        if(!Finite(position)||!Finite(velocity)){Clear();return;}
        float bound=Math.Max(96,expectedTravel*2+16);
        bool continuous=valid&&identity==key&&Vector2.DistanceSquared(Current,position)<=bound*bound;
        Previous=continuous?Current:position;Current=position;Velocity=velocity;
        HasPair=continuous;valid=true;identity=key;
    }
    public Vector2 Offset(Vector2 livePosition,float fraction,bool predict,Vector2 predictionVelocity)
    {
        // Network corrections, direct writes and reused slots must never draw
        // stale data. The next simulation sample establishes a fresh pair.
        if(!valid||!HasPair||!Finite(livePosition)||!Finite(predictionVelocity)||
            Vector2.DistanceSquared(livePosition,Current)>.0001f)return Vector2.Zero;
        if(!GraphicsMath.IsFinite(fraction))return Vector2.Zero;
        fraction=GraphicsMath.Clamp(fraction,0,1);
        // Blend a small delta rather than subtracting large world coordinates
        // after the lerp, retaining subpixel precision near world edges.
        return predict?predictionVelocity*fraction:(Previous-Current)*(1-fraction);
    }
    public static bool Finite(Vector2 value)=>GraphicsMath.IsFinite(value.X)&&GraphicsMath.IsFinite(value.Y);
}
