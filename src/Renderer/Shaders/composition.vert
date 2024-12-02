#version 450

layout (set = 0, binding = 0) uniform SceneInfo {
    mat4 cameraView;
    mat4 cameraProj;
    vec3 ambientLightColor;
    float ambientLightStrength;
    vec3 directionalLightDirection;
    vec3 directionalLightColor;
} sceneInfo;

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inTexCoord;

layout (location = 0) out vec3 outCameraPos;
layout (location = 1) out vec2 outTexCoord;
layout (location = 2) out vec3 outAmbientColor;
layout (location = 3) out float outAmbientStrength;
layout (location = 4) out vec3 outDirectionalLightDir;
layout (location = 5) out vec3 outDirectionalLightColor;

void main() {
    outCameraPos = sceneInfo.cameraView[3].xyz;
    outTexCoord = inTexCoord;
    outAmbientColor = sceneInfo.ambientLightColor;
    outAmbientStrength = sceneInfo.ambientLightStrength;
    outDirectionalLightDir = sceneInfo.directionalLightDirection;
    outDirectionalLightColor = sceneInfo.directionalLightColor;

    gl_Position = vec4(inPos, 1.0f);
}
