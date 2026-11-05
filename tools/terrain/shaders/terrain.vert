#version 450
layout(location=0) in vec2 position;
layout(location=1) in vec2 textureUV;
layout(location=2) in vec2 lightUV;
layout(location=3) in vec4 tint;
layout(set=1,binding=0,std140) uniform VertexData { mat4 transform; } uniforms;
layout(location=0) out vec2 uv;
layout(location=1) out vec2 illuminationUV;
layout(location=2) out vec4 color;
void main() {
    gl_Position=uniforms.transform*vec4(position,0.0,1.0);
    uv=textureUV; illuminationUV=lightUV; color=tint;
}
