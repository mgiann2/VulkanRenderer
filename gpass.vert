#version 450

layout (set = 0, binding = 0) uniform UniformBufferObject {
    mat4 model;
    mat4 view;
    mat4 proj;
} ubo;

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in vec3 inNormal;
layout (location = 3) in vec3 inTangent;

layout (location = 0) out vec2 outTexCoord;
layout (location = 1) out vec4 outPosition;
layout (location = 2) out mat3 outTBN;

void main() {
    outPosition = ubo.proj * ubo.view * ubo.model * vec4(inPosition, 1.0);
    gl_Position = outPosition;
    outTexCoord = inTexCoord;

    vec3 T = normalize(vec3(ubo.model * vec4(inTangent, 0.0)));
    vec3 N = normalize(vec3(ubo.model * vec4(inNormal, 0.0)));
    vec3 B = cross(N, T);
    outTBN = mat3(T, B, N);
}
