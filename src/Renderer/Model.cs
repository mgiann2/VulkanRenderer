using Silk.NET.Assimp;
using Silk.NET.Maths;

namespace Renderer;

public readonly struct Model
{
    public Mesh Mesh { get; init; }
    public Material Material { get; init; }

    public Model(Mesh mesh, Material material)
    {
        Mesh = mesh;
        Material = material;
    }
}

unsafe public partial class VulkanRenderer
{
    public Model LoadModel(string modelPath,
                           string albedoPath,
                           string normalPath,
                           string aoRoughnessMetalnessPath)
    {
        return new Model()
        {
            Mesh = new Mesh(this, modelPath),
            Material = new Material(this, albedoPath, normalPath, aoRoughnessMetalnessPath)
        };
    }

    public void DestroyModel(Model model)
    {
        model.Material.Dispose();
        model.Mesh.Dispose();
    }
}
