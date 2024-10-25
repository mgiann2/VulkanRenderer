#version 450

layout (binding = 1) uniform sampler2D albedoSampler;
layout (binding = 2) uniform sampler2D normalSampler;
layout (binding = 3) uniform sampler2D metalnessSampler;

layout (location = 0) in vec2 inTexCoord;

layout (location = 0) out vec4 outAlbedo;
layout (location = 1) out vec3 outNormal;
layout (location = 2) out vec3 outSpecular;

void main() {
    outAlbedo = texture(albedoSampler, inTexCoord);
    outNormal = texture(normalSampler, inTexCoord).rgb;
    outSpecular = texture(metalnessSampler, inTexCoord).rgb;
}
