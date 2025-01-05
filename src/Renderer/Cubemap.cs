using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class Cubemap : IDisposable
{
    private static Vk vk = VulkanHelper.Vk;
    private VulkanRenderer renderer;
    private bool disposedValue;

    public Image CubemapImage { get; }
    public ImageView CubemapImageView { get; }
    public DeviceMemory CubemapMemory { get; }

    public Cubemap(VulkanRenderer renderer, Image cubemapImage, DeviceMemory cubemapMemory)
    {
        this.renderer = renderer;
        CubemapImage = cubemapImage;
        CubemapMemory = cubemapMemory;
        CubemapImageView = VulkanHelper.CreateCubemapImageView(renderer.Device, cubemapImage, Format.R16G16B16A16Sfloat);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(renderer.Device, CubemapImageView, null);
            vk.DestroyImage(renderer.Device, CubemapImage, null);
            vk.FreeMemory(renderer.Device, CubemapMemory, null);
            disposedValue = true;
        }
    }

    ~Cubemap()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
