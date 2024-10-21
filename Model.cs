using Silk.NET.Assimp;
using Silk.NET.Maths;

public readonly struct Model
{
    public VertexBuffer VertexBuffer { get; init; }
    public IndexBuffer IndexBuffer { get; init; }
    public Texture Texture { get; init; }
}

unsafe public partial class VulkanRenderer
{
    public Model LoadModel(string modelPath, string texturePath)
    {
        // load model
        using var assimp = Assimp.GetApi();
        Scene* scene = assimp.ImportFile(modelPath, (uint)PostProcessPreset.TargetRealTimeMaximumQuality);

        var vertexMap = new Dictionary<Vertex, uint>();
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        VisitSceneNode(scene->MRootNode);

        assimp.ReleaseImport(scene);

        // create vertex and index buffers
        var vertexBuffer = CreateVertexBuffer(vertices.ToArray());
        var indexBuffer = CreateIndexBuffer(Array.ConvertAll(indices.ToArray(), val => (ushort)val));

        // create texture
        var texture = CreateTexture(texturePath);

        return new Model()
        {
            VertexBuffer = vertexBuffer,
            IndexBuffer = indexBuffer,
            Texture = texture
        };

        void VisitSceneNode(Node* node)
        {
            for (int m = 0; m < node->MNumMeshes; m++)
            {
                var mesh = scene->MMeshes[node->MMeshes[m]];

                for (int f = 0; f < mesh->MNumFaces; f++)
                {
                    var face = mesh->MFaces[f];

                    for (int i = 0; i < face.MNumIndices; i++)
                    {
                        uint index = face.MIndices[i];

                        var position = mesh->MVertices[index];
                        var texture = mesh->MTextureCoords[0][(int)index];

                        Vertex vertex = new()
                        {
                            pos = new Vector3D<float>(position.X, position.Y, position.Z),
                            color = new Vector3D<float>(1.0f, 1.0f, 1.0f),
                            texCoord = new Vector2D<float>(texture.X, 1.0f - texture.Y)
                        };

                        if (vertexMap.TryGetValue(vertex, out var meshIndex))
                        {
                            indices.Add(meshIndex);
                        }
                        else
                        {
                            indices.Add((uint) vertices.Count);
                            vertexMap[vertex] = (uint)vertices.Count;
                            vertices.Add(vertex);
                        }
                    }
                }
            }

            for (int c = 0; c < node->MNumChildren; c++)
            {
                VisitSceneNode(node->MChildren[c]);
            }
        }
    }

    public void UnloadModel(Model model)
    {
        DestroyTexture(model.Texture);
        DestroyBuffer(model.IndexBuffer);
        DestroyBuffer(model.VertexBuffer);
    }
}
