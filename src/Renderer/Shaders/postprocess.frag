#version 450

layout (set = 0, binding = 0) uniform sampler2D albedoSampler;
layout (set = 1, binding = 0) uniform sampler2D bloomSampler;

layout(location = 0) in vec2 inTexCoord;

layout(location = 0) out vec4 outColor;

// ACES function used from https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
vec3 ACESFilmicTonemap(vec3 color) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((color*(a*color+b))/(color*(c*color+d)+e), 0.0, 1.0);
}

void main() {
    vec3 hdrColor = texture(albedoSampler, inTexCoord).rgb;
    vec3 bloomColor = texture(bloomSampler, inTexCoord).rgb;

    vec3 combinedColor = hdrColor + bloomColor;

    // apply ACES filmic tonemapping
    vec3 mappedColor = ACESFilmicTonemap(combinedColor);

    // gamma correction
    const float gamma = 2.2;
    vec3 gammaCorrectedColor = pow(mappedColor, vec3(1.0 / gamma));

    outColor = vec4(gammaCorrectedColor, 1.0);
}

