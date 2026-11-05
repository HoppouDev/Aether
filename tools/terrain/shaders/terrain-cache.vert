#version 450
layout(location=0) in vec2 position;
layout(location=1) in vec2 textureUV;
layout(location=2) in vec2 lightUV;
layout(location=3) in vec4 descriptor;
layout(set=1,binding=0,std140) uniform VertexData { mat4 transform; } p;
layout(location=0) out vec2 uv;
layout(location=1) out vec2 illuminationUV;
layout(location=2) flat out uvec4 appearance;
void main() {
    gl_Position=p.transform*vec4(position,0.0,1.0);
    uv=textureUV; illuminationUV=lightUV;
    appearance=uvec4(round(descriptor*255.0));
}
