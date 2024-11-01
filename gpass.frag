#version 450

layout (set = 0, binding = 1) uniform sampler2D albedoSampler;
layout (set = 0, binding = 2) uniform sampler2D normalSampler;
layout (set = 0, binding = 3) uniform sampler2D metalnessSampler;

layout (location = 0) in vec2 inTexCoord;
layout (location = 1) in vec4 inPosition;

layout (location = 0) out vec4 outAlbedo;
layout (location = 1) out vec4 outNormal;
layout (location = 2) out vec4 outSpecular;
layout (location = 3) out vec4 outPosition;

void main() {
    outAlbedo = texture(albedoSampler, inTexCoord);
    outNormal = texture(normalSampler, inTexCoord);
    outSpecular = texture(metalnessSampler, inTexCoord);
    outPosition = inPosition;
}
