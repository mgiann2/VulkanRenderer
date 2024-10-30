#version 450

layout (binding = 0) uniform sampler2D albedoSampler;
layout (binding = 1) uniform sampler2D normalSampler;
layout (binding = 2) uniform sampler2D specularSampler;
layout (binding = 3) uniform sampler2D positionSampler;

layout(location = 0) in vec2 inTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    // outColor = texture(albedoSampler, inTexCoord);
    outColor = vec4(inTexCoord.x, inTexCoord.y, 0.0f, 1.0f);
}

