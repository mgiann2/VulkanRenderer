#version 450

layout (set = 0, binding = 0) uniform sampler2D albedoSampler;
layout (set = 0, binding = 1) uniform sampler2D normalSampler;
layout (set = 0, binding = 2) uniform sampler2D specularSampler;
layout (set = 0, binding = 3) uniform sampler2D positionSampler;

layout(location = 0) in vec2 inTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    vec3 color = texture(albedoSampler, inTexCoord).rgb;
    vec3 fragNorm = normalize(texture(normalSampler, inTexCoord).rgb);
    vec3 fragPos = texture(positionSampler, inTexCoord).rgb;

    // hardcoded light information
    float ambientStrength = 0.1f;
    vec3 lightColor = vec3(1.0, 1.0, 1.0);
    vec3 lightPos = vec3(-2.0, 2.0, -2.0);
    vec3 viewPos = vec3(0.0, 0.5, -3.0);

    // ambient lighting
    vec3 ambient = ambientStrength * lightColor;

    // diffuse lighting
    vec3 lightDir = normalize(lightPos - fragPos);
    float diff = max(dot(fragNorm, lightDir), 0.0);
    vec3 diffuse = diff * lightColor;

    // specular lighting
    float specularStrength = 0.5;
    vec3 viewDir = normalize(viewPos - fragPos);
    vec3 reflectDir = reflect(-lightDir, fragNorm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    vec3 specular = specularStrength * spec * lightColor;

    vec3 result = (ambient + diffuse + specular) * color;

    outColor = vec4(result, 1.0);
}

