#version 450

layout (set = 0, binding = 0) uniform SceneInfo {
    mat4 cameraView;
    mat4 cameraProj;
    vec3 ambientLightColor;
    float ambientLightStrength;
} sceneInfo;

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inTexCoord;

layout(location = 0) out vec2 outTexCoord;
layout(location = 1) out vec3 outAmbientColor;
layout(location = 2) out float outAmbientStrength;

void main() {
    outTexCoord = inTexCoord;
    outAmbientColor = sceneInfo.ambientLightColor;
    outAmbientStrength = sceneInfo.ambientLightStrength;

    gl_Position = vec4(inPos, 1.0f);
}
