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
    
    private SCDevice scDevice;
    private bool disposedValue;

    public ImageAttachment(SCDevice scDevice, Format format, ImageUsageFlags usage, Extent2D imageExtent)
    {
        this.scDevice = scDevice;

        ImageAspectFlags aspectFlags = ImageAspectFlags.None;
        if (usage == ImageUsageFlags.ColorAttachmentBit || usage == (ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit))
        {
            aspectFlags = ImageAspectFlags.ColorBit;
        }
        else if (usage == ImageUsageFlags.DepthStencilAttachmentBit)
        {
            aspectFlags = ImageAspectFlags.DepthBit;
        }

        (Image, ImageMemory) = VulkanHelper.CreateImage(scDevice, 
                (uint) imageExtent.Width, (uint) imageExtent.Height, 
                format, ImageTiling.Optimal, usage | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);

        Format = format;
        ImageView = VulkanHelper.CreateImageView(scDevice, Image, format, aspectFlags);
    }

    protected virtual void Dispose(bool disposing)
    {
        Vk vk = VulkanHelper.Vk;
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImage(scDevice.LogicalDevice, Image, null);
            vk.FreeMemory(scDevice.LogicalDevice, ImageMemory, null);
            vk.DestroyImageView(scDevice.LogicalDevice, ImageView, null);

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

unsafe public class CubeMapImageAttachment : IFramebufferAttachment, IDisposable
{
    public Format Format { get; } 
    public Image Image { get; }
    public DeviceMemory ImageMemory { get; }
    public ImageView ImageView { get; } 
    
    private SCDevice scDevice;
    private bool disposedValue;

    public CubeMapImageAttachment(SCDevice scDevice, Format format, ImageUsageFlags usage, Extent2D imageExtent)
    {
        this.scDevice = scDevice;

        ImageAspectFlags aspectFlags = ImageAspectFlags.None;
        if (usage == ImageUsageFlags.ColorAttachmentBit || usage == (ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit))
        {
            aspectFlags = ImageAspectFlags.ColorBit;
        }
        else if (usage == ImageUsageFlags.DepthStencilAttachmentBit)
        {
            aspectFlags = ImageAspectFlags.DepthBit;
        }

        (Image, ImageMemory) = VulkanHelper.CreateCubemapImage(scDevice, format,
                ImageTiling.Optimal, usage | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit,
                (uint) imageExtent.Width, (uint) imageExtent.Height);

        Format = format;
        ImageView = VulkanHelper.CreateCubemapImageView(scDevice, Image, format, aspectFlags);
    }

    protected virtual void Dispose(bool disposing)
    {
        Vk vk = VulkanHelper.Vk;
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImage(scDevice.LogicalDevice, Image, null);
            vk.FreeMemory(scDevice.LogicalDevice, ImageMemory, null);
            vk.DestroyImageView(scDevice.LogicalDevice, ImageView, null);

            disposedValue = true;
        }
    }

    ~CubeMapImageAttachment()
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

    private SCDevice scDevice;
    private bool disposedValue;

    public ImageViewAttachment(SCDevice scDevice, ImageView imageView, Format format)
    {
        this.scDevice = scDevice;
        ImageView = imageView;
        Format = format;
    }

    protected virtual void Dispose(bool disposing)
    {
        Vk vk = VulkanHelper.Vk;

        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyImageView(scDevice.LogicalDevice, ImageView, null);

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
