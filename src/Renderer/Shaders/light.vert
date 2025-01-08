#version 450

layout (set = 0, binding = 0) uniform SceneInfo {
    mat4 cameraView;
    mat4 cameraProj;
    vec3 cameraPos;
} sceneInfo;

layout (push_constant) uniform LightData {
    mat4 model;
    vec3 pos;
    vec3 color;
} light;

layout (location = 0) in vec3 inPos;

layout (location = 0) out vec3 outLightColor;
layout (location = 1) out vec3 outLightPos;
layout (location = 2) out vec3 outCameraPos;

void main() {
    outLightColor = light.color.rgb;
    outLightPos = light.pos.xyz;
    outCameraPos = sceneInfo.cameraPos;

    gl_Position = sceneInfo.cameraProj * sceneInfo.cameraView * light.model * vec4(inPos, 1.0f);
}
