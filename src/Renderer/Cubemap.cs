using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class Cubemap : IDisposable
{
    private static Vk vk = VulkanHelper.Vk;
    private SCDevice scDevice;
    private bool disposedValue;

    public Image CubemapImage { get; }
    public ImageView CubemapImageView { get; }
    public DeviceMemory CubemapMemory { get; }

    public Cubemap(SCDevice scDevice, Image cubemapImage, DeviceMemory cubemapMemory)
    {
        this.scDevice = scDevice;
        CubemapImage = cubemapImage;
        CubemapMemory = cubemapMemory;
        CubemapImageView = VulkanHelper.CreateCubemapImageView(this.scDevice, cubemapImage, Format.R16G16B16A16Sfloat);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(scDevice.LogicalDevice, CubemapImageView, null);
            vk.DestroyImage(scDevice.LogicalDevice, CubemapImage, null);
            vk.FreeMemory(scDevice.LogicalDevice, CubemapMemory, null);
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
