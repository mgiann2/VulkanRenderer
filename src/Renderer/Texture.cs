using Silk.NET.Vulkan;
using StbiSharp;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Renderer;

unsafe public class Texture : IDisposable
{
    private static Vk vk = VulkanHelper.Vk;
    private SCDevice scDevice;
    private bool disposedValue;

    public Image TextureImage { get; }
    public DeviceMemory TextureImageMemory { get; }
    public ImageView TextureImageView { get; }

    public Texture(SCDevice scDevice, string filepath, bool isNormal = false)
    {
        this.scDevice = scDevice;

        Format format = isNormal ? Format.R8G8B8A8Unorm : Format.R8G8B8A8Srgb;

        using (var stream = File.OpenRead(filepath))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            (TextureImage, TextureImageMemory) = CreateTextureImage(memoryStream, scDevice, format);
        }

        TextureImageView = CreateTextureImageView(TextureImage, scDevice, format);
    }

    (Image, DeviceMemory) CreateTextureImage(MemoryStream memoryStream, SCDevice scDevice, Format format)
    {
        var image = Stbi.LoadFromMemory(memoryStream, 4);

        ulong imageSize = (ulong)(image.Width * image.Height * 4);

        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(scDevice, imageSize, BufferUsageFlags.TransferSrcBit,
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(scDevice.LogicalDevice, stagingBufferMemory, 0, imageSize, 0, &data);
        image.Data.CopyTo(new Span<byte>(data, (int)imageSize));
        vk.UnmapMemory(scDevice.LogicalDevice, stagingBufferMemory);

        (var textureImage, var textureImageMemory) = VulkanHelper.CreateImage(scDevice, 
                (uint)image.Width, (uint)image.Height, format, ImageTiling.Optimal,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);

        scDevice.TransitionImageLayout(textureImage, format, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        scDevice.CopyBufferToImage(stagingBuffer, textureImage, (uint)image.Width, (uint)image.Height, 1);
        scDevice.TransitionImageLayout(textureImage, format, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        vk.DestroyBuffer(scDevice.LogicalDevice, stagingBuffer, null);
        vk.FreeMemory(scDevice.LogicalDevice, stagingBufferMemory, null);

        return (textureImage, textureImageMemory);
    }

    ImageView CreateTextureImageView(Image textureImage, SCDevice scDevice, Format format)
    {
        return VulkanHelper.CreateImageView(scDevice, textureImage, format, ImageAspectFlags.ColorBit);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(scDevice.LogicalDevice, TextureImageView, null);
            vk.DestroyImage(scDevice.LogicalDevice, TextureImage, null);
            vk.FreeMemory(scDevice.LogicalDevice, TextureImageMemory, null);
            disposedValue = true;
        }
    }

    ~Texture()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
