#version 450
layout(set=2,binding=0) uniform sampler2D atlas;
layout(set=2,binding=1) uniform sampler2D lighting;
layout(set=3,binding=0,std140) uniform Parameters {
    vec4 atlasAndLightOrigin;
    vec4 highAndSmooth;
    vec4 medium;
    vec4 activeSize;
} p;
layout(location=0) in vec2 uv;
layout(location=1) in vec2 tile;
layout(location=2) flat in uvec4 appearance;
layout(location=0) out vec4 pixel;
vec3 lightAt(ivec2 cell) { return texelFetch(lighting,cell,0).rgb; }
void main() {
    if(appearance.r!=uint(p.activeSize.z))discard;
    bool fullbright=(appearance.g&1u)!=0u;
    if(p.activeSize.w>0.5 && appearance.r==0u && !fullbright)discard;
    ivec2 cell=ivec2(round(tile))-ivec2(p.atlasAndLightOrigin.zw);
    if(any(lessThan(cell,ivec2(0)))||any(greaterThanEqual(cell,ivec2(p.activeSize.xy))))discard;
    vec4 raw=texelFetch(lighting,cell,0);
    // Negative alpha encodes the exact CPU-selected base RGB, plus a draw mask.
    // Integers 1..2^24 are represented exactly by R32F; do not pack NaN payloads.
    if(raw.a>=0.0)discard;
    uint packed=uint(-raw.a)-1u;
    vec3 base=vec3(packed&255u,(packed>>8)&255u,(packed>>16)&255u);
    vec3 lit=base/255.0;
    vec2 local=mod(uv,18.0);
    vec2 sampleUV=uv, underlyingUV=uv;
    bool overlaps=false;
    int shape=int(appearance.a);
    if(shape>=1 && shape<=4) {
        float step=floor(local.x/2.0);
        if(shape==2 || shape==4)step=7.0-step;
        if(shape<=2) {
            if(local.y<14.0) {
                if(local.y<step*2.0)discard;
                sampleUV.y-=step*2.0;
            }
        } else if(local.y>=2.0) {
            if(local.y>=16.0-step*2.0)discard;
            sampleUV.y+=step*2.0;
        } else { underlyingUV.y+=step*2.0; overlaps=true; }
    } else if(shape==5 || shape==6) {
        if(local.y<8.0)discard;
        sampleUV.y-=8.0;
        if(shape==6 && local.y>=12.0)sampleUV=vec2(144.0+local.x,66.0+local.y-12.0);
    } else if(!fullbright && p.highAndSmooth.w>0.5 && any(greaterThan(base,p.highAndSmooth.xyz))) {
        ivec2 offset=ivec2(local.x<4.0?-1:local.x<12.0?0:1,local.y<4.0?-1:local.y<12.0?0:1);
        precise vec3 average=(lightAt(cell+offset)*p.medium.w+lit)*0.5;
        lit=min(floor(average*255.0),vec3(255.0))/255.0;
    } else if(!fullbright && p.highAndSmooth.w>0.5 && any(greaterThan(base,p.medium.xyz))) {
        vec3 vertical=lightAt(cell+ivec2(0,local.y<8.0?-1:1));
        vec3 horizontal=lightAt(cell+ivec2(local.x<8.0?-1:1,0));
        // Match CPU left-associated sums; a dot product may reassociate a tie.
        precise float verticalSum=(vertical.x+vertical.y)+vertical.z;
        precise float horizontalSum=(horizontal.x+horizontal.y)+horizontal.z;
        vec3 neighbor=verticalSum>=horizontalSum?horizontal:vertical;
        precise vec3 average=(neighbor*p.medium.w+lit)*0.5;
        lit=min(floor(average*255.0),vec3(255.0))/255.0;
    }
    vec2 atlasUV=(sampleUV+p.atlasAndLightOrigin.xy)/vec2(textureSize(atlas,0));
    pixel=texture(atlas,atlasUV)*vec4(lit,1.0);
    // Upper slope strips are drawn underneath the two-pixel edge strip. Preserve
    // both alpha contributions, including transparent material texels.
    if(overlaps)pixel+=texture(atlas,(underlyingUV+p.atlasAndLightOrigin.xy)/vec2(textureSize(atlas,0)))*vec4(lit,1.0)*(1.0-pixel.a);
}
