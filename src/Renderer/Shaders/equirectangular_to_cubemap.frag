#version 450

layout (set = 1, binding = 0) uniform sampler2D skyboxSampler;

layout (location = 0) in vec3 inPosition;
layout (location = 0) out vec4 outColor;

const vec2 invAtan = vec2(0.1591, 0.3183);
vec2 SampleSphericalMap(vec3 skyboxPos)
{
    vec2 uv = vec2(atan(skyboxPos.z, skyboxPos.x), asin(skyboxPos.y));
    uv *= invAtan;
    uv += 0.5;
    return uv;
}

void main()
{
    vec2 uv = SampleSphericalMap(normalize(inPosition));
    vec3 color = texture(skyboxSampler, uv).rgb;

    outColor = vec4(color, 1.0);
}
