#version 450

layout (set = 0, binding = 0) uniform SceneInfo {
    mat4 cameraView;
    mat4 cameraProj;
    vec3 cameraPos;
    vec3 directionalLightDirection;
    vec3 directionalLightColor;
    mat4 lightSpaceMatrix;
} sceneInfo;

layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inTexCoord;

layout (location = 0) out vec3 outCameraPos;
layout (location = 1) out vec2 outTexCoord;
layout (location = 2) out vec3 outDirectionalLightDir;
layout (location = 3) out vec3 outDirectionalLightColor;
layout (location = 4) out mat4 outLightSpaceMatrix;

void main() {
    outCameraPos = sceneInfo.cameraPos;
    outTexCoord = inTexCoord;
    outDirectionalLightDir = sceneInfo.directionalLightDirection;
    outDirectionalLightColor = sceneInfo.directionalLightColor;
    outLightSpaceMatrix = sceneInfo.lightSpaceMatrix;

    gl_Position = vec4(inPos, 1.0);
}
