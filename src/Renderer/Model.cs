namespace Renderer;

public readonly struct Model
{
    public Mesh Mesh { get; }
    public Material Material { get; }

    public Model(Mesh mesh, Material material)
    {
        Mesh = mesh;
        Material = material;
    }

    public Model(VulkanRenderer renderer,
                 string modelPath,
                 string albedoPath,
                 string normalPath,
                 string aoRoughnessMetalnessPath)
    {
        Mesh = new Mesh(renderer, modelPath);
        Material = new Material(renderer, albedoPath, normalPath, aoRoughnessMetalnessPath);
    }
}
