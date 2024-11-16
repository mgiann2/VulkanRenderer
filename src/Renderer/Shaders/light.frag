#version 450

layout (set = 1, binding = 0) uniform sampler2D albedoSampler;
layout (set = 1, binding = 1) uniform sampler2D normalSampler;
layout (set = 1, binding = 2) uniform sampler2D aoRoughnessMetalnessSampler;
layout (set = 1, binding = 3) uniform sampler2D positionSampler;

layout(location = 0) in vec3 inLightColor;
layout(location = 1) in vec3 inLightPos;

layout(location = 0) out vec4 outColor;

void main() {
    vec2 texCoord = vec2(gl_FragCoord.x / 800.0, gl_FragCoord.y / 600.0);
    vec3 color = texture(albedoSampler, texCoord).rgb;
    vec3 fragNorm = normalize(texture(normalSampler, texCoord).rgb);
    vec3 fragPos = texture(positionSampler, texCoord).rgb;

    // diffuse lighting
    float distance = length(inLightPos - fragPos);
    float attenuation = 1.0 / (distance * distance);
    vec3 lightDir = normalize(inLightPos - fragPos);
    float diff = max(dot(fragNorm, lightDir), 0.0);
    vec3 diffuse = inLightColor * attenuation;

    outColor = vec4(color * diffuse, 1.0);
}

