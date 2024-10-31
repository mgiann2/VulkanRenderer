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

        CreateImage((uint) swapchainInfo.Extent.Width, (uint) swapchainInfo.Extent.Height,
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

        // create attachment descriptions
        var attachmentDescriptions = new AttachmentDescription[5];

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
                FinalLayout = (i == 4) ? ImageLayout.DepthStencilAttachmentOptimal : ImageLayout.ShaderReadOnlyOptimal
            };
        }
        attachmentDescriptions[0].Format = gBuffer.Albedo.Format;
        attachmentDescriptions[1].Format = gBuffer.Normal.Format;
        attachmentDescriptions[2].Format = gBuffer.Specular.Format;
        attachmentDescriptions[3].Format = gBuffer.Position.Format;
        attachmentDescriptions[4].Format = gBuffer.Depth.Format;

        // create attachment references
        var colorReferences = new AttachmentReference[]
        {
            new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 2, Layout = ImageLayout.ColorAttachmentOptimal },
            new() { Attachment = 3, Layout = ImageLayout.ColorAttachmentOptimal }
        };
        AttachmentReference depthReference = new() { Attachment = 4, Layout = ImageLayout.DepthStencilAttachmentOptimal };

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
                DependencyFlags = DependencyFlags.ByRegionBit
            },
            new()
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit,
                DependencyFlags = DependencyFlags.ByRegionBit
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

            if (vk.CreateRenderPass(device, in renderPassInfo, null, out renderPass) != Result.Success)
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

    void CreateCompositionRenderPass(out FramebufferAttachment depthAttachment, out RenderPass renderPass, out Framebuffer[] framebuffers)
    {
        // create attachments
        depthAttachment = CreateFramebufferAttachment(FindDepthFormat(), ImageUsageFlags.DepthStencilAttachmentBit);

        // create attachment descriptions
        AttachmentDescription colorAttachmentDescription = new()
        {
            Format = swapchainInfo.ImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentDescription depthAttachmentDescription = new()
        {
            Format = depthAttachment.Format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.Clear,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        AttachmentReference colorAttachmentRef = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        AttachmentReference depthAttachmentRef = new()
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        // create render pass
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
            PDepthStencilAttachment = &depthAttachmentRef
        };

        var dependencies = new SubpassDependency[]
        {
            new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
                DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
            },
            new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
            }
        };

        var attachmentDescriptions = new AttachmentDescription[] { colorAttachmentDescription, depthAttachmentDescription };

        fixed (AttachmentDescription* attachmentsPtr = attachmentDescriptions)
        fixed (SubpassDependency* dependenciesPtr = dependencies)
        {
            RenderPassCreateInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = dependenciesPtr,
                AttachmentCount = (uint) attachmentDescriptions.Length,
                PAttachments = attachmentsPtr
            };

            if (vk.CreateRenderPass(device, in renderPassInfo, null, out renderPass) != Result.Success)
            {
                throw new Exception("Failed to create composition render pass!");
            }
        }

        // create framebuffers
        int swapchainImageCount = swapchainInfo.ImageViews.Length;
        framebuffers = new Framebuffer[swapchainImageCount];
        
        for (int i = 0; i < swapchainImageCount; i++)
        {
            var attachments = new ImageView[] { swapchainInfo.ImageViews[i], depthAttachment.ImageView };
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

    void DestroyCompositionRenderPass(FramebufferAttachment depthAttachment, RenderPass renderPass, Framebuffer[] framebuffers)
    {
        DestroyFramebufferAttachment(depthAttachment);

        vk.DestroyRenderPass(device, renderPass, null);

        foreach (var framebuffer in framebuffers)
            vk.DestroyFramebuffer(device, framebuffer, null);
    }
}
