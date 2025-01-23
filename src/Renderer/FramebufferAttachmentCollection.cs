using Silk.NET.Vulkan;

namespace Renderer;

public interface IFramebufferAttachmentCollection : IDisposable
{
    public IReadOnlyList<IFramebufferAttachment> ColorAttachments { get; }
    public IFramebufferAttachment? DepthAttachment { get; }
    public ImageView[] Attachments { get; }
    public Extent2D ImageExtent { get; }
}

unsafe public class SingleColorAttachment : IFramebufferAttachmentCollection
{
    public ImageAttachment Color { get; }
    public ImageAttachment Depth { get; }

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
    public Extent2D ImageExtent { get; }

    private bool disposedValue;

    public SingleColorAttachment(SCDevice scDevice, Format format, Extent2D imageExtent)
    {
        ImageExtent = imageExtent;
        Color = new ImageAttachment(scDevice, format,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, imageExtent);
        Depth = new ImageAttachment(scDevice, VulkanHelper.FindDepthFormat(scDevice),
                ImageUsageFlags.DepthStencilAttachmentBit, imageExtent);
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

    ~SingleColorAttachment()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

unsafe public class DepthOnlyAttachment : IFramebufferAttachmentCollection
{
    public ImageAttachment Depth { get; }

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments 
    {
        get
        {
            return new IFramebufferAttachment[] { };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => Depth; } 
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { Depth.ImageView };
        }
    }
    public Extent2D ImageExtent { get; }

    private bool disposedValue;

    public DepthOnlyAttachment(SCDevice scDevice, Extent2D imageExtent)
    {
        ImageExtent = imageExtent;
        Depth = new ImageAttachment(scDevice, VulkanHelper.FindDepthFormat(scDevice), 
                ImageUsageFlags.DepthStencilAttachmentBit, imageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            Depth.Dispose();

            disposedValue = true;
        }
    }

    ~DepthOnlyAttachment()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

unsafe public class DepthCubeMapOnlyAttachment : IFramebufferAttachmentCollection
{
    public CubeMapImageAttachment Depth { get; }

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments 
    {
        get
        {
            return new IFramebufferAttachment[] { };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => Depth; } 
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { Depth.ImageView };
        }
    }
    public Extent2D ImageExtent { get; }

    private bool disposedValue;

    public DepthCubeMapOnlyAttachment(SCDevice scDevice, Extent2D imageExtent)
    {
        ImageExtent = imageExtent;
        Depth = new CubeMapImageAttachment(scDevice, VulkanHelper.FindDepthFormat(scDevice), 
                ImageUsageFlags.DepthStencilAttachmentBit, imageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            Depth.Dispose();

            disposedValue = true;
        }
    }

    ~DepthCubeMapOnlyAttachment()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
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
    public Extent2D ImageExtent { get; }

    public GBufferAttachments(SCDevice scDevice, Extent2D swapchainImageExtent)
    {
        ImageExtent = swapchainImageExtent;
        Albedo = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Normal = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        AoRoughnessMetalness = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Position = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Depth = new ImageAttachment(scDevice, VulkanHelper.FindDepthFormat(scDevice), ImageUsageFlags.DepthStencilAttachmentBit, swapchainImageExtent);
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
    public ImageAttachment ThresholdedColor { get; }
    public ImageAttachment Depth { get; }

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { Color, ThresholdedColor };
        }
    }
    public IFramebufferAttachment DepthAttachment { get => Depth; }
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { Color.ImageView, ThresholdedColor.ImageView, Depth.ImageView };
        }
    }

    public CompositionAttachments(SCDevice scDevice, Extent2D swapchainImageExtent)
    {
        ImageExtent = swapchainImageExtent;
        Color = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        ThresholdedColor = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
        Depth = new ImageAttachment(scDevice, VulkanHelper.FindDepthFormat(scDevice), ImageUsageFlags.DepthStencilAttachmentBit, swapchainImageExtent);
    }
    public Extent2D ImageExtent { get; }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            Color.Dispose();
            ThresholdedColor.Dispose();
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

unsafe public class BloomAttachments : IFramebufferAttachmentCollection
{
    public ImageAttachment Color { get; }

    private bool disposedValue;

    public IReadOnlyList<IFramebufferAttachment> ColorAttachments
    {
        get 
        {
            return new IFramebufferAttachment[] { Color };
        }
    }
    public IFramebufferAttachment? DepthAttachment { get => null; }
    public ImageView[] Attachments
    {
        get
        {
            return new ImageView[] { Color.ImageView };
        }
    }
    public Extent2D ImageExtent { get; }

    public BloomAttachments(SCDevice scDevice, Extent2D swapchainImageExtent)
    {
        ImageExtent = swapchainImageExtent;
        Color = new ImageAttachment(scDevice, Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit, swapchainImageExtent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            Color.Dispose();

            disposedValue = true;
        }
    }

    ~BloomAttachments()
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
    public Extent2D ImageExtent { get; }

    public SwapChainAttachment(SCDevice scDevice, ImageView imageView, Format format)
    {
        ImageExtent = scDevice.SwapchainInfo.Extent;
        SwapchainAttachment = new ImageViewAttachment(scDevice, imageView, format);
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

