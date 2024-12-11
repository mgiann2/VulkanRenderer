using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Renderer;

unsafe public class RenderStage : IDisposable
{
    private CommandBuffer[] commandBuffers;
    private Semaphore[] signalSemaphores;
    private Framebuffer[] framebuffers;
    private IFramebufferAttachmentCollection[] framebufferAttachmentCollections;

    private Vk vk;
    private CommandPool commandPool;
    private Device device;
    private Extent2D swapchainExtent;

    bool disposedValue;

    public RenderPass RenderPass { get; }
    public List<ClearValue> ClearValues = new();

    public RenderStage(Device device, RenderPass renderPass, IFramebufferAttachmentCollection[] framebufferAttachmentCollections, CommandPool commandPool, Extent2D swapchainExtent, uint framebufferCount, uint framesInFlight)
    {
        vk = VulkanHelper.Vk;
        this.device = device;
        this.commandPool = commandPool;
        this.swapchainExtent = swapchainExtent;

        this.RenderPass = renderPass;
        this.framebufferAttachmentCollections = framebufferAttachmentCollections;

        // Create framebuffers
        this.framebuffers = new Framebuffer[framebufferCount];
        for (uint i = 0; i < framebufferCount; i++)
        {
            var attachments = framebufferAttachmentCollections[i].Attachments;
            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                AttachmentCount = (uint) attachments.Length,
                RenderPass = renderPass,
                Width = swapchainExtent.Width,
                Height = swapchainExtent.Height,
                Layers = 1
            };
            fixed (ImageView* attachmentsPtr = attachments)
                framebufferInfo.PAttachments = attachmentsPtr;

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out framebuffers[i]) != Result.Success)
            {
                throw new Exception("Failed to create framebuffer!");
            }
        }

        // Create command buffers
        var tmpCommandBuffers = new CommandBuffer[framesInFlight];
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = framesInFlight 
        };

        fixed (CommandBuffer* commandBuffersPtr = tmpCommandBuffers)
        {
            if (vk.AllocateCommandBuffers(device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
        commandBuffers = tmpCommandBuffers;

        // Create signal semaphores
        this.signalSemaphores = new Semaphore[framesInFlight];
        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        for (uint i = 0; i < framesInFlight; i++) 
        {
            if (vk.CreateSemaphore(device, in semaphoreInfo, null, out signalSemaphores[i]) != Result.Success)
            {
                throw new Exception("Failed to create semaphores!");
            }
        }
    }

    /// <summary>
    /// Begins a render stage to allow for drawing commands. Implicitly begins the command buffer
    /// at commandBufferIndex and begins a render pass to render to the framebuffer at framebufferIndex.
    /// Furthemore, sets the viewport and scissor to be the full size of the swapchain image.
    /// </summary>
    /// <param name="commandBufferIndex">The index of the command buffer to use for rendering.</param>
    /// <param name="framebufferIndex">The index of the framebuffer to render to.</param>
    public void BeginCommands(uint commandBufferIndex, uint framebufferIndex)
    {
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        if (vk.BeginCommandBuffer(commandBuffers[commandBufferIndex], in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }

        RenderPassBeginInfo renderPassBeginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = RenderPass,
            Framebuffer = framebuffers[framebufferIndex],
            RenderArea = 
            {
                Extent = swapchainExtent,
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
            Width = swapchainExtent.Width,
            Height = swapchainExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        vk.CmdSetViewport(commandBuffers[commandBufferIndex], 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = swapchainExtent
        };
        vk.CmdSetScissor(commandBuffers[commandBufferIndex], 0, 1, in scissor);
    }

    /// <summary>
    /// Ends a render stage's drawing commands. Implicitly ends the command buffer at commandBufferIndex.
    /// </summary>
    /// <param name="commandBufferIndex">The index of the command buffer that was used for rendering.</param>
    public void EndCommands(uint commandBufferIndex)
    {
        vk.CmdEndRenderPass(commandBuffers[commandBufferIndex]);
        if (vk.EndCommandBuffer(commandBuffers[commandBufferIndex]) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }
    }

    public void SubmitCommands(Queue queue, uint commandBufferIndex, Semaphore[] waitSemaphores, Fence? inFlightFence = null)
    {
        var signalSemaphore = signalSemaphores[commandBufferIndex];
        var commandBuffer = commandBuffers[commandBufferIndex];
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            PWaitDstStageMask = &waitStage,
            WaitSemaphoreCount = (uint) waitSemaphores.Length
        };
        fixed (Semaphore* waitSemaphoresPtr = waitSemaphores)
            submitInfo.PWaitSemaphores = waitSemaphoresPtr;

        if (inFlightFence.HasValue)
        {
            if (vk.QueueSubmit(queue, 1, in submitInfo, inFlightFence.Value) != Result.Success)
            {
                throw new Exception("Failed to submit queue!");
            }
        }
        else
        {
            if (vk.QueueSubmit(queue, 1, in submitInfo, default) != Result.Success)
            {
                throw new Exception("Failed to submit queue!");
            }
        }
    }

    public CommandBuffer GetCommandBuffer(uint commandBufferIndex)
    {
        return commandBuffers[commandBufferIndex];
    }

    public void ResetCommandBuffer(uint commandBufferIndex)
    {
        vk.ResetCommandBuffer(commandBuffers[commandBufferIndex], CommandBufferResetFlags.None);
    }

    public Semaphore GetSignalSemaphore(uint semaphoreIndex)
    {
        return signalSemaphores[semaphoreIndex];
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyRenderPass(device, RenderPass, null);

            fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
                vk.FreeCommandBuffers(device, commandPool, (uint) commandBuffers.Length, commandBuffersPtr);
            
            foreach (var framebuffer in framebuffers)
            {
                vk.DestroyFramebuffer(device, framebuffer, null);
            }

            foreach (var framebufferAttachmentCollection in framebufferAttachmentCollections)
            {
                framebufferAttachmentCollection.Dispose();
            }

            foreach (var semaphore in signalSemaphores)
            {
                vk.DestroySemaphore(device, semaphore, null);
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
