#version 450
layout(set=2,binding=0) uniform sampler2D atlas;
layout(set=2,binding=1) uniform sampler2D lighting;
layout(set=3,binding=0,std140) uniform FragmentData { vec4 lightRect; vec4 options; vec4 tint; vec4 reserved; } uniforms;
layout(location=0) in vec2 uv;
layout(location=1) in vec2 illuminationUV;
layout(location=2) in vec4 color;
layout(location=0) out vec4 pixel;
void main() { pixel=texture(atlas,uv)*texture(lighting,illuminationUV)*color*uniforms.tint; }
