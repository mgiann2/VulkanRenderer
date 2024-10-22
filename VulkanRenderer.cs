using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Buffer = Silk.NET.Vulkan.Buffer;

struct QueueFamilyIndices
{
    public uint? GraphicsFamily { get; set; }
    public uint? PresentFamily { get; set; }

    public bool IsComplete()
    {
        return GraphicsFamily.HasValue && PresentFamily.HasValue;
    }
}

struct SwapChainSupportDetails
{
    public SurfaceCapabilitiesKHR Capabilities;
    public SurfaceFormatKHR[] Formats;
    public PresentModeKHR[] PresentModes;
}

public struct UniformBufferObject
{
    public Matrix4X4<float> model;
    public Matrix4X4<float> view;
    public Matrix4X4<float> proj;
}

unsafe public partial class VulkanRenderer : IDisposable
{
    enum RendererState
    {
        Idle,
        DrawingFrame,
        UsingRenderPass
    }

    readonly bool EnableValidationLayers;
    const int MaxFramesInFlight = 2;

    readonly string[] validationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName
    };

    readonly Vk vk;
    readonly Device device; 
    readonly PhysicalDevice physicalDevice;

    IWindow window;
    IVkSurface windowSurface;
    Instance instance;

    KhrSurface khrSurface;
    SurfaceKHR surface;

    Queue graphicsQueue;
    Queue presentQueue;

    KhrSwapchain khrSwapchain;
    SwapchainKHR swapchain;
    Image[] swapchainImages;
    ImageView[] swapchainImageViews;
    Extent2D swapchainExtent;
    Format swapchainImageFormat;
    Framebuffer[] swapchainFramebuffers;

    RenderPass renderPass;
    PipelineLayout pipelineLayout;
    Pipeline graphicsPipeline;

    Buffer[] uniformBuffers;
    DeviceMemory[] uniformBufferMemory;

    DescriptorSetLayout uboDescriptorSetLayout;
    DescriptorPool uboDescriptorPool;
    DescriptorSet[] uboDescriptorSets;

    DescriptorSetLayout samplerDescriptorSetLayout;
    DescriptorPool samplerDescriptorPool;

    CommandPool commandPool;
    CommandBuffer[] commandBuffers;

    Sampler textureSampler;

    GBuffer gBuffer;

    Image depthImage;
    ImageView depthImageView;
    DeviceMemory depthImageMemory;

    Semaphore[] imageAvailableSemaphores;
    Semaphore[] renderFinishedSemaphores;
    Fence[] inFlightFences;

    uint currentFrame;
    bool framebufferResized = false;
    uint imageIndex;
    RendererState rendererState = RendererState.Idle;

    bool disposedValue;
    
    public VulkanRenderer(IWindow window, bool enableValidationLayers = false)
    {
        vk = Vk.GetApi();
        
        this.window = window;
        if (window.VkSurface == null) throw new Exception("No vk surface exists on window!");
        windowSurface = window.VkSurface;

        EnableValidationLayers = enableValidationLayers;
        
        CreateInstance(out instance);
        CreateSurface(out khrSurface, out surface);
        PickPhysicalDevice(out physicalDevice);
        CreateLogicalDevice(out device, out graphicsQueue, out presentQueue);
        CreateSwapchain(out khrSwapchain, out swapchain, out swapchainImages, out swapchainImageFormat, out swapchainExtent);
        CreateImageViews(out swapchainImageViews);
        CreateRenderPass(out renderPass);
        CreateDescriptorSetLayouts(out uboDescriptorSetLayout, out samplerDescriptorSetLayout);
        CreateGraphicsPipeline(out graphicsPipeline, out pipelineLayout, "shaders/tmp_vert.spv", "shaders/tmp_frag.spv");
        CreateDepthResources(out depthImage, out depthImageMemory, out depthImageView);
        CreateFramebuffers(out swapchainFramebuffers);
        CreateCommandPool(out commandPool);
        CreateTextureSampler(out textureSampler);
        CreateCommandBuffers(out commandBuffers);
        CreateUniformBuffers(out uniformBuffers, out uniformBufferMemory);
        CreateDescriptorPools(out uboDescriptorPool, out samplerDescriptorPool);
        CreateUBODescriptorSets(out uboDescriptorSets);
        CreateSyncObjects(out imageAvailableSemaphores, out renderFinishedSemaphores, out inFlightFences);

        window.FramebufferResize += OnFramebufferResize;
    }

    /// <summary>
    /// Initializes renderer to start drawing new frame
    /// </summary>
    public void BeginFrame()
    {
        if (rendererState != RendererState.Idle) throw new Exception("BeginFrame called before current frame has been ended!");

        vk.WaitForFences(device, 1, ref inFlightFences[currentFrame], true, ulong.MaxValue);

        imageIndex = 0;
        var result = khrSwapchain.AcquireNextImage(device, swapchain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default, ref imageIndex);
        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("Failed to acquire next image!");
        }

        vk.ResetFences(device, 1, ref inFlightFences[currentFrame]);

        // Begin command buffer
        vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };

        if (vk.BeginCommandBuffer(commandBuffers[currentFrame], in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }

        rendererState = RendererState.DrawingFrame;
    }

    /// <summary>
    /// Submits a new frame to be drawn
    /// </summary>
    public void EndFrame()
    {
        if (rendererState != RendererState.DrawingFrame) throw new Exception("EndFrame called either frame has been started or render pass commands have been submitted!");

        var commandBuffer = commandBuffers[currentFrame];
        if (vk.EndCommandBuffer(commandBuffer) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }

        var waitSemaphores = stackalloc[] { imageAvailableSemaphores[currentFrame] };
        var waitStages = stackalloc[] { PipelineStageFlags.ColorAttachmentOutputBit };
        var signalSemaphores = stackalloc[] { renderFinishedSemaphores[currentFrame] };

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores
        };

        if (vk.QueueSubmit(graphicsQueue, 1, in submitInfo, inFlightFences[currentFrame]) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer!");
        }

        var swapchains = stackalloc[] { swapchain };

        uint idx = imageIndex;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapchains,
            PImageIndices = &idx
        };

        var result = khrSwapchain.QueuePresent(presentQueue, in presentInfo);
        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || framebufferResized)
        {
            framebufferResized = false;
            RecreateSwapchain();
        }
        else if (result != Result.Success)
        {
            throw new Exception("Failed to present swapchain image!");
        }

        currentFrame = (currentFrame + 1) % MaxFramesInFlight;
        rendererState = RendererState.Idle;
    }

    public void BeginRenderPass()
    {
        if (rendererState != RendererState.DrawingFrame) throw new Exception("Renderer either has not started a frame or has already started a render pass!");

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = swapchainFramebuffers[imageIndex],
            RenderArea = new()
            {
                Extent = swapchainExtent,
                Offset = { X = 0, Y = 0 }
            }
        };

        ClearValue[] clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };

        renderPassInfo.ClearValueCount = (uint) clearColors.Length;
        fixed (ClearValue* clearColorsPtr = clearColors)
            renderPassInfo.PClearValues = clearColorsPtr;

        vk.CmdBeginRenderPass(commandBuffers[currentFrame], in renderPassInfo, SubpassContents.Inline);

        vk.CmdBindPipeline(commandBuffers[currentFrame], PipelineBindPoint.Graphics, graphicsPipeline);

        Viewport viewport = new()
        {
            X = 0.0f,
            Y = 0.0f,
            Width = swapchainExtent.Width,
            Height = swapchainExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        vk.CmdSetViewport(commandBuffers[currentFrame], 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = swapchainExtent
        };
        vk.CmdSetScissor(commandBuffers[currentFrame], 0, 1, in scissor);

        // TODO: Determine if there is better location for binding descriptor sets
        vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics,
                                 pipelineLayout, 0, 1, in uboDescriptorSets[currentFrame], 0, default);

        rendererState = RendererState.UsingRenderPass;
    }

    public void EndRenderPass()
    {
        if (rendererState != RendererState.UsingRenderPass) throw new Exception("Tried to end render pass before beginning a frame or render pass!");

        vk.CmdEndRenderPass(commandBuffers[currentFrame]);

        rendererState = RendererState.DrawingFrame;
    }

    public void DeviceWaitIdle()
    {
        vk.DeviceWaitIdle(device);
    }

    public void Draw(uint vertexCount)
    {
        vk.CmdDraw(commandBuffers[currentFrame], vertexCount, 1, 0, 0);
    }

    public void DrawIndexed(uint indexCount)
    {
        vk.CmdDrawIndexed(commandBuffers[currentFrame], indexCount, 1, 0, 0, 0);
    }

    public void UpdateUniformBuffer(UniformBufferObject ubo)
    {
        void* data;
        vk.MapMemory(device, uniformBufferMemory[currentFrame], 0, (ulong)Unsafe.SizeOf<UniformBufferObject>(), 0, &data);
        new Span<UniformBufferObject>(data, 1)[0] = ubo;
        vk.UnmapMemory(device, uniformBufferMemory[currentFrame]);
    }

    void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer newBuffer, out DeviceMemory newBufferMemory)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, in bufferInfo, null, out newBuffer) != Result.Success)
        {
            throw new Exception("Failed to create buffer!");
        }

        MemoryRequirements memoryRequirements;
        vk.GetBufferMemoryRequirements(device, newBuffer, out memoryRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, properties)
        };

        if (vk.AllocateMemory(device, in allocInfo, null, out newBufferMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate buffer memory!");
        }

        vk.BindBufferMemory(device, newBuffer, newBufferMemory, 0);
    }

    void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        var commandBuffer = BeginSingleTimeCommand();

        BufferCopy copyRegion = new() { Size = size };
        vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, in copyRegion);

        EndSingleTimeCommand(commandBuffer);
    }

    uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
                return (uint) i;
        }

        throw new Exception("Unable to find suitable memory type!");
    }

    void OnFramebufferResize(Vector2D<int> framebufferSize)
    {
        framebufferResized = true;
    }

    void CreateInstance(out Instance instance)
    {
        if (EnableValidationLayers && !CheckValidationLayerSupport())
        {
            throw new Exception("Requested validation layers, but not available!");
        }

        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*) Marshal.StringToHGlobalAnsi("Vulkan Renderer"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*) Marshal.StringToHGlobalAnsi("No Engine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version13
        };

        var extensions = GetRequiredExtensions();

        InstanceCreateInfo instanceInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint) extensions.Count(),
            PpEnabledExtensionNames = (byte**) SilkMarshal.StringArrayToPtr(extensions),
            EnabledLayerCount = 0,
            PNext = null
        };

        if (EnableValidationLayers)
        {
            instanceInfo.EnabledLayerCount = (uint) validationLayers.Count();
            instanceInfo.PpEnabledLayerNames = (byte**) SilkMarshal.StringArrayToPtr(validationLayers);

            DebugUtilsMessengerCreateInfoEXT messengerInfo = new()
            {
                SType = StructureType.DebugUtilsMessengerCreateInfoExt,
                MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                  DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                  DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
                MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                              DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                              DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
                PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT) DebugCallback
            };
            instanceInfo.PNext = &messengerInfo;
        }

        if (vk.CreateInstance(in instanceInfo, null, out instance) != Result.Success)
        {
            throw new Exception("Failed to create instance!");
        }

        // free unmanaged memory
        Marshal.FreeHGlobal((nint) appInfo.PEngineName);
        Marshal.FreeHGlobal((nint) appInfo.PApplicationName);
        SilkMarshal.Free((nint) instanceInfo.PpEnabledExtensionNames);
        if (EnableValidationLayers) SilkMarshal.Free((nint) instanceInfo.PpEnabledLayerNames);
    }

    void CreateSurface(out KhrSurface khrSurface, out SurfaceKHR surface)
    {
        if (!vk.TryGetInstanceExtension(instance, out khrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found!");
        }
        
        surface = windowSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }

    void PickPhysicalDevice(out PhysicalDevice physicalDevice)
    {
        var devices = vk.GetPhysicalDevices(instance);

        foreach (var device in devices)
        {
            if (IsPhysicalDeviceSuitable(device))
            {
                physicalDevice = device;
                return;
            }
        }

        throw new Exception("Unable to find suitable physical device!");
    }

    bool IsPhysicalDeviceSuitable(PhysicalDevice physicalDevice)
    {
        var indices = FindQueueFamilies(physicalDevice);

        bool extensionsSupported = CheckDeviceExtensionSupport(physicalDevice);

        bool swapChainAdequate = false;
        if (extensionsSupported)
        {
            var swapChainSupportDetails = QuerySwapChainSupport(physicalDevice);
            swapChainAdequate = swapChainSupportDetails.PresentModes.Any() && swapChainSupportDetails.Formats.Any();
        }

        PhysicalDeviceFeatures supportedFeatures;
        vk.GetPhysicalDeviceFeatures(physicalDevice, out supportedFeatures);

        return indices.IsComplete() && extensionsSupported && swapChainAdequate && supportedFeatures.SamplerAnisotropy;
    }

    void CreateLogicalDevice(out Device logicalDevice, out Queue graphicsQueue, out Queue presentationQueue)
    {
        var indices = FindQueueFamilies(physicalDevice);

        var uniqueQueueFamilies = new[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };
        uniqueQueueFamilies = uniqueQueueFamilies.Distinct().ToArray();

        using var mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
        var queueCreateInfos = (DeviceQueueCreateInfo*) Unsafe.AsPointer(ref mem.GetPinnableReference());

        float queuePriority = 1.0f;
        for (int i = 0; i < uniqueQueueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new()
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueQueueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        PhysicalDeviceFeatures deviceFeatures = new();
        deviceFeatures.SamplerAnisotropy = true;

        DeviceCreateInfo deviceInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            PQueueCreateInfos = queueCreateInfos,
            QueueCreateInfoCount = (uint) uniqueQueueFamilies.Length,
            PEnabledFeatures = &deviceFeatures,
            EnabledExtensionCount = (uint) deviceExtensions.Length,
            PpEnabledExtensionNames = (byte**) SilkMarshal.StringArrayToPtr(deviceExtensions)
        };

        if (EnableValidationLayers)
        {
            deviceInfo.EnabledLayerCount = (uint) validationLayers.Length;
            deviceInfo.PpEnabledLayerNames = (byte**) SilkMarshal.StringArrayToPtr(validationLayers);
        }
        else
        {
            deviceInfo.EnabledLayerCount = 0;
        }

        if (vk.CreateDevice(physicalDevice, in deviceInfo, null, out logicalDevice) != Result.Success)
        {
            throw new Exception("Failed to create logical device!");
        }

        vk.GetDeviceQueue(logicalDevice, indices.GraphicsFamily!.Value, 0, out graphicsQueue);
        vk.GetDeviceQueue(logicalDevice, indices.PresentFamily!.Value, 0, out presentationQueue);

        if (EnableValidationLayers) SilkMarshal.Free((nint) deviceInfo.PpEnabledLayerNames);
        SilkMarshal.Free((nint) deviceInfo.PpEnabledExtensionNames);
    }

    void CreateSwapchain(out KhrSwapchain khrSwapchain, out SwapchainKHR swapchain,
            out Image[] swapchainImages, out Format swapchainImageFormat, out Extent2D swapchainExtent)
    {
        var swapchainSupportDetails = QuerySwapChainSupport(physicalDevice);

        var surfaceFormat = ChooseSwapSurfaceFormat(swapchainSupportDetails.Formats);
        var presentMode = ChooseSwapPresentMode(swapchainSupportDetails.PresentModes);
        var extent = ChooseSwapExtent(swapchainSupportDetails.Capabilities);

        uint imageCount = swapchainSupportDetails.Capabilities.MinImageCount + 1;
        if (swapchainSupportDetails.Capabilities.MaxImageCount > 0 && imageCount > swapchainSupportDetails.Capabilities.MaxImageCount)
        {
            imageCount = swapchainSupportDetails.Capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR swapchainInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit
        };

        var indices = FindQueueFamilies(physicalDevice);
        var queueFamilyIndices = stackalloc[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };

        if (indices.GraphicsFamily != indices.PresentFamily)
        {
            swapchainInfo.ImageSharingMode = SharingMode.Concurrent;
            swapchainInfo.QueueFamilyIndexCount = 2;
            swapchainInfo.PQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            swapchainInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        swapchainInfo.PreTransform = swapchainSupportDetails.Capabilities.CurrentTransform;
        swapchainInfo.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
        swapchainInfo.PresentMode = presentMode;
        swapchainInfo.Clipped = true;
        swapchainInfo.OldSwapchain = default;

        if (!vk.TryGetDeviceExtension(instance, device, out khrSwapchain))
        {
            throw new Exception("VK_KHR_swapchain extension not found!");
        }

        if (khrSwapchain.CreateSwapchain(device, in swapchainInfo, null, out swapchain) != Result.Success)
        {
            throw new Exception("Failded to create swapchain!");
        }

        uint swapchainImageCount = 0;
        khrSwapchain.GetSwapchainImages(device, swapchain, ref swapchainImageCount, null);
        swapchainImages = new Image[swapchainImageCount];
        fixed (Image* swapchainImagesPtr = swapchainImages)
        {
            khrSwapchain.GetSwapchainImages(device, swapchain, ref swapchainImageCount, swapchainImagesPtr);
        }

        swapchainImageFormat = surfaceFormat.Format;
        swapchainExtent = extent;
    }

    void CleanupSwapchain()
    {
        vk.DestroyImageView(device, depthImageView, null);
        vk.FreeMemory(device, depthImageMemory, null);
        vk.DestroyImage(device, depthImage, null);

        foreach (var framebuffer in swapchainFramebuffers)
        {
            vk.DestroyFramebuffer(device, framebuffer, null);
        }

        foreach (var imageView in swapchainImageViews)
        {
            vk.DestroyImageView(device, imageView, null);
        }

        khrSwapchain.DestroySwapchain(device, swapchain, null);
    }

    void RecreateSwapchain()
    {
        Vector2D<int> framebufferSize = window.FramebufferSize;

        // wait for window to be unminimized
        while (framebufferSize.X == 0 || framebufferSize.Y == 0)
        {
            framebufferSize = window.FramebufferSize;
            window.DoEvents();
        }
        
        vk.DeviceWaitIdle(device);

        CreateSwapchain(out khrSwapchain, out swapchain, out swapchainImages, out swapchainImageFormat, out swapchainExtent);
        CreateImageViews(out swapchainImageViews);
        CreateDepthResources(out depthImage, out depthImageMemory, out depthImageView);
        CreateFramebuffers(out swapchainFramebuffers);
    }

    void CreateImageViews(out ImageView[] imageViews) 
    {
        imageViews = new ImageView[swapchainImages.Length];

        for (int i = 0; i < swapchainImages.Length; i++)
        {
            imageViews[i] = CreateImageView(swapchainImages[i], swapchainImageFormat, ImageAspectFlags.ColorBit);
        }
    }

    void CreateRenderPass(out RenderPass renderPass)
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = swapchainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentReference colorAttachmentRef = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        AttachmentDescription depthAttachment = new()
        {
            Format = FindDepthFormat(),
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };
        
        AttachmentReference depthAttachmentRef = new()
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
            PDepthStencilAttachment = &depthAttachmentRef
        };

        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };

        var attachments = new AttachmentDescription[] { colorAttachment, depthAttachment };

        RenderPassCreateInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        renderPassInfo.AttachmentCount = (uint) attachments.Length;
        fixed (AttachmentDescription* attachmentsPtr = attachments)
            renderPassInfo.PAttachments = attachmentsPtr;

        if (vk.CreateRenderPass(device, in renderPassInfo, null, out renderPass) != Result.Success)
        {
            throw new Exception("Failed to create render pass!");
        }
    }

    void CreateDescriptorSetLayouts(out DescriptorSetLayout uboDescriptorSetLayout, out DescriptorSetLayout samplerDescriptorSetLayout) 
    {
        // uniform buffer descriptor set layout
        DescriptorSetLayoutBinding uboLayoutBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit
        };

        DescriptorSetLayoutCreateInfo uboLayoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &uboLayoutBinding 
        };
    
        if (vk.CreateDescriptorSetLayout(device, in uboLayoutInfo, null, out uboDescriptorSetLayout) != Result.Success)
        {
            throw new Exception("Failed to create uniform buffer object descriptor set layout!");
        }

        // image sampler descriptor set layout
        DescriptorSetLayoutBinding samplerLayoutBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutCreateInfo samplerLayoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &samplerLayoutBinding
        };

        if (vk.CreateDescriptorSetLayout(device, in samplerLayoutInfo, null, out samplerDescriptorSetLayout) != Result.Success)
        {
            throw new Exception("Failed to create sampler descriptor set layout!");
        }
    }

    void CreateGraphicsPipeline(out Pipeline graphicsPipeline, out PipelineLayout layout, string vertexShaderPath, string fragmentShaderPath)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(vertexShaderPath);
        byte[] fragmentShaderCode = File.ReadAllBytes(fragmentShaderPath);

        var vertexShaderModule = CreateShaderModule(vertexShaderCode);
        var fragmentShaderModule = CreateShaderModule(fragmentShaderCode);

        PipelineShaderStageCreateInfo vertexShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertexShaderModule,
            PName = (byte*) SilkMarshal.StringToPtr("main")
        };

        PipelineShaderStageCreateInfo fragmentShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragmentShaderModule,
            PName = (byte*) SilkMarshal.StringToPtr("main")
        };

        var shaderStagesInfo = stackalloc[] { vertexShaderStageInfo, fragmentShaderStageInfo };

        var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor };
        PipelineDynamicStateCreateInfo dynamicStateInfo = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        PipelineInputAssemblyStateCreateInfo assemblyInfo = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false,
        };

        PipelineViewportStateCreateInfo viewportInfo = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        PipelineRasterizationStateCreateInfo rasterizerInfo = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1.0f,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.CounterClockwise
        };

        PipelineMultisampleStateCreateInfo multisampleInfo = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        PipelineColorBlendAttachmentState colorBlendAttachment = new()
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false
        };

        PipelineColorBlendStateCreateInfo colorBlendInfo = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment
        };

        PipelineDepthStencilStateCreateInfo depthStencil = new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less,
            DepthBoundsTestEnable = false
        };

        DescriptorSetLayout[] descriptorSetLayouts = new[] { uboDescriptorSetLayout, samplerDescriptorSetLayout };

        fixed (DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts)
        {
            PipelineLayoutCreateInfo pipelineLayoutInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint) descriptorSetLayouts.Length,
                PSetLayouts = descriptorSetLayoutsPtr
            };

            if (vk.CreatePipelineLayout(device, in pipelineLayoutInfo, null, out layout) != Result.Success)
            {
                throw new Exception("Failed to create pipeline layout!");
            }
        }

        var bindingDescription = Vertex.GetBindingDescription();
        var attributeDescriptions = Vertex.GetAttributeDescriptions();
        fixed (VertexInputAttributeDescription* attributeDescriptionsPtr = attributeDescriptions)
        {
            PipelineVertexInputStateCreateInfo vertexInfo = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDescription,
                VertexAttributeDescriptionCount = (uint) attributeDescriptions.Length,
                PVertexAttributeDescriptions = attributeDescriptionsPtr
            };

            GraphicsPipelineCreateInfo pipelineInfo = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStagesInfo,
                PInputAssemblyState = &assemblyInfo,
                PVertexInputState = &vertexInfo,
                PViewportState = &viewportInfo,
                PRasterizationState = &rasterizerInfo,
                PMultisampleState = &multisampleInfo,
                PColorBlendState = &colorBlendInfo,
                PDynamicState = &dynamicStateInfo,
                PDepthStencilState = &depthStencil,
                Layout = layout,
                RenderPass = renderPass,
                Subpass = 0
            };

            if (vk.CreateGraphicsPipelines(device, default, 1, in pipelineInfo, null, out graphicsPipeline) != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline!");
            }
        }

        vk.DestroyShaderModule(device, vertexShaderModule, null);
        vk.DestroyShaderModule(device, fragmentShaderModule, null);
    }

    ShaderModule CreateShaderModule(byte[] shaderCode)
    {
        ShaderModuleCreateInfo createInfo = new()
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint) shaderCode.Length,
        };

        ShaderModule shaderModule;

        fixed (byte* shaderCodePtr = shaderCode)
        {
            createInfo.PCode = (uint*) shaderCodePtr;

            if (vk.CreateShaderModule(device, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader!");
            }
        }

        return shaderModule;
    }

    void CreateUniformBuffers(out Buffer[] uniformBuffers, out DeviceMemory[] uniformBuffersMemory)
    {
        ulong bufferSize = (ulong) (Unsafe.SizeOf<UniformBufferObject>());

        uniformBuffers = new Buffer[MaxFramesInFlight];
        uniformBuffersMemory = new DeviceMemory[MaxFramesInFlight];

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            CreateBuffer(bufferSize, BufferUsageFlags.UniformBufferBit,
                         MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                         out uniformBuffers[i], out uniformBuffersMemory[i]);
        }
    }

    void CreateDescriptorPools(out DescriptorPool uboDescriptorPool, out DescriptorPool samplerDescriptorPool)
    {
        DescriptorPoolSize uboPoolSize = new()
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = (uint) MaxFramesInFlight
        };

        DescriptorPoolCreateInfo uboPoolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &uboPoolSize,
            MaxSets = (uint) MaxFramesInFlight
        };

        if (vk.CreateDescriptorPool(device, in uboPoolInfo, null, out uboDescriptorPool) != Result.Success)
        {
            throw new Exception("Failed to creat uniform buffer object descriptor pool!");
        }

        DescriptorPoolSize samplerPoolSize =  new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint) MaxFramesInFlight * 2,
        };

        DescriptorPoolCreateInfo samplerPoolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &samplerPoolSize,
            MaxSets = (uint) MaxFramesInFlight * 2,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
        };

        if (vk.CreateDescriptorPool(device, in samplerPoolInfo, null, out samplerDescriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create sampler descriptor pool!");
        }
    }

    void CreateUBODescriptorSets(out DescriptorSet[] uboDescriptorSets)
    {
        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, uboDescriptorSetLayout);

        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = uboDescriptorPool,
                DescriptorSetCount = (uint) MaxFramesInFlight,
                PSetLayouts = layoutsPtr
            };

            uboDescriptorSets = new DescriptorSet[MaxFramesInFlight];
            fixed (DescriptorSet* descriptorSetsPtr = uboDescriptorSets)
            {
                if (vk.AllocateDescriptorSets(device, in allocInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate uniform buffer object descriptor sets!");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorBufferInfo bufferInfo = new()
            {
                Buffer = uniformBuffers[i],
                Offset = 0,
                Range = (ulong) Unsafe.SizeOf<UniformBufferObject>()
            };

            WriteDescriptorSet descriptorWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = uboDescriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            vk.UpdateDescriptorSets(device, 1, &descriptorWrite, 0, default);
        }
    }

    void CreateFramebuffers(out Framebuffer[] framebuffers)
    {
        framebuffers = new Framebuffer[swapchainImageViews.Length];

        for (int i = 0; i < swapchainImageViews.Length; i++)
        {
            var attachments = new ImageView[] { swapchainImageViews[i], depthImageView };

            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass,
                Width = swapchainExtent.Width,
                Height = swapchainExtent.Height,
                Layers = 1
            };

            framebufferInfo.AttachmentCount = (uint) attachments.Length;
            fixed (ImageView* attachmentsPtr = attachments)
                framebufferInfo.PAttachments = attachmentsPtr;

            if (vk.CreateFramebuffer(device, in framebufferInfo, null, out framebuffers[i]) != Result.Success)
            {
                throw new Exception("Failed to create framebuffer!");
            }
        }
    }

    void CreateCommandPool(out CommandPool commandPool)
    {
        var queueFamilyIndices = FindQueueFamilies(physicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamilyIndices.GraphicsFamily!.Value
        };

        if (vk.CreateCommandPool(device, in poolInfo, null, out commandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool!");
        }
    }

    void CreateDepthResources(out Image depthImage, out DeviceMemory depthImageMemory, out ImageView depthImageView)
    {
        var depthFormat = FindDepthFormat();
        CreateImage(swapchainExtent.Width, swapchainExtent.Height, depthFormat,
                    ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit,
                    MemoryPropertyFlags.DeviceLocalBit, out depthImage, out depthImageMemory);
        depthImageView = CreateImageView(depthImage, depthFormat, ImageAspectFlags.DepthBit);
    }

    bool HasStencilComponent(Format format)
    {
        return format == Format.D32SfloatS8Uint || format == Format.D24UnormS8Uint;
    }

    Format FindDepthFormat()
    {
        var candidates = new Format[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint };
        return FindSupportedFormat(candidates, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
    }

    Format FindSupportedFormat(Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
    {
        foreach (var format in candidates)
        {
            vk.GetPhysicalDeviceFormatProperties(physicalDevice, format, out FormatProperties props);
            if (tiling == ImageTiling.Linear && (props.LinearTilingFeatures & features) == features)
            {
                return format;
            }
            else if (tiling == ImageTiling.Optimal && (props.OptimalTilingFeatures & features) == features)
            {
                return format;
            }
        }

        throw new Exception("Failed to find supported format!");
    }

    void CreateTextureSampler(out Sampler textureSampler)
    {
        PhysicalDeviceProperties properties;
        vk.GetPhysicalDeviceProperties(physicalDevice, out properties);

        SamplerCreateInfo samplerInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = true,
            MaxAnisotropy = properties.Limits.MaxSamplerAnisotropy,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0.0f,
            MinLod = 0.0f,
            MaxLod = 0.0f
        };

        if (vk.CreateSampler(device, in samplerInfo, null, out textureSampler) != Result.Success)
        {
            throw new Exception("Failed to create texture sampler!");
        }
    }

    void CreateImage(uint width, uint height, Format format, ImageTiling tiling,
                     ImageUsageFlags usage, MemoryPropertyFlags properties,
                     out Image image, out DeviceMemory imageMemory)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new() { Width = width, Height = height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit
        };

        if (vk.CreateImage(device, in imageInfo, null, out image) != Result.Success)
        {
            throw new Exception("Failed to create textue image!");
        }

        MemoryRequirements memRequirements;
        vk.GetImageMemoryRequirements(device, image, out memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        if (vk.AllocateMemory(device, in allocInfo, null, out imageMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate texture image memory!");
        }

        vk.BindImageMemory(device, image, imageMemory, 0);
    }

    void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var commandBuffer = BeginSingleTimeCommand();

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;

            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else
        {
            throw new Exception("Unsupported layout transition!");
        }

        vk.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage,
                              0, 0, default, 0, default, 1, in barrier);

        EndSingleTimeCommand(commandBuffer);
    }

    void CopyBufferToImage(Buffer buffer, Image image, uint width, uint height)
    {
        var commandBuffer = BeginSingleTimeCommand();

        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new() { X = 0, Y = 0, Z = 0 },
            ImageExtent = new() { Width = width, Height = height, Depth = 1 }
        };

        vk.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, in region);

        EndSingleTimeCommand(commandBuffer);
    }

    ImageView CreateImageView(Image image, Format format, ImageAspectFlags aspectFlags)
    {
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            Format = format,
            ViewType = ImageViewType.Type2D,
            SubresourceRange = new()
            {
                AspectMask = aspectFlags,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        if (vk.CreateImageView(device, in viewInfo, null, out var imageView) != Result.Success)
        {
            throw new Exception("Failed to create image view!");
        }
        return imageView;
    }

    void CreateCommandBuffers(out CommandBuffer[] commandBuffers)
    {
        commandBuffers = new CommandBuffer[MaxFramesInFlight];

        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint) commandBuffers.Length
        };

        fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
        {
            if (vk.AllocateCommandBuffers(device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
    }

    void CreateSyncObjects(out Semaphore[] imageAvailableSemaphores, out Semaphore[] renderFinishedSemaphores, out Fence[] inFlightFences)
    {
        imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
        renderFinishedSemaphores = new Semaphore[MaxFramesInFlight];
        inFlightFences = new Fence[MaxFramesInFlight];

        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (vk.CreateSemaphore(device, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                vk.CreateSemaphore(device, in semaphoreInfo, null, out renderFinishedSemaphores[i]) != Result.Success ||
                vk.CreateFence(device, in fenceInfo, null, out inFlightFences[i]) != Result.Success)
            {
                throw new Exception("Failed to create sync objects!");
            }
        }
    }

    CommandBuffer BeginSingleTimeCommand()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = commandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        vk.AllocateCommandBuffers(device, in allocInfo, out commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        vk.BeginCommandBuffer(commandBuffer, in beginInfo);
        return commandBuffer;
    }

    void EndSingleTimeCommand(CommandBuffer commandBuffer)
    {
        vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        vk.QueueSubmit(graphicsQueue, 1, in submitInfo, default);
        vk.QueueWaitIdle(graphicsQueue);

        vk.FreeCommandBuffers(device, commandPool, 1, in commandBuffer);
    }

    QueueFamilyIndices FindQueueFamilies(PhysicalDevice physicalDevice)
    {
        QueueFamilyIndices indices = new();

        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, queueFamiliesPtr);
        }
        
        uint i = 0;
        foreach (var queueFamily in queueFamilies)
        {
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, surface, out var presentSupport);

            if (presentSupport)
            {
                indices.PresentFamily = i;
            }

            if (indices.IsComplete())
            {
                break;
            }

            i++;
        }

        return indices;
    }

    bool CheckDeviceExtensionSupport(PhysicalDevice physicalDevice)
    {
        uint extensionCount = 0;
        vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*) null, ref extensionCount, null);

        var availableExtensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
        {
            vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*) null, ref extensionCount, availableExtensionsPtr);
        }

        var availableExtensionNames = availableExtensions.Select(extension => Marshal.PtrToStringAnsi((nint) extension.ExtensionName)).ToHashSet();

        return deviceExtensions.All(availableExtensionNames.Contains);
    }

    SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
    {
        var details = new SwapChainSupportDetails();

        khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, out details.Capabilities);

        uint formatCount = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref formatCount, formatsPtr);
            }
        }
        else 
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* presentModesPtr = details.PresentModes)
            {
                khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, ref presentModeCount, presentModesPtr);
            }
        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
    }

    SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] availableFormats)
    {
        foreach (var surfaceFormat in availableFormats)
        {
            if (surfaceFormat.Format == Format.R8G8B8A8Srgb && surfaceFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return surfaceFormat;
            }
        }

        return availableFormats[0];
    }

    PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] availablePresentModes)
    {
        foreach (var presentMode in availablePresentModes)
        {
            if (presentMode == PresentModeKHR.MailboxKhr)
            {
                return presentMode;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }
        else
        {
            var framebufferSize = window.FramebufferSize;

            var actualExtent = new Extent2D() 
            {
                Width = (uint) framebufferSize.X,
                Height = (uint) framebufferSize.Y
            };

            actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
            actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);
            
            return actualExtent;
        }
    }

    string[] GetRequiredExtensions()
    {
        var glfwExtensions = windowSurface.GetRequiredExtensions(out var glfwExtensionCount);
        var extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);
        
        if (EnableValidationLayers)
        {
            return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
        }

        return extensions;
    }

    bool CheckValidationLayerSupport()
    {
        uint layerCount = 0;
        vk.EnumerateInstanceLayerProperties(ref layerCount, null);
        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
        {
            vk.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
        }

        var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((nint) layer.LayerName)).ToHashSet();

        return validationLayers.All(availableLayerNames.Contains);
    }

    uint DebugCallback(
            DebugUtilsMessageSeverityFlagsEXT messageSeverity,
            DebugUtilsMessageTypeFlagsEXT messageType,
            DebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData)
    {
        Console.WriteLine($"Validation layer: {Marshal.PtrToStringAnsi((nint) pCallbackData->PMessage)}");

        return Vk.False;
    }
}

