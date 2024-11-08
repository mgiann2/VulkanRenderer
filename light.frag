#version 450

layout (set = 0, binding = 0) uniform sampler2D albedoSampler;
layout (set = 0, binding = 1) uniform sampler2D normalSampler;
layout (set = 0, binding = 2) uniform sampler2D specularSampler;
layout (set = 0, binding = 3) uniform sampler2D positionSampler;

layout (push_constant) uniform LightData
{
    vec3 pos;
    vec3 color;
} light;

layout(location = 0) in vec2 inTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    vec3 color = texture(albedoSampler, inTexCoord).rgb;
    vec3 fragNorm = normalize(texture(normalSampler, inTexCoord).rgb);
    vec3 fragPos = texture(positionSampler, inTexCoord).rgb;

    // diffuse lighting
    vec3 lightDir = normalize(light.pos - fragPos);
    float diff = max(dot(fragNorm, lightDir), 0.0);
    vec3 diffuse = diff * light.color;

    outColor = vec4(diffuse * color, 1.0);
}

