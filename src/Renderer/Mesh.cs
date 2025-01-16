using Silk.NET.Assimp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class Mesh : IDisposable
{
    private VertexBuffer vertexBuffer;
    private IndexBuffer indexBuffer;

    private Vk vk = VulkanHelper.Vk;
    private bool disposedValue;

    public Mesh(VertexBuffer vertexBuffer, IndexBuffer indexBuffer)
    {
        this.vertexBuffer = vertexBuffer;
        this.indexBuffer = indexBuffer;
    }

    public Mesh(VulkanRenderer renderer, string meshPath)
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
        vertexBuffer = new VertexBuffer(renderer.SCDevice, vertices.ToArray());
        indexBuffer = new IndexBuffer(renderer.SCDevice, Array.ConvertAll(indices.ToArray(), val => (ushort)val));

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

    public void Bind(CommandBuffer commandBuffer)
    {
        vertexBuffer.Bind(commandBuffer);
        indexBuffer.Bind(commandBuffer);
    }

    public void Draw(CommandBuffer commandBuffer)
    {
        vk.CmdDrawIndexed(commandBuffer, indexBuffer.IndexCount, 1, 0, 0, 0);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vertexBuffer.Dispose();
            indexBuffer.Dispose();

            disposedValue = true;
        }
    }

    ~Mesh()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
