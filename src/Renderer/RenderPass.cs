using Silk.NET.Vulkan;

namespace Renderer;

struct FramebufferAttachment
{
    public Format Format { get; init; }
    public Image Image { get; init; }
    public DeviceMemory ImageMemory { get; init; }
    public ImageView ImageView { get; init; }
}

struct GBuffer
{
    public FramebufferAttachment Albedo { get; init; }
    public FramebufferAttachment Normal { get; init; }
    public FramebufferAttachment Specular { get; init; }
    public FramebufferAttachment Position { get; init; }
    public FramebufferAttachment Depth { get; init; }
}

unsafe public partial class VulkanRenderer
{
    FramebufferAttachment CreateFramebufferAttachment(Format format, ImageUsageFlags usage)
    {
        ImageAspectFlags aspectFlags = ImageAspectFlags.None;
        if (usage == ImageUsageFlags.ColorAttachmentBit)
        {
            aspectFlags = ImageAspectFlags.ColorBit;
        }
        else if (usage == ImageUsageFlags.DepthStencilAttachmentBit)
        {
            aspectFlags = ImageAspectFlags.DepthBit;
        }

        (var image, var imageMemory) = VulkanHelper.CreateImage(device, physicalDevice, 
                (uint) swapchainInfo.Extent.Width, (uint) swapchainInfo.Extent.Height,
                format, ImageTiling.Optimal, usage | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit);
        var imageView = CreateImageView(image, format, aspectFlags);

        return new FramebufferAttachment
        {
            Image = image,
            ImageView = imageView,
            ImageMemory = imageMemory,
            Format = format
        };
    }

    void DestroyFramebufferAttachment(FramebufferAttachment attachment)
    {
        vk.DestroyImage(device, attachment.Image, null);
        vk.DestroyImageView(device, attachment.ImageView, null);
        vk.FreeMemory(device, attachment.ImageMemory, null);
    }

    void CreateGeometryRenderPass(out GBuffer gBuffer, out RenderPass renderPass, out Framebuffer framebuffer)
    {
        // create GBuffer attachments
        gBuffer = new()
        {
            Albedo = CreateFramebufferAttachment(Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit),
            Normal = CreateFramebufferAttachment(Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit),
            Specular = CreateFramebufferAttachment(Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit),
            Position = CreateFramebufferAttachment(Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit),
            Depth = CreateFramebufferAttachment(FindDepthFormat(), ImageUsageFlags.DepthStencilAttachmentBit)
        };

        RenderPassBuilder renderPassBuilder = new(device);
        renderPassBuilder.AddColorAttachment(gBuffer.Albedo.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBuffer.Normal.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBuffer.Specular.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBuffer.Position.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(gBuffer.Depth.Format)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.BottomOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.MemoryReadBit, AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                                        DependencyFlags.ByRegionBit)
                         .AddDependency(0, Vk.SubpassExternal,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit, AccessFlags.MemoryReadBit,
                                        DependencyFlags.ByRegionBit);
        renderPass = renderPassBuilder.Build();

        // create framebuffer
        var attachments = new ImageView[] 
        {
            gBuffer.Albedo.ImageView,
            gBuffer.Normal.ImageView,
            gBuffer.Specular.ImageView,
            gBuffer.Position.ImageView,
            gBuffer.Depth.ImageView
        };

        fixed (ImageView* attachmentsPtr = attachments)
        {
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                PAttachments = attachmentsPtr,
                RenderPass = renderPass,
                AttachmentCount = (uint) attachments.Length,
                Width = swapchainInfo.Extent.Width,
                Height = swapchainInfo.Extent.Height,
                Layers = 1
            };

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out framebuffer) != Result.Success)
            {
                throw new Exception("Failed to create geometry framebuffer!");
            }
        }
    }

    void DestroyGeomtryRenderPass(GBuffer gBuffer, RenderPass renderPass, Framebuffer framebuffer)
    {
        DestroyFramebufferAttachment(gBuffer.Albedo);
        DestroyFramebufferAttachment(gBuffer.Normal);
        DestroyFramebufferAttachment(gBuffer.Specular);
        DestroyFramebufferAttachment(gBuffer.Position);
        DestroyFramebufferAttachment(gBuffer.Depth);

        vk.DestroyRenderPass(device, renderPass, null);

        vk.DestroyFramebuffer(device, framebuffer, null);
    }

    void CreateCompositionRenderPass(out FramebufferAttachment colorAttachment, out FramebufferAttachment depthAttachment, out RenderPass renderPass, out Framebuffer framebuffer)
    {
        // create attachments
        colorAttachment = CreateFramebufferAttachment(Format.R16G16B16A16Sfloat, ImageUsageFlags.ColorAttachmentBit);
        depthAttachment = CreateFramebufferAttachment(FindDepthFormat(), ImageUsageFlags.DepthStencilAttachmentBit);

        RenderPassBuilder renderPassBuilder = new(device);
        renderPassBuilder.AddColorAttachment(colorAttachment.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(depthAttachment.Format)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
                                        DependencyFlags.None)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.None, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
                                        DependencyFlags.None);
        renderPass = renderPassBuilder.Build();

        // create framebuffer
        
        var attachments = new ImageView[]
        {
            colorAttachment.ImageView,
            depthAttachment.ImageView
        };

        fixed (ImageView* attachmentsPtr = attachments)
        {
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                PAttachments = attachmentsPtr,
                RenderPass = renderPass,
                AttachmentCount = (uint) attachments.Length,
                Width = swapchainInfo.Extent.Width,
                Height = swapchainInfo.Extent.Height,
                Layers = 1
            };

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out framebuffer) != Result.Success)
            {
                throw new Exception("Failed to create geometry framebuffer!");
            }
        }
    }

    void DestroyCompositionRenderPass(FramebufferAttachment colorAttachment, FramebufferAttachment depthAttachment, RenderPass renderPass, Framebuffer framebuffer)
    {
        DestroyFramebufferAttachment(colorAttachment);
        DestroyFramebufferAttachment(depthAttachment);

        vk.DestroyRenderPass(device, renderPass, null);

        vk.DestroyFramebuffer(device, framebuffer, null);
    }

    void CreatePostProcessingRenderPass(out RenderPass renderPass, out Framebuffer[] framebuffers)
    {
        RenderPassBuilder renderPassBuilder = new(device);
        renderPassBuilder.AddColorAttachment(swapchainInfo.ImageFormat, ImageLayout.PresentSrcKhr)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
                                        DependencyFlags.None)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.None, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
                                        DependencyFlags.None);
        renderPass = renderPassBuilder.Build();

        // create framebuffers
        int swapchainImageCount = swapchainInfo.ImageViews.Length;
        framebuffers = new Framebuffer[swapchainImageCount];
        
        for (int i = 0; i < swapchainImageCount; i++)
        {
            var attachments = new ImageView[] { swapchainInfo.ImageViews[i] };
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                AttachmentCount = (uint) attachments.Length,
                RenderPass = renderPass,
                Width = swapchainInfo.Extent.Width,
                Height = swapchainInfo.Extent.Height,
                Layers = 1
            };
            fixed (ImageView* attachmentsPtr = attachments)
                framebufferInfo.PAttachments = attachmentsPtr;

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out framebuffers[i]) != Result.Success)
            {
                throw new Exception("Failed to create framebuffer!");
            }
        }
    }

    void DestroyPostProcessingRenderPass(RenderPass renderPass, Framebuffer[] framebuffers)
    {
        vk.DestroyRenderPass(device, renderPass, null);

        foreach (var framebuffer in framebuffers)
            vk.DestroyFramebuffer(device, framebuffer, null);
    }
}
