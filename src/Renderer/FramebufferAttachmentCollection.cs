using Silk.NET.Vulkan;

namespace Renderer;

public interface IFramebufferAttachmentCollection : IDisposable
{
    public IReadOnlyList<IFramebufferAttachment> ColorAttachments { get; }
    public IFramebufferAttachment? DepthAttachment { get; }
    public ImageView[] Attachments { get; }
}

unsafe public class GBufferAttachments : IFramebufferAttachmentCollection
{
    public ImageAttachment Albedo { get; }
    public ImageAttachment Normal { get; }
    public ImageAttachment AoRoughnessMetalness { get; }
    public ImageAttachment Position { get; }
    public ImageAttachment Depth { get; }

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { Albedo, Normal, AoRoughnessMetalness, Position };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => Depth; }
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { Albedo.ImageView, Normal.ImageView, AoRoughnessMetalness.ImageView, Position.ImageView, Depth.ImageView };
        }
    }

    public GBufferAttachments(Device device, PhysicalDevice physicalDevice, Extent2D swapchainImageExtent)
    {
        Albedo = new ImageAttachment(device, physicalDevice, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Normal = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        AoRoughnessMetalness = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Position = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Depth = new ImageAttachment(device, physicalDevice, VulkanHelper.FindDepthFormat(physicalDevice), ImageUsageFlags.DepthStencilAttachmentBit, swapchainImageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            Albedo.Dispose();
            Normal.Dispose();
            AoRoughnessMetalness.Dispose();
            Position.Dispose();
            Depth.Dispose();

            disposedValue = true;
        }
    }

    ~GBufferAttachments()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

unsafe public class CompositionAttachments : IFramebufferAttachmentCollection
{
    public ImageAttachment Color { get; }
    public ImageAttachment Depth { get; }

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { Color };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => Depth; }
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { Color.ImageView, Depth.ImageView };
        }
    }

    public CompositionAttachments(Device device, PhysicalDevice physicalDevice, Extent2D swapchainImageExtent)
    {
        Color = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Depth = new ImageAttachment(device, physicalDevice, VulkanHelper.FindDepthFormat(physicalDevice), ImageUsageFlags.DepthStencilAttachmentBit, swapchainImageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            Color.Dispose();
            Depth.Dispose();

            disposedValue = true;
        }
    }

    ~CompositionAttachments()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

unsafe public class SwapChainAttachment : IFramebufferAttachmentCollection
{
    public ImageViewAttachment SwapchainAttachment { get; }

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { SwapchainAttachment };
        }
    }
    public IFramebufferAttachment? DepthAttachment { get => null; }
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { SwapchainAttachment.ImageView };
        }
    }

    public SwapChainAttachment(Device device, ImageView imageView, Format format)
    {
        SwapchainAttachment = new ImageViewAttachment(device, imageView, format);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            SwapchainAttachment.Dispose();

            disposedValue = true;
        }
    }

    ~SwapChainAttachment()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
