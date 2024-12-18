using Silk.NET.Maths;

namespace Renderer;

public static class PrimitiveMesh
{
    private static readonly Vector3D<float>[] cubePositions =
    {
        new Vector3D<float>(-0.5f, 0.5f, -0.5f),
        new Vector3D<float>(-0.5f, -0.5f, -0.5f),
        new Vector3D<float>(0.5f, 0.5f, -0.5f),
        new Vector3D<float>(0.5f, -0.5f, -0.5f),
        new Vector3D<float>(-0.5f, 0.5f, 0.5f),
        new Vector3D<float>(-0.5f, -0.5f, 0.5f),
        new Vector3D<float>(0.5f, 0.5f, 0.5f),
        new Vector3D<float>(0.5f, -0.5f, 0.5f)
    };

    private static readonly Vector2D<float>[] cubeTexCoords =
    {
        new Vector2D<float>(1.0f, 0.0f),
        new Vector2D<float>(1.0f, 1.0f),
        new Vector2D<float>(0.0f, 0.0f),
        new Vector2D<float>(0.0f, 1.0f),
    };

    private static readonly Vector3D<float>[] cubeNormals =
    {
        -Vector3D<float>.UnitZ,
        -Vector3D<float>.UnitX,
        Vector3D<float>.UnitX,
        Vector3D<float>.UnitY,
        -Vector3D<float>.UnitY,
        Vector3D<float>.UnitZ
    };

    private static readonly Vector3D<float>[] cubeTangents =
    {
        Vector3D<float>.UnitY,
        -Vector3D<float>.UnitZ,
        -Vector3D<float>.UnitZ,
        -Vector3D<float>.UnitX,
        -Vector3D<float>.UnitX,
        -Vector3D<float>.UnitY
    };

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

