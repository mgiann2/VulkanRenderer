using Silk.NET.Vulkan;
using StbiSharp;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Renderer;

unsafe public class Texture : IDisposable
{
    private static Vk vk = VulkanHelper.Vk;
    private VulkanRenderer renderer;
    private bool disposedValue;

    public Image TextureImage { get; }
    public DeviceMemory TextureImageMemory { get; }
    public ImageView TextureImageView { get; }

    public Texture(VulkanRenderer renderer, string filepath)
    {
        this.renderer = renderer;

        using (var stream = File.OpenRead(filepath))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            (TextureImage, TextureImageMemory) = CreateTextureImage(memoryStream, renderer);
        }

        TextureImageView = CreateTextureImageView(TextureImage, renderer);
    }

    (Image, DeviceMemory) CreateTextureImage(MemoryStream memoryStream, VulkanRenderer renderer)
    {
        var image = Stbi.LoadFromMemory(memoryStream, 4);

        ulong imageSize = (ulong)(image.Width * image.Height * 4);

        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(renderer.Device, renderer.PhysicalDevice, imageSize, BufferUsageFlags.TransferSrcBit,
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(renderer.Device, stagingBufferMemory, 0, imageSize, 0, &data);
        image.Data.CopyTo(new Span<byte>(data, (int)imageSize));
        vk.UnmapMemory(renderer.Device, stagingBufferMemory);

        (var textureImage, var textureImageMemory) = VulkanHelper.CreateImage(renderer.Device, renderer.PhysicalDevice, 
                (uint)image.Width, (uint)image.Height, Format.R8G8B8A8Srgb, ImageTiling.Optimal,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);

        renderer.TransitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        renderer.CopyBufferToImage(stagingBuffer, textureImage, (uint)image.Width, (uint)image.Height, 1);
        renderer.TransitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        vk.DestroyBuffer(renderer.Device, stagingBuffer, null);
        vk.FreeMemory(renderer.Device, stagingBufferMemory, null);

        return (textureImage, textureImageMemory);
    }

    ImageView CreateTextureImageView(Image textureImage, VulkanRenderer renderer)
    {
        return VulkanHelper.CreateImageView(renderer.Device, textureImage, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(renderer.Device, TextureImageView, null);
            vk.DestroyImage(renderer.Device, TextureImage, null);
            vk.FreeMemory(renderer.Device, TextureImageMemory, null);
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
