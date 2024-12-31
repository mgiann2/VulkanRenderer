#version 450

layout (set = 0, binding = 0) uniform SceneInfo {
    mat4 cameraView;
    mat4 cameraProj;
} sceneInfo;

layout (location = 0) in vec3 inPosition;

layout (location = 0) out vec3 outPosition;

void main() {
    outPosition = inPosition;

    mat4 newCameraView = mat4(mat3(sceneInfo.cameraView));
    gl_Position = sceneInfo.cameraProj * newCameraView * vec4(inPosition, 1.0);
}
