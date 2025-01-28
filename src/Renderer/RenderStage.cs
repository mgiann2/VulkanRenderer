using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class RenderStage : IDisposable
{
    private CommandBuffer[] commandBuffers;
    private Framebuffer[] framebuffers;
    private IFramebufferAttachmentCollection[] framebufferAttachmentCollections;

    private Vk vk;
    private SCDevice scDevice;
    private Extent2D extent;

    bool disposedValue;

    public RenderPass RenderPass { get; }
    public List<ClearValue> ClearValues = new();

    public RenderStage(SCDevice scDevice, RenderPass renderPass, IFramebufferAttachmentCollection[] framebufferAttachmentCollections, uint commandBufferCount)
    {
        vk = VulkanHelper.Vk;
        this.scDevice = scDevice;
        this.extent = framebufferAttachmentCollections[0].ImageExtent;

        this.RenderPass = renderPass;
        this.framebufferAttachmentCollections = framebufferAttachmentCollections;

        // Create framebuffers
        uint framebufferCount = (uint) framebufferAttachmentCollections.Length;
        this.framebuffers = new Framebuffer[framebufferCount];
        for (uint i = 0; i < framebufferCount; i++)
        {
            framebuffers[i] = VulkanHelper.CreateFramebuffer(scDevice, renderPass, framebufferAttachmentCollections[i]);
        }

        // Create command buffers
        var tmpCommandBuffers = new CommandBuffer[commandBufferCount];
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = scDevice.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = commandBufferCount 
        };

        fixed (CommandBuffer* commandBuffersPtr = tmpCommandBuffers)
        {
            if (vk.AllocateCommandBuffers(scDevice.LogicalDevice, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
        commandBuffers = tmpCommandBuffers;
    }

    public RenderStage(SCDevice scDevice, RenderPass renderPass, IFramebufferAttachmentCollection[] framebufferAttachmentCollections, uint layerCount, uint commandBufferCount)
    {
        vk = VulkanHelper.Vk;
        this.scDevice = scDevice;
        this.extent = framebufferAttachmentCollections[0].ImageExtent;

        this.RenderPass = renderPass;
        this.framebufferAttachmentCollections = framebufferAttachmentCollections;

        // Create framebuffers
        uint framebufferCount = (uint) framebufferAttachmentCollections.Length;
        this.framebuffers = new Framebuffer[framebufferCount];
        for (uint i = 0; i < framebufferCount; i++)
        {
            framebuffers[i] = VulkanHelper.CreateFramebuffer(scDevice, renderPass, framebufferAttachmentCollections[i], layerCount);
        }

        // Create command buffers
        var tmpCommandBuffers = new CommandBuffer[commandBufferCount];
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = scDevice.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = commandBufferCount 
        };

        fixed (CommandBuffer* commandBuffersPtr = tmpCommandBuffers)
        {
            if (vk.AllocateCommandBuffers(scDevice.LogicalDevice, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
        commandBuffers = tmpCommandBuffers;
    }

    /// <summary>
    /// Begins a render stage to allow for drawing commands. Implicitly begins the command buffer
    /// at commandBufferIndex.
    /// </summary>
    /// <param name="commandBufferIndex">The index of the command buffer to use for rendering.</param>
    public void BeginCommands(uint commandBufferIndex)
    {
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        if (vk.BeginCommandBuffer(commandBuffers[commandBufferIndex], in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }
    }

    /// <summary>
    /// Ends a render stage's drawing commands. Implicitly ends the command buffer at commandBufferIndex.
    /// </summary>
    /// <param name="commandBufferIndex">The index of the command buffer that was used for rendering.</param>
    public void EndCommands(uint commandBufferIndex)
    {
        if (vk.EndCommandBuffer(commandBuffers[commandBufferIndex]) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }
    }

    /// <summary>
    /// Begins a render pass using the framebuffer at framebufferIndex. Requires
    /// that the command buffer at commandBufferIndex has began using the 
    /// BeginCommands method.
    /// Furthemore, sets the viewport and scissor to be the full size of the framebuffer image.
    /// </summary>
    /// <param name="commandBufferIndex">The index of the command buffer to use for rendering.</param>
    /// <param name="framebufferIndex">The index of the framebuffer to render to.</param>
    public void BeginRenderPass(uint commandBufferIndex, uint framebufferIndex)
    {
        RenderPassBeginInfo renderPassBeginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = RenderPass,
            Framebuffer = framebuffers[framebufferIndex],
            RenderArea = 
            {
                Extent = extent,
                Offset = { X = 0, Y = 0 }
            }
        };

        var clearValues = ClearValues.ToArray();
        fixed (ClearValue* clearValuesPtr = clearValues)
            renderPassBeginInfo.PClearValues = clearValuesPtr;
        renderPassBeginInfo.ClearValueCount = (uint) clearValues.Length;

        vk.CmdBeginRenderPass(commandBuffers[commandBufferIndex], in renderPassBeginInfo, SubpassContents.Inline);

        Viewport viewport = new()
        {
            X = 0.0f,
            Y = 0.0f,
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        vk.CmdSetViewport(commandBuffers[commandBufferIndex], 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = extent
        };
        vk.CmdSetScissor(commandBuffers[commandBufferIndex], 0, 1, in scissor);
    }

    /// <summary>
    /// Ends the current render pass. Requires that a render pass has begun using
    /// the BeginRenderPass method.
    /// </summary>
    /// <param name="commandBufferIndex">The index of the command buffer to use for rendering.</param>
    public void EndRenderPass(uint commandBufferIndex)
    {
        vk.CmdEndRenderPass(commandBuffers[commandBufferIndex]);
    }

    public CommandBuffer GetCommandBuffer(uint commandBufferIndex)
    {
        return commandBuffers[commandBufferIndex];
    }

    public void ResetCommandBuffer(uint commandBufferIndex)
    {
        vk.ResetCommandBuffer(commandBuffers[commandBufferIndex], CommandBufferResetFlags.None);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyRenderPass(scDevice.LogicalDevice, RenderPass, null);

            fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
                vk.FreeCommandBuffers(scDevice.LogicalDevice, scDevice.CommandPool, (uint) commandBuffers.Length, commandBuffersPtr);
            
            foreach (var framebuffer in framebuffers)
            {
                vk.DestroyFramebuffer(scDevice.LogicalDevice, framebuffer, null);
            }

            foreach (var framebufferAttachmentCollection in framebufferAttachmentCollections)
            {
                framebufferAttachmentCollection.Dispose();
            }

            disposedValue = true;
        }
    }

    ~RenderStage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
