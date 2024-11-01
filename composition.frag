#version 450

layout (set = 0, binding = 0) uniform sampler2D albedoSampler;
layout (set = 0, binding = 1) uniform sampler2D normalSampler;
layout (set = 0, binding = 2) uniform sampler2D specularSampler;
layout (set = 0, binding = 3) uniform sampler2D positionSampler;

layout(location = 0) in vec2 inTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = texture(normalSampler, inTexCoord);
}

