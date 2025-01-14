#version 450

layout (set = 0, binding = 0) uniform ShadowInfo {
    mat4 lightSpaceMatrix;
} shadowInfo;

layout (push_constant) uniform PushConstants
{
    mat4 model;
} pc;

layout (location = 0) in vec3 inPosition;

void main()
{
    gl_Position = shadowInfo.lightSpaceMatrix * pc.model * vec4(inPosition, 1.0);
}
