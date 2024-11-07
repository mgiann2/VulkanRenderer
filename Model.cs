using Silk.NET.Assimp;
using Silk.NET.Maths;

public readonly struct Model
{
    public VertexBuffer VertexBuffer { get; init; }
    public IndexBuffer IndexBuffer { get; init; }
    public Material Material { get; init; }
}

public readonly struct Mesh
{
    public VertexBuffer VertexBuffer { get; init; }
    public IndexBuffer IndexBuffer { get; init; }
}

unsafe public partial class VulkanRenderer
{
    public Model LoadModel(string modelPath,
                           string albedoPath,
                           string normalPath,
                           string metalnessPath)
    {
        // load model
        uint postProcessSteps = (uint) (PostProcessSteps.FlipUVs | PostProcessSteps.Triangulate | PostProcessSteps.CalculateTangentSpace);
        using var assimp = Assimp.GetApi();
        Scene* scene = assimp.ImportFile(modelPath, postProcessSteps);

        var vertexMap = new Dictionary<Vertex, uint>();
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        VisitSceneNode(scene->MRootNode);

        assimp.ReleaseImport(scene);

        // create vertex and index buffers
        var vertexBuffer = CreateVertexBuffer(vertices.ToArray());
        var indexBuffer = CreateIndexBuffer(Array.ConvertAll(indices.ToArray(), val => (ushort)val));

        // create texture
        var material = CreateMaterial(albedoPath, normalPath, metalnessPath);

        return new Model()
        {
            VertexBuffer = vertexBuffer,
            IndexBuffer = indexBuffer,
            Material = material
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
                        var texCoord = mesh->MTextureCoords[0][(int)index];
                        var normal = mesh->MNormals[index];
                        var tangent = mesh->MTangents[index];

                        Vertex vertex = new()
                        {
                            pos = new Vector3D<float>(position.X, position.Y, position.Z),
                            texCoord = new Vector2D<float>(texCoord.X, texCoord.Y),
                            normal = new Vector3D<float>(normal.X, normal.Y, normal.Z),
                            tangent = new Vector3D<float>(tangent.X, tangent.Y, tangent.Z)
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

    public Mesh LoadMesh(string meshPath)
    {
        // load model
        uint postProcessSteps = (uint) (PostProcessSteps.FlipUVs | PostProcessSteps.Triangulate | PostProcessSteps.CalculateTangentSpace);
        using var assimp = Assimp.GetApi();
        Scene* scene = assimp.ImportFile(meshPath, postProcessSteps);

        var vertexMap = new Dictionary<Vertex, uint>();
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        VisitSceneNode(scene->MRootNode);

        assimp.ReleaseImport(scene);

        // create vertex and index buffers
        var vertexBuffer = CreateVertexBuffer(vertices.ToArray());
        var indexBuffer = CreateIndexBuffer(Array.ConvertAll(indices.ToArray(), val => (ushort)val));

        return new Mesh()
        {
            VertexBuffer = vertexBuffer,
            IndexBuffer = indexBuffer,
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
                        var texCoord = mesh->MTextureCoords[0][(int)index];
                        var normal = mesh->MNormals[index];
                        var tangent = mesh->MTangents[index];

                        Vertex vertex = new()
                        {
                            pos = new Vector3D<float>(position.X, position.Y, position.Z),
                            texCoord = new Vector2D<float>(texCoord.X, texCoord.Y),
                            normal = new Vector3D<float>(normal.X, normal.Y, normal.Z),
                            tangent = new Vector3D<float>(tangent.X, tangent.Y, tangent.Z)
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

    public void DrawModel(Model model)
    {
        Bind(model.VertexBuffer, geometryCommandBuffers[currentFrame]);
        Bind(model.IndexBuffer, geometryCommandBuffers[currentFrame]);
        BindMaterial(model.Material);

        vk.CmdDrawIndexed(geometryCommandBuffers[currentFrame], model.IndexBuffer.IndexCount, 1, 0, 0, 0);
    }

    public void UnloadModel(Model model)
    {
        DestroyMaterial(model.Material);
        DestroyBuffer(model.IndexBuffer);
        DestroyBuffer(model.VertexBuffer);
    }

    public void UnloadMesh(Mesh mesh)
    {
        DestroyBuffer(mesh.VertexBuffer);
        DestroyBuffer(mesh.IndexBuffer);
    }
}
