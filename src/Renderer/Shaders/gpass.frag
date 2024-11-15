#version 450

layout (set = 1, binding = 0) uniform sampler2D albedoSampler;
layout (set = 1, binding = 1) uniform sampler2D normalSampler;
layout (set = 1, binding = 2) uniform sampler2D metalnessSampler;

layout (location = 0) in vec2 inTexCoord;
layout (location = 1) in vec4 inPosition;
layout (location = 2) in mat3 inTBN;

layout (location = 0) out vec4 outAlbedo;
layout (location = 1) out vec4 outNormal;
layout (location = 2) out vec4 outSpecular;
layout (location = 3) out vec4 outPosition;

void main() {
    outAlbedo = texture(albedoSampler, inTexCoord);

    vec3 normal = texture(normalSampler, inTexCoord).rgb;
    normal = normal * 2.0 - 1.0;
    outNormal = vec4(normalize(inTBN * normal), 0.0);

    outSpecular = texture(metalnessSampler, inTexCoord);
    outPosition = inPosition;
}
