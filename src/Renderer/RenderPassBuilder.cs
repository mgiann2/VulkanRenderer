using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class RenderPassBuilder
{
    Vk vk;
    Device device;

    List<AttachmentDescription> colorAttachmentDescriptions = new();
    List<AttachmentReference> colorAttachmentReferences = new();

    AttachmentDescription? depthStencilAttachmentDescription;
    AttachmentReference? depthStencilAttachmentReference;

    List<SubpassDependency> dependencies = new();

    public RenderPassBuilder(Device device)
    {
        vk = Vk.GetApi();
        this.device = device;
    }

    public RenderPassBuilder AddColorAttachment(Format format, ImageLayout finalLayout)
    {
        AttachmentDescription attachmentDescription = new()
        {
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = finalLayout,
            Format = format
        };

        AttachmentReference attachmentReference = new()
        {
            Attachment = (uint) colorAttachmentReferences.Count(),
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        colorAttachmentDescriptions.Add(attachmentDescription);
        colorAttachmentReferences.Add(attachmentReference);

        return this;
    }

    public RenderPassBuilder SetDepthStencilAttachment(Format format)
    {
        AttachmentDescription attachmentDescription = new()
        {
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
            Format = format
        };

        AttachmentReference attachmentReference = new()
        {
            Attachment = 0, // this value will be updated when render pass is built
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        depthStencilAttachmentDescription = attachmentDescription;
        depthStencilAttachmentReference = attachmentReference;

        return this;
    }

    public RenderPassBuilder ClearDepthStencilAttachment()
    {
        depthStencilAttachmentDescription = null;
        depthStencilAttachmentReference = null;

        return this;
    }

    public RenderPassBuilder AddDependency(uint srcSubpass, uint dstSubpass,
                                           PipelineStageFlags srcStageMask, PipelineStageFlags dstStageMask,
                                           AccessFlags srcAccessMask, AccessFlags dstAccessMask, DependencyFlags flags)
    {
        SubpassDependency dependency = new()
        {
            SrcSubpass = srcSubpass,
            DstSubpass = dstSubpass,
            SrcStageMask = srcStageMask,
            DstStageMask = dstStageMask,
            SrcAccessMask = srcAccessMask,
            DstAccessMask = dstAccessMask,
            DependencyFlags = flags
        };

        dependencies.Add(dependency);

        return this;
    }

    public RenderPassBuilder ClearDependencies()
    {
        dependencies.Clear();

        return this;
    }

    public RenderPass Build()
    {
        // create subpass description
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = (uint) colorAttachmentDescriptions.Count()
        };

        fixed (AttachmentReference* colorRefsPtr = colorAttachmentReferences.ToArray())
            subpass.PColorAttachments = colorRefsPtr;

        if (depthStencilAttachmentReference.HasValue)
        {
            var depthRef = depthStencilAttachmentReference.Value;
            depthRef.Attachment = (uint) colorAttachmentReferences.Count();
            subpass.PDepthStencilAttachment = &depthRef;
        }

        // combine attachment descriptions
        List<AttachmentDescription> attachmentDescriptions = new();
        attachmentDescriptions.AddRange(colorAttachmentDescriptions);

        if (depthStencilAttachmentDescription.HasValue)
        {
            attachmentDescriptions.Add(depthStencilAttachmentDescription.Value);
        }

        // create render pass
        RenderPassCreateInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            PSubpasses = &subpass,
            SubpassCount = 1,
            AttachmentCount = (uint) attachmentDescriptions.Count(),
            DependencyCount = (uint) dependencies.Count()
        };

        fixed (AttachmentDescription* attachmentDescriptionsPtr = attachmentDescriptions.ToArray())
        fixed (SubpassDependency* dependenciesPtr = dependencies.ToArray())
        {
            renderPassInfo.PAttachments = attachmentDescriptionsPtr;
            renderPassInfo.PDependencies = dependenciesPtr;
        }

        if (vk.CreateRenderPass(device, in renderPassInfo, null, out var renderPass) != Result.Success)
        {
            throw new Exception("Unable to create render pass!");
        }

        return renderPass;
    }
}
