#version 450

layout (set = 1, binding = 0) uniform samplerCube skyboxCubeSampler;

layout (location = 0) in vec3 inPosition;
layout (location = 0) out vec4 outColor;

void main()
{
    vec3 color = texture(skyboxCubeSampler, inPosition).rgb;
    outColor = vec4(color, 1.0);
}
