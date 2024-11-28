#version 450

layout (set = 1, binding = 0) uniform sampler2D albedoSampler;
layout (set = 1, binding = 1) uniform sampler2D normalSampler;
layout (set = 1, binding = 2) uniform sampler2D aoRoughnessMetalnessSampler;
layout (set = 1, binding = 3) uniform sampler2D positionSampler;

layout(location = 0) in vec2 inTexCoord;
layout(location = 1) in vec3 inAmbientColor;
layout(location = 2) in float inAmbientStrength;

layout(location = 0) out vec4 outColor;

void main() {
    vec3 fragColor = texture(albedoSampler, inTexCoord).rgb;
    vec3 fragNorm = normalize(texture(normalSampler, inTexCoord).rgb);
    vec3 fragPos = texture(positionSampler, inTexCoord).rgb;

    // ambient lighting
    vec3 ambient = inAmbientStrength * inAmbientColor;

    vec3 result = ambient * fragColor;
    
    if (result == vec3(0.0)) discard;
    outColor = vec4(result, 1.0);
}