// IDisposable methods
unsafe public partial class VulkanRenderer : IDisposable
{
    ~VulkanRenderer()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // dispose managed objects
            }

            // free unmanaged resources unmanaged objects and override finalizer
            for (int i = 0; i < MaxFramesInFlight; i++)
            {
                vk.DestroySemaphore(device, imageAvailableSemaphores[i], null);
                vk.DestroySemaphore(device, renderFinishedSemaphores[i], null);
                vk.DestroyFence(device, inFlightFences[i], null);
            }

            vk.DestroySampler(device, textureSampler, null);

            vk.DestroyCommandPool(device, commandPool, null);

            CleanupSwapchain();

            vk.DestroyPipelineLayout(device, pipelineLayout, null);

            for (int i = 0; i < MaxFramesInFlight; i++)
            {
                vk.DestroyBuffer(device, uniformBuffers[i], null);
                vk.FreeMemory(device, uniformBufferMemory[i], null);
            }
            vk.DestroyDescriptorPool(device, uboDescriptorPool, null);
            vk.DestroyDescriptorSetLayout(device, uboDescriptorSetLayout, null);

            vk.DestroyRenderPass(device, renderPass, null);

            vk.DestroyDevice(device, null);
            vk.DestroyInstance(instance, null);

            disposedValue = true;
        }
    }
}
