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

    public Texture(VulkanRenderer renderer, string filepath, bool isNormal = false)
    {
        this.renderer = renderer;

        Format format = isNormal ? Format.R8G8B8A8Unorm : Format.R8G8B8A8Srgb;

        using (var stream = File.OpenRead(filepath))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            (TextureImage, TextureImageMemory) = CreateTextureImage(memoryStream, renderer, format);
        }

        TextureImageView = CreateTextureImageView(TextureImage, renderer, format);
    }

    (Image, DeviceMemory) CreateTextureImage(MemoryStream memoryStream, VulkanRenderer renderer, Format format)
    {
        var image = Stbi.LoadFromMemory(memoryStream, 4);

        ulong imageSize = (ulong)(image.Width * image.Height * 4);

        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(renderer.SCDevice, imageSize, BufferUsageFlags.TransferSrcBit,
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(renderer.SCDevice.LogicalDevice, stagingBufferMemory, 0, imageSize, 0, &data);
        image.Data.CopyTo(new Span<byte>(data, (int)imageSize));
        vk.UnmapMemory(renderer.SCDevice.LogicalDevice, stagingBufferMemory);

        (var textureImage, var textureImageMemory) = VulkanHelper.CreateImage(renderer.SCDevice, 
                (uint)image.Width, (uint)image.Height, format, ImageTiling.Optimal,
                ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);

        renderer.TransitionImageLayout(textureImage, format, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        renderer.CopyBufferToImage(stagingBuffer, textureImage, (uint)image.Width, (uint)image.Height, 1);
        renderer.TransitionImageLayout(textureImage, format, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        vk.DestroyBuffer(renderer.SCDevice.LogicalDevice, stagingBuffer, null);
        vk.FreeMemory(renderer.SCDevice.LogicalDevice, stagingBufferMemory, null);

        return (textureImage, textureImageMemory);
    }

    ImageView CreateTextureImageView(Image textureImage, VulkanRenderer renderer, Format format)
    {
        return VulkanHelper.CreateImageView(renderer.SCDevice, textureImage, format, ImageAspectFlags.ColorBit);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(renderer.SCDevice.LogicalDevice, TextureImageView, null);
            vk.DestroyImage(renderer.SCDevice.LogicalDevice, TextureImage, null);
            vk.FreeMemory(renderer.SCDevice.LogicalDevice, TextureImageMemory, null);
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
