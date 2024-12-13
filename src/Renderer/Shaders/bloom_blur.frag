#version 450

layout (set = 0, binding = 0) uniform sampler2D albedoSampler;

layout(location = 0) in vec2 inTexCoord;

layout(location = 0) out vec4 outColor;

layout (push_constant) uniform PushConstants
{
    bool horizontal;
} pc;

void main() {
    float weight[5] = float[] (0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);
    vec2 texOffset = 1.0 / textureSize(albedoSampler, 0);
    vec3 color = texture(albedoSampler, inTexCoord).rgb * weight[0]; // apply weight to center pixel

    if (pc.horizontal) {
        for (int i = 1; i < 5; i++) {
            color += texture(albedoSampler, inTexCoord + vec2(texOffset.x * i, 0.0)).rgb * weight[i];
            color += texture(albedoSampler, inTexCoord - vec2(texOffset.x * i, 0.0)).rgb * weight[i];
        }
    }
    else {
        for (int i = 1; i < 5; i++) {
            color += texture(albedoSampler, inTexCoord + vec2(0.0, texOffset.y * i)).rgb * weight[i];
            color += texture(albedoSampler, inTexCoord - vec2(0.0, texOffset.y * i)).rgb * weight[i];
        }
    }

    outColor = vec4(color, 1.0);
}

