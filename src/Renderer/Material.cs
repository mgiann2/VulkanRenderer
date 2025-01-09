using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class Material : IDisposable
{
    private static Vk vk = VulkanHelper.Vk;
    private VulkanRenderer renderer;
    private bool disposedValue;

    public Texture Albedo { get; init; }
    public Texture Normal { get; init; }
    public Texture AORoughnessMetalness { get; init; }
    public DescriptorSet[] DescriptorSets { get; init; }

    public Material(VulkanRenderer renderer, string albedoPath, string normalPath, string aoRoughnessMetalnessPath)
    {
        this.renderer = renderer;

        Albedo = new Texture(renderer, albedoPath);
        Normal = new Texture(renderer, normalPath, isNormal: true);
        AORoughnessMetalness = new Texture(renderer, aoRoughnessMetalnessPath);

        DescriptorSets = renderer.CreateMaterialInfoDescriptorSets(Albedo.TextureImageView,
                                                          Normal.TextureImageView,
                                                          AORoughnessMetalness.TextureImageView);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            fixed (DescriptorSet* descriptorSetsPtr = DescriptorSets)
            {
                vk.FreeDescriptorSets(renderer.SCDevice.LogicalDevice, renderer.materialInfoDescriptorPool,
                        (uint) DescriptorSets.Length, descriptorSetsPtr);
            }

            Albedo.Dispose();
            Normal.Dispose();
            AORoughnessMetalness.Dispose();

            disposedValue = true;
        }
    }

    ~Material()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
