#version 450
layout(set=2,binding=0) uniform sampler2D atlas;
layout(set=2,binding=1) uniform sampler2D lighting;
layout(set=3,binding=0,std140) uniform FragmentData { vec4 atlasOrigin; vec4 chunkAndLightOrigin; vec4 tint; vec4 reserved; } p;
layout(location=0) in vec2 uv;
layout(location=1) in vec2 local;
layout(location=2) in vec4 anchor;
layout(location=0) out vec4 pixel;
void main() {
    ivec2 cell=ivec2(round(anchor.rg*255.0)+p.chunkAndLightOrigin.xy-p.chunkAndLightOrigin.zw)*2;
    vec4 tl=texelFetch(lighting,cell,0),tr=texelFetch(lighting,cell+ivec2(1,0),0);
    vec4 br=texelFetch(lighting,cell+ivec2(1,1),0),bl=texelFetch(lighting,cell+ivec2(0,1),0);
    // Match TileBatch's two triangles (TL,TR,BR) and (TL,BR,BL), not bilinear interpolation.
    vec4 light=local.x>=local.y ? tl*(1.0-local.x)+tr*(local.x-local.y)+br*local.y : tl*(1.0-local.y)+br*local.x+bl*(local.y-local.x);
    pixel=texture(atlas,(uv+p.atlasOrigin.xy+vec2(0.0,p.atlasOrigin.w))/vec2(textureSize(atlas,0)))*light;
}
