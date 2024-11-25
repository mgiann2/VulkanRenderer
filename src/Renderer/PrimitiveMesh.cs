using Silk.NET.Maths;

namespace Renderer;

public static class PrimitiveMesh
{
    public static Mesh CreateQuadMesh(VulkanRenderer renderer)
    {
        var normal = -1.0f * Vector3D<float>.UnitZ;
        var tangent = Vector3D<float>.UnitX;

        Vertex[] vertices = new[] 
        {
            new Vertex() { pos = new Vector3D<float>(-1.0f, -1.0f, 0.0f), texCoord = new Vector2D<float>(0.0f, 0.0f), normal = normal, tangent = tangent }, // top left
            new Vertex() { pos = new Vector3D<float>(1.0f, -1.0f, 0.0f), texCoord = new Vector2D<float>(1.0f, 0.0f), normal = normal, tangent = tangent }, // top right
            new Vertex() { pos = new Vector3D<float>(-1.0f, 1.0f, 0.0f), texCoord = new Vector2D<float>(0.0f, 1.0f), normal = normal, tangent = tangent }, // bottom left
            new Vertex() { pos = new Vector3D<float>(1.0f, 1.0f, 0.0f), texCoord = new Vector2D<float>(1.0f, 1.0f), normal = normal, tangent = tangent }, // bottom right
        };

        ushort[] indices = new ushort[] { 0, 2, 3, 0, 3, 1 };

        var vertexBuffer = renderer.CreateVertexBuffer(vertices);
        var indexBuffer = renderer.CreateIndexBuffer(indices);

        return new Mesh(vertexBuffer, indexBuffer);
    }
}