    public static Mesh CreateCubeMesh(VulkanRenderer renderer)
    {
        Vertex[] vertices = new[]
        {
            new Vertex{ pos = cubePositions[0], texCoord = cubeTexCoords[0], normal = cubeNormals[0], tangent = cubeTangents[0] },
            new Vertex{ pos = cubePositions[3], texCoord = cubeTexCoords[3], normal = cubeNormals[0], tangent = cubeTangents[0] },
            new Vertex{ pos = cubePositions[1], texCoord = cubeTexCoords[1], normal = cubeNormals[0], tangent = cubeTangents[0] },
            new Vertex{ pos = cubePositions[0], texCoord = cubeTexCoords[0], normal = cubeNormals[0], tangent = cubeTangents[0] },
            new Vertex{ pos = cubePositions[2], texCoord = cubeTexCoords[2], normal = cubeNormals[0], tangent = cubeTangents[0] },
            new Vertex{ pos = cubePositions[3], texCoord = cubeTexCoords[3], normal = cubeNormals[0], tangent = cubeTangents[0] },

            new Vertex{ pos = cubePositions[2], texCoord = cubeTexCoords[0], normal = cubeNormals[1], tangent = cubeTangents[1] },
            new Vertex{ pos = cubePositions[7], texCoord = cubeTexCoords[3], normal = cubeNormals[1], tangent = cubeTangents[1] },
            new Vertex{ pos = cubePositions[3], texCoord = cubeTexCoords[1], normal = cubeNormals[1], tangent = cubeTangents[1] },
            new Vertex{ pos = cubePositions[2], texCoord = cubeTexCoords[0], normal = cubeNormals[1], tangent = cubeTangents[1] },
            new Vertex{ pos = cubePositions[6], texCoord = cubeTexCoords[2], normal = cubeNormals[1], tangent = cubeTangents[1] },
            new Vertex{ pos = cubePositions[7], texCoord = cubeTexCoords[3], normal = cubeNormals[1], tangent = cubeTangents[1] },

            new Vertex{ pos = cubePositions[4], texCoord = cubeTexCoords[0], normal = cubeNormals[2], tangent = cubeTangents[2] },
            new Vertex{ pos = cubePositions[1], texCoord = cubeTexCoords[3], normal = cubeNormals[2], tangent = cubeTangents[2] },
            new Vertex{ pos = cubePositions[5], texCoord = cubeTexCoords[1], normal = cubeNormals[2], tangent = cubeTangents[2] },
            new Vertex{ pos = cubePositions[4], texCoord = cubeTexCoords[0], normal = cubeNormals[2], tangent = cubeTangents[2] },
            new Vertex{ pos = cubePositions[0], texCoord = cubeTexCoords[2], normal = cubeNormals[2], tangent = cubeTangents[2] },
            new Vertex{ pos = cubePositions[1], texCoord = cubeTexCoords[3], normal = cubeNormals[2], tangent = cubeTangents[2] },

            new Vertex{ pos = cubePositions[4], texCoord = cubeTexCoords[0], normal = cubeNormals[3], tangent = cubeTangents[3] },
            new Vertex{ pos = cubePositions[2], texCoord = cubeTexCoords[3], normal = cubeNormals[3], tangent = cubeTangents[3] },
            new Vertex{ pos = cubePositions[0], texCoord = cubeTexCoords[1], normal = cubeNormals[3], tangent = cubeTangents[3] },
            new Vertex{ pos = cubePositions[4], texCoord = cubeTexCoords[0], normal = cubeNormals[3], tangent = cubeTangents[3] },
            new Vertex{ pos = cubePositions[6], texCoord = cubeTexCoords[2], normal = cubeNormals[3], tangent = cubeTangents[3] },
            new Vertex{ pos = cubePositions[2], texCoord = cubeTexCoords[3], normal = cubeNormals[3], tangent = cubeTangents[3] },

            new Vertex{ pos = cubePositions[1], texCoord = cubeTexCoords[0], normal = cubeNormals[4], tangent = cubeTangents[4] },
            new Vertex{ pos = cubePositions[7], texCoord = cubeTexCoords[3], normal = cubeNormals[4], tangent = cubeTangents[4] },
            new Vertex{ pos = cubePositions[5], texCoord = cubeTexCoords[1], normal = cubeNormals[4], tangent = cubeTangents[4] },
            new Vertex{ pos = cubePositions[1], texCoord = cubeTexCoords[0], normal = cubeNormals[4], tangent = cubeTangents[4] },
            new Vertex{ pos = cubePositions[3], texCoord = cubeTexCoords[2], normal = cubeNormals[4], tangent = cubeTangents[4] },
            new Vertex{ pos = cubePositions[7], texCoord = cubeTexCoords[3], normal = cubeNormals[4], tangent = cubeTangents[4] },

            new Vertex{ pos = cubePositions[6], texCoord = cubeTexCoords[0], normal = cubeNormals[5], tangent = cubeTangents[5] },
            new Vertex{ pos = cubePositions[5], texCoord = cubeTexCoords[3], normal = cubeNormals[5], tangent = cubeTangents[5] },
            new Vertex{ pos = cubePositions[7], texCoord = cubeTexCoords[1], normal = cubeNormals[5], tangent = cubeTangents[5] },
            new Vertex{ pos = cubePositions[6], texCoord = cubeTexCoords[0], normal = cubeNormals[5], tangent = cubeTangents[5] },
            new Vertex{ pos = cubePositions[4], texCoord = cubeTexCoords[2], normal = cubeNormals[5], tangent = cubeTangents[5] },
            new Vertex{ pos = cubePositions[5], texCoord = cubeTexCoords[3], normal = cubeNormals[5], tangent = cubeTangents[5] },
        };

        for (int i = 0; i < 6; i++)
        {
            var pos1 = vertices[i*6].pos;
            var pos2 = vertices[i*6+1].pos;
            var pos3 = vertices[i*6+2].pos;
            var pos4 = vertices[i*6+4].pos;

            var uv1 = vertices[i*6].texCoord;
            var uv2 = vertices[i*6+1].texCoord;
            var uv3 = vertices[i*6+2].texCoord;
            var uv4 = vertices[i*6+4].texCoord;

            var norm = vertices[i*6].normal;

            Vector3D<float> edge1 = pos2 - pos1;
            Vector3D<float> edge2 = pos3 - pos1;
            Vector2D<float> deltaUV1 = uv2 - uv1;
            Vector2D<float> deltaUV2 = uv3 - uv1;

            float f = 1.0f / (deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y);
            for (int j = 0; j < 6; j++)
            {
                vertices[i*6+j].tangent.X = f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X);
                vertices[i*6+j].tangent.Y = f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y);
                vertices[i*6+j].tangent.Z = f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z);
            }
        }

        ushort[] indices = new ushort[36];
        for (ushort i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        var vertexBuffer = renderer.CreateVertexBuffer(vertices);
        var indexBuffer = renderer.CreateIndexBuffer(indices);

        return new Mesh(vertexBuffer, indexBuffer);
    }
}
