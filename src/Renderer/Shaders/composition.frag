#version 450

layout (set = 1, binding = 0) uniform sampler2D albedoSampler;
layout (set = 1, binding = 1) uniform sampler2D normalSampler;
layout (set = 1, binding = 2) uniform sampler2D aoRoughnessMetalnessSampler;
layout (set = 1, binding = 3) uniform sampler2D positionSampler;

layout (location = 0) in vec3 inCameraPos;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in vec3 inAmbientColor;
layout (location = 3) in float inAmbientStrength;
layout (location = 4) in vec3 inDirectionalLightDir;
layout (location = 5) in vec3 inDirectionalLightColor;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;
const float SURFACE_REFLECTION = 0.04;

float NormalDistribution(vec3 N, vec3 H, float roughness);
float GeometrySchlick(float NdotV, float roughness);
float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness);
vec3 FresnelSchlick(float cosTheta, vec3 F0);

void main() {
    // get needed light variables
    vec3 albedo = texture(albedoSampler, inTexCoord).rgb;
    vec3 normal = texture(normalSampler, inTexCoord).rgb;
    vec3 pos = texture(positionSampler, inTexCoord).rgb;
    vec3 aoRoughnessMetalness = texture(aoRoughnessMetalnessSampler, inTexCoord).rgb;

    // TODO: bug since all black color will be transparent
    // discard pixel if there is no color since this implies there is no object to be drawn
    if (albedo == vec3(0.0))
    {
        discard;
        return;
    }

    float ao = aoRoughnessMetalness.r;
    float roughness = aoRoughnessMetalness.g;
    float metalness = aoRoughnessMetalness.b;

    vec3 F0 = vec3(SURFACE_REFLECTION);
    F0 = mix(F0, albedo, metalness);

    vec3 N = normalize(normal);
    vec3 V = normalize(inCameraPos - pos);
    vec3 L = normalize(-inDirectionalLightDir);
    vec3 H = normalize(V + L);

    // radiance
    vec3 radiance = inDirectionalLightColor;

    // cook-torrence BRDF
    float normalDist = NormalDistribution(N, H, roughness);
    float geometry = GeometrySmith(N, V, L, roughness);
    vec3 fresnel = FresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 kS = fresnel;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metalness;

    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    vec3 specular = (normalDist * geometry * fresnel) / (4.0 * NdotV * NdotL + 0.0001);

    // compute output light
    vec3 outLight = (kD * albedo / PI + specular) * radiance * NdotL;
    vec3 ambient = albedo * ao * inAmbientStrength * inAmbientColor;

    outColor = vec4(ambient + outLight, 1.0);
}

float NormalDistribution(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}

float GeometrySchlick(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float g1 = GeometrySchlick(NdotV, roughness);
    float g2 = GeometrySchlick(NdotL, roughness);

    return g1 * g2;
}

vec3 FresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}
