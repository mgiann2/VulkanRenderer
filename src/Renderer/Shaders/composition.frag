#version 450

layout (set = 1, binding = 0) uniform sampler2D albedoSampler;
layout (set = 1, binding = 1) uniform sampler2D normalSampler;
layout (set = 1, binding = 2) uniform sampler2D aoRoughnessMetalnessSampler;
layout (set = 1, binding = 3) uniform sampler2D positionSampler;
layout (set = 2, binding = 0) uniform samplerCube irradianceMap;
layout (set = 3, binding = 0) uniform sampler2D directionalShadowMap;

layout (location = 0) in vec3 inCameraPos;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in vec3 inDirectionalLightDir;
layout (location = 3) in vec3 inDirectionalLightColor;
layout (location = 4) in mat4 inLightSpaceMatrix;

layout (location = 0) out vec4 outColor;
layout (location = 1) out vec4 outThresholdColor;

const float PI = 3.14159265359;
const float SURFACE_REFLECTION = 0.04;

float NormalDistribution(vec3 N, vec3 H, float roughness);
float GeometrySchlick(float NdotV, float roughness);
float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness);
vec3 FresnelSchlick(float cosTheta, vec3 F0);
float ShadowCalculation(vec4 fragPosLightSpace, vec3 normal, vec3 lightDir);

void main() {
    // get needed light variables
    vec3 albedo = texture(albedoSampler, inTexCoord).rgb;
    float alpha = texture(albedoSampler, inTexCoord).a;
    vec3 normal = texture(normalSampler, inTexCoord).rgb;
    vec3 pos = texture(positionSampler, inTexCoord).rgb;
    vec3 aoRoughnessMetalness = texture(aoRoughnessMetalnessSampler, inTexCoord).rgb;

    // discard pixel if the alpha is 0 since this implies there is no object to be drawn
    if (alpha == 0.0)
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

    // compute directional light
    vec3 Lo = vec3(0.0);
    {
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
        // kD *= 1.0 - metalness;

        float NdotV = max(dot(N, V), 0.0);
        float NdotL = max(dot(N, L), 0.0);
        vec3 specular = (normalDist * geometry * fresnel) / (4.0 * NdotV * NdotL + 0.0001);

        vec4 fragPosLightSpace = inLightSpaceMatrix * vec4(pos, 1.0);
        float shadow = ShadowCalculation(fragPosLightSpace, N, L);
        Lo += (kD * albedo / PI + specular) * radiance * (1.0 - shadow) * NdotL;
    }

    // compute ambient light
    {
        vec3 irradiance = texture(irradianceMap, N).rgb;

        vec3 kS = FresnelSchlick(max(dot(N, V), 0.0), F0);
        vec3 kD = 1.0 - kS;
        // kD *= 1.0 - metalness;
        vec3 diffuse = irradiance * albedo;
        vec3 ambient = (kD * diffuse) * ao;

        Lo += ambient;
    }

    // compute output light
    outColor = vec4(Lo, 1.0);
    
    float brighness = dot(outColor.rgb, vec3(0.2126, 0.7152, 0.0722));
    if (brighness > 1.0)
        outThresholdColor = vec4(outColor.rgb, 1.0);
    else
        outThresholdColor = vec4(0.0, 0.0, 0.0, 1.0);
}

float NormalDistribution(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom + 0.0001;

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

float ShadowCalculation(vec4 fragPosLightSpace, vec3 normal, vec3 lightDir)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords.xy = projCoords.xy * 0.5 + 0.5;
    float currentDepth = projCoords.z; 

    // PCF
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(directionalShadowMap, 0);
    for (int x = -1; x <= 1; ++x)
    {
        for (int y = -1; y <= 1; ++y)
        {
            float pcfDepth = texture(directionalShadowMap, projCoords.xy + vec2(x, y) * texelSize).r;
            shadow += currentDepth > pcfDepth ? 1.0 : 0.0;
        }
    }
    shadow /= 9.0;

    if (currentDepth > 1.0)
        shadow = 0.0;

    return shadow;
}
