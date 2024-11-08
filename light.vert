#version 450

layout(location = 0) in vec3 inPos;

layout(location = 0) out vec2 outTexCoord;

void main() {
    outTexCoord = (inPos.xy + 1) / 2;
    gl_Position = vec4(inPos, 1.0f);
}
