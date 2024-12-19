#version 450

layout (set = 0, binding = 0) uniform SceneInfo {
    mat4 cameraView;
    mat4 cameraProj;
} sceneInfo;

layout (push_constant) uniform PushConstants
{
    mat4 model;
    vec3 color;
} pc;

layout (location = 0) in vec3 inPosition;

layout (location = 0) out vec3 outColor;

void main() {
    outColor = pc.color;
    gl_Position = sceneInfo.cameraProj * sceneInfo.cameraView * pc.model * vec4(inPosition, 1.0);
}
