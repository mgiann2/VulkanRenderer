#version 450
layout (triangles) in;
layout (triangle_strip, max_vertices=18) out;

layout (set = 0, binding = 0) uniform ShadowInfo {
    mat4 shadowMatrices[6];
} shadowInfo;

layout (location = 0) out vec4 outPosition;

void main()
{
    for (int face = 0; face < 6; ++face)
    {
        gl_Layer = face;
        for (int i = 0; i < 3; ++i)
        {
            outPosition = gl_in[i].gl_Position;
            gl_Position = shadowInfo.shadowMatrices[face] * outPosition;
            EmitVertex();
        }
        EndPrimitive();
    }
}
