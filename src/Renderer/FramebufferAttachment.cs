using Silk.NET.Vulkan;

namespace Renderer;

public interface IFramebufferAttachment
{
    public Format Format { get; }
    public ImageView ImageView { get; }
}

unsafe public class ImageAttachment : IFramebufferAttachment, IDisposable
{
    public Format Format { get; } 
    public Image Image { get; }
    public DeviceMemory ImageMemory { get; }
    public ImageView ImageView { get; } 
    
    private Device device;
    private bool disposedValue;

    public ImageAttachment(Device device, PhysicalDevice physicalDevice, Format format, ImageUsageFlags usage, Extent2D imageExtent)
    {
        this.device = device;

        ImageAspectFlags aspectFlags = ImageAspectFlags.None;
        if (usage == ImageUsageFlags.ColorAttachmentBit || usage == (ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit))
        {
            aspectFlags = ImageAspectFlags.ColorBit;
        }
        else if (usage == ImageUsageFlags.DepthStencilAttachmentBit)
        {
            aspectFlags = ImageAspectFlags.DepthBit;
        }

        (Image, ImageMemory) = VulkanHelper.CreateImage(device, physicalDevice, 
                (uint) imageExtent.Width, (uint) imageExtent.Height, 
                format, ImageTiling.Optimal, usage | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);

        Format = format;
        ImageView = VulkanHelper.CreateImageView(device, Image, format, aspectFlags);
    }

    protected virtual void Dispose(bool disposing)
    {
        Vk vk = VulkanHelper.Vk;
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImage(device, Image, null);
            vk.FreeMemory(device, ImageMemory, null);
            vk.DestroyImageView(device, ImageView, null);

            disposedValue = true;
        }
    }

    ~ImageAttachment()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

unsafe public class ImageViewAttachment : IFramebufferAttachment, IDisposable
{
    public Format Format { get; }
    public ImageView ImageView { get; }

    private Device device;
    private bool disposedValue;

    public ImageViewAttachment(Device device, ImageView imageView, Format format)
    {
        this.device = device;
        ImageView = imageView;
        Format = format;
    }

    protected virtual void Dispose(bool disposing)
    {
        Vk vk = VulkanHelper.Vk;

        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(device, ImageView, null);

            disposedValue = true;
        }
    }

    ~ImageViewAttachment()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
