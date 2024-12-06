using Silk.NET.Vulkan;

namespace Renderer;

public interface IFramebufferAttachmentCollection : IDisposable
{
    public IReadOnlyList<IFramebufferAttachment> ColorAttachments { get; }
    public IFramebufferAttachment? DepthAttachment { get; }
}

unsafe public class GBufferAttachments : IFramebufferAttachmentCollection
{
    private ImageAttachment albedo;
    private ImageAttachment normal;
    private ImageAttachment aoRoughnessMetalness;
    private ImageAttachment position;
    private ImageAttachment depth;

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { albedo, normal, aoRoughnessMetalness, position };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => depth; }

    public GBufferAttachments(Device device, PhysicalDevice physicalDevice, Extent2D swapchainImageExtent)
    {
        albedo = new ImageAttachment(device, physicalDevice, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        normal = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        aoRoughnessMetalness = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        position = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        depth = new ImageAttachment(device, physicalDevice, VulkanHelper.FindDepthFormat(physicalDevice), ImageUsageFlags.DepthStencilAttachmentBit, swapchainImageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            albedo.Dispose();
            normal.Dispose();
            aoRoughnessMetalness.Dispose();
            position.Dispose();
            depth.Dispose();

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
    private ImageAttachment color;
    private ImageAttachment depth;

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { color };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => depth; }

    public CompositionAttachments(Device device, PhysicalDevice physicalDevice, Extent2D swapchainImageExtent)
    {
        color = new ImageAttachment(device, physicalDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        depth = new ImageAttachment(device, physicalDevice, VulkanHelper.FindDepthFormat(physicalDevice), ImageUsageFlags.DepthStencilAttachmentBit, swapchainImageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            color.Dispose();
            depth.Dispose();

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
    private ImageViewAttachment swapchainAttachment;

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { swapchainAttachment };
        }
    }
    public IFramebufferAttachment? DepthAttachment { get => null; }

    public SwapChainAttachment(Device device, ImageView imageView, Format format)
    {
        swapchainAttachment = new ImageViewAttachment(device, imageView, format);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            swapchainAttachment.Dispose();

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
