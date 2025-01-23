#version 450

layout (location = 0) in vec4 inPosition;

layout (push_constant) uniform PushConstants {
    layout(offset = 64) vec3 pos;
    layout(offset = 76) float farPlane;
} pc;

void main() 
{
    float lightDistance = length(inPosition.xyz - pc.pos);
    lightDistance = lightDistance / pc.farPlane;
    gl_FragDepth = lightDistance;
}
