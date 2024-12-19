#version 450

layout (location = 0) in vec3 inColor;

layout (location = 0) out vec4 outColor;
layout (location = 1) out vec4 outThresholdColor;

void main() {
    outColor = vec4(inColor, 1.0);

    float brighness = dot(outColor.rgb, vec3(0.2126, 0.7152, 0.0722));
    if (brighness > 1.0)
        outThresholdColor = vec4(outColor.rgb, 1.0);
    else
        outThresholdColor = vec4(0.0, 0.0, 0.0, 1.0);
}
