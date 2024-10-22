using Silk.NET.Vulkan;

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

        CreateImage((uint) swapchainExtent.Width, (uint) swapchainExtent.Height,
                    format, ImageTiling.Optimal, usage | ImageUsageFlags.SampledBit,
                    MemoryPropertyFlags.DeviceLocalBit,
                    out var image, out var imageMemory);
        var imageView = CreateImageView(image, format, aspectFlags);

        return new FramebufferAttachment
        {
            Image = image,
            ImageView = imageView,
            ImageMemory = imageMemory,
            Format = format
        };
    }

    void CreateGeometryRenderPass()
    {
        // create GBuffer
        GBuffer gBuffer = new()
        {
            Albedo = CreateFramebufferAttachment(Format.R8G8B8A8Srgb, ImageUsageFlags.ColorAttachmentBit),
            Normal = CreateFramebufferAttachment(Format.R16G16B16Sfloat, ImageUsageFlags.ColorAttachmentBit),
            Specular = CreateFramebufferAttachment(Format.R8G8B8Srgb, ImageUsageFlags.ColorAttachmentBit),
            Depth = CreateFramebufferAttachment(FindDepthFormat(), ImageUsageFlags.DepthStencilAttachmentBit)
        };

        // create attachment descriptions
        var attachmentDescriptions = new AttachmentDescription[4];

        for (int i = 0;  i < attachmentDescriptions.Length; i++)
        {
            attachmentDescriptions[i] = new()
            {
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = (i == 3) ? ImageLayout.DepthStencilAttachmentOptimal : ImageLayout.ShaderReadOnlyOptimal
            };
        }
        attachmentDescriptions[0].Format = gBuffer.Albedo.Format;
        attachmentDescriptions[1].Format = gBuffer.Normal.Format;
        attachmentDescriptions[2].Format = gBuffer.Specular.Format;
        attachmentDescriptions[3].Format = gBuffer.Depth.Format;

        // create attachment references
        var colorReferences = new AttachmentReference[]
        {
            new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 2, Layout = ImageLayout.ColorAttachmentOptimal },
        };
        AttachmentReference depthReference = new() { Attachment = 3, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        // create render pass
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = (uint) colorReferences.Length,
            PDepthStencilAttachment = &depthReference
        };
        fixed (AttachmentReference* colorReferencesPtr = colorReferences)
            subpass.PColorAttachments = colorReferencesPtr;

        var dependencies = new SubpassDependency[]
        {
            new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.BottomOfPipeBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.MemoryReadBit,
                DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
            },
            new()
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit
            }
        };

        fixed (AttachmentDescription* attachmentDescriptionsPtr = attachmentDescriptions)
        fixed (SubpassDependency* dependenciesPtr = dependencies)
        {
            RenderPassCreateInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                PAttachments = attachmentDescriptionsPtr,
                AttachmentCount = (uint) attachmentDescriptions.Length,
                PSubpasses = &subpass,
                SubpassCount = 1,
                PDependencies = dependenciesPtr,
                DependencyCount = (uint) dependencies.Length
            };

            if (vk.CreateRenderPass(device, in renderPassInfo, null, out var renderPass) != Result.Success)
            {
                throw new Exception("Failed to create geometry render pass!");
            }
        }

        // create framebuffer
        var attachments = new ImageView[] 
        {
            gBuffer.Albedo.ImageView,
            gBuffer.Normal.ImageView,
            gBuffer.Specular.ImageView,
            gBuffer.Depth.ImageView
        };

        fixed (ImageView* attachmentsPtr = attachments)
        {
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                PAttachments = attachmentsPtr,
                AttachmentCount = (uint) attachments.Length,
                Width = swapchainExtent.Width,
                Height = swapchainExtent.Height,
                Layers = 1
            };

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out var framebuffer) != Result.Success)
            {
                throw new Exception("Failed to create geometry framebuffer!");
            }
        }
    }

    void CreateCompositionRenderPass()
    {

    }
}
