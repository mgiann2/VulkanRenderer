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

// struct UniformBufferObject
// {
//     public Matrix4X4<float> model;
//     public Matrix4X4<float> view;
//     public Matrix4X4<float> proj;
// }

unsafe class VulkanRenderer : IDisposable
{
    enum RendererState
    {
        Idle,
        DrawingFrame,
        UsingRenderPass
    }

    private readonly bool EnableValidationLayers;
    private const int MaxFramesInFlight = 2;

    private readonly string[] validationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    private readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName
    };

    public readonly Vk Vk;
    public readonly Device Device; 
    public readonly PhysicalDevice PhysicalDevice;

    private IWindow window;
    private IVkSurface windowSurface;
    private Instance instance;

    private KhrSurface khrSurface;
    private SurfaceKHR surface;

    public Queue GraphicsQueue;
    private Queue presentQueue;

    private KhrSwapchain khrSwapchain;
    private SwapchainKHR swapchain;
    private Image[] swapchainImages;
    private ImageView[] swapchainImageViews;
    private Extent2D swapchainExtent;
    private Format swapchainImageFormat;
    private Framebuffer[] swapchainFramebuffers;

    private RenderPass renderPass;
    private PipelineLayout pipelineLayout;
    private Pipeline graphicsPipeline;

    public CommandPool CommandPool;
    private CommandBuffer[] commandBuffers;

    private Semaphore[] imageAvailableSemaphores;
    private Semaphore[] renderFinishedSemaphores;
    private Fence[] inFlightFences;

    private uint currentFrame;
    private bool framebufferResized = false;
    private uint imageIndex;
    private RendererState rendererState = RendererState.Idle;

    public CommandBuffer CurrentCommandBuffer => commandBuffers[currentFrame];

    private bool disposedValue;

    public VulkanRenderer(IWindow window, bool enableValidationLayers = false)
    {
        Vk = Vk.GetApi();
        
        this.window = window;
        if (window.VkSurface == null) throw new Exception("No vk surface exists on window!");
        windowSurface = window.VkSurface;

        EnableValidationLayers = enableValidationLayers;
        
        CreateInstance(out instance);
        CreateSurface(out khrSurface, out surface);
        PickPhysicalDevice(out PhysicalDevice);
        CreateLogicalDevice(out Device, out GraphicsQueue, out presentQueue);
        CreateSwapchain(out khrSwapchain, out swapchain, out swapchainImages, out swapchainImageFormat, out swapchainExtent);
        CreateImageViews(out swapchainImageViews);
        CreateRenderPass(out renderPass);
        CreateGraphicsPipeline(out graphicsPipeline, out pipelineLayout, "shaders/tmp_vert.spv", "shaders/tmp_frag.spv");
        CreateFramebuffers(out swapchainFramebuffers);
        CreateCommandPool(out CommandPool);
        CreateCommandBuffers(out commandBuffers);
        CreateSyncObjects(out imageAvailableSemaphores, out renderFinishedSemaphores, out inFlightFences);

        window.FramebufferResize += OnFramebufferResize;
    }

    /// <summary>
    /// Initializes renderer to start drawing new frame
    /// </summary>
    public void BeginFrame()
    {
        if (rendererState != RendererState.Idle) throw new Exception("BeginFrame called before current frame has been ended!");

        Vk.WaitForFences(Device, 1, ref inFlightFences[currentFrame], true, ulong.MaxValue);

        imageIndex = 0;
        var result = khrSwapchain.AcquireNextImage(Device, swapchain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default, ref imageIndex);
        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("Failed to acquire next image!");
        }

        Vk.ResetFences(Device, 1, ref inFlightFences[currentFrame]);

        // Begin command buffer
        Vk.ResetCommandBuffer(commandBuffers[currentFrame], CommandBufferResetFlags.None);
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };

        if (Vk.BeginCommandBuffer(commandBuffers[currentFrame], in beginInfo) != Result.Success)
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
        if (Vk.EndCommandBuffer(commandBuffer) != Result.Success)
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

        if (Vk.QueueSubmit(GraphicsQueue, 1, in submitInfo, inFlightFences[currentFrame]) != Result.Success)
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

        ClearValue clearColor = new()
        {
            Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f },
        };

        renderPassInfo.ClearValueCount = 1;
        renderPassInfo.PClearValues = &clearColor;

        Vk.CmdBeginRenderPass(commandBuffers[currentFrame], in renderPassInfo, SubpassContents.Inline);

        Vk.CmdBindPipeline(commandBuffers[currentFrame], PipelineBindPoint.Graphics, graphicsPipeline);

        Viewport viewport = new()
        {
            X = 0.0f,
            Y = 0.0f,
            Width = swapchainExtent.Width,
            Height = swapchainExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        Vk.CmdSetViewport(commandBuffers[currentFrame], 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = swapchainExtent
        };
        Vk.CmdSetScissor(commandBuffers[currentFrame], 0, 1, in scissor);

        rendererState = RendererState.UsingRenderPass;
    }

    public void EndRenderPass()
    {
        if (rendererState != RendererState.UsingRenderPass) throw new Exception("Tried to end render pass before beginning a frame or render pass!");

        Vk.CmdEndRenderPass(commandBuffers[currentFrame]);

        rendererState = RendererState.DrawingFrame;
    }

    public void Draw(VertexBuffer vertexBuffer)
    {
        vertexBuffer.Bind();
        Vk.CmdDraw(commandBuffers[currentFrame], vertexBuffer.VertexCount, 1, 0, 0);
    }

    public void DrawIndexed(VertexBuffer vertexBuffer, IndexBuffer indexBuffer)
    {
        vertexBuffer.Bind();
        indexBuffer.Bind();
        Vk.CmdDrawIndexed(CurrentCommandBuffer, indexBuffer.IndexCount, 1, 0, 0, 0);
    }

    private void OnFramebufferResize(Vector2D<int> framebufferSize)
    {
        framebufferResized = true;
    }

    private void CreateInstance(out Instance instance)
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

        if (Vk.CreateInstance(in instanceInfo, null, out instance) != Result.Success)
        {
            throw new Exception("Failed to create instance!");
        }

        // free unmanaged memory
        Marshal.FreeHGlobal((nint) appInfo.PEngineName);
        Marshal.FreeHGlobal((nint) appInfo.PApplicationName);
        SilkMarshal.Free((nint) instanceInfo.PpEnabledExtensionNames);
        if (EnableValidationLayers) SilkMarshal.Free((nint) instanceInfo.PpEnabledLayerNames);
    }

    private void CreateSurface(out KhrSurface khrSurface, out SurfaceKHR surface)
    {
        if (!Vk.TryGetInstanceExtension(instance, out khrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found!");
        }
        
        surface = windowSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }

    private void PickPhysicalDevice(out PhysicalDevice physicalDevice)
    {
        var devices = Vk.GetPhysicalDevices(instance);

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

    private bool IsPhysicalDeviceSuitable(PhysicalDevice physicalDevice)
    {
        var indices = FindQueueFamilies(physicalDevice);

        bool extensionsSupported = CheckDeviceExtensionSupport(physicalDevice);

        bool swapChainAdequate = false;
        if (extensionsSupported)
        {
            var swapChainSupportDetails = QuerySwapChainSupport(physicalDevice);
            swapChainAdequate = swapChainSupportDetails.PresentModes.Any() && swapChainSupportDetails.Formats.Any();
        }

        return indices.IsComplete() && extensionsSupported && swapChainAdequate;
    }

    private void CreateLogicalDevice(out Device logicalDevice, out Queue graphicsQueue, out Queue presentationQueue)
    {
        var indices = FindQueueFamilies(PhysicalDevice);

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

        if (Vk.CreateDevice(PhysicalDevice, in deviceInfo, null, out logicalDevice) != Result.Success)
        {
            throw new Exception("Failed to create logical device!");
        }

        Vk.GetDeviceQueue(logicalDevice, indices.GraphicsFamily!.Value, 0, out graphicsQueue);
        Vk.GetDeviceQueue(logicalDevice, indices.PresentFamily!.Value, 0, out presentationQueue);

        if (EnableValidationLayers) SilkMarshal.Free((nint) deviceInfo.PpEnabledLayerNames);
        SilkMarshal.Free((nint) deviceInfo.PpEnabledExtensionNames);
    }

    private void CreateSwapchain(out KhrSwapchain khrSwapchain, out SwapchainKHR swapchain,
            out Image[] swapchainImages, out Format swapchainImageFormat, out Extent2D swapchainExtent)
    {
        var swapchainSupportDetails = QuerySwapChainSupport(PhysicalDevice);

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

        var indices = FindQueueFamilies(PhysicalDevice);
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

        if (!Vk.TryGetDeviceExtension(instance, Device, out khrSwapchain))
        {
            throw new Exception("VK_KHR_swapchain extension not found!");
        }

        if (khrSwapchain.CreateSwapchain(Device, in swapchainInfo, null, out swapchain) != Result.Success)
        {
            throw new Exception("Failded to create swapchain!");
        }

        uint swapchainImageCount = 0;
        khrSwapchain.GetSwapchainImages(Device, swapchain, ref swapchainImageCount, null);
        swapchainImages = new Image[swapchainImageCount];
        fixed (Image* swapchainImagesPtr = swapchainImages)
        {
            khrSwapchain.GetSwapchainImages(Device, swapchain, ref swapchainImageCount, swapchainImagesPtr);
        }

        swapchainImageFormat = surfaceFormat.Format;
        swapchainExtent = extent;
    }

    private void CleanupSwapchain()
    {
        foreach (var framebuffer in swapchainFramebuffers)
        {
            Vk.DestroyFramebuffer(Device, framebuffer, null);
        }

        foreach (var imageView in swapchainImageViews)
        {
            Vk.DestroyImageView(Device, imageView, null);
        }

        khrSwapchain.DestroySwapchain(Device, swapchain, null);
    }

    private void RecreateSwapchain()
    {
        Vector2D<int> framebufferSize = window.FramebufferSize;

        // wait for window to be unminimized
        while (framebufferSize.X == 0 || framebufferSize.Y == 0)
        {
            framebufferSize = window.FramebufferSize;
            window.DoEvents();
        }
        
        Vk.DeviceWaitIdle(Device);

        CreateSwapchain(out khrSwapchain, out swapchain, out swapchainImages, out swapchainImageFormat, out swapchainExtent);
        CreateImageViews(out swapchainImageViews);
        CreateFramebuffers(out swapchainFramebuffers);
    }

    private void CreateImageViews(out ImageView[] imageViews) 
    {
        imageViews = new ImageView[swapchainImages.Length];

        for (int i = 0; i < swapchainImages.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = swapchainImages[i],
                Format = swapchainImageFormat,
                ViewType = ImageViewType.Type2D
            };
            createInfo.Components.R = ComponentSwizzle.Identity;
            createInfo.Components.G = ComponentSwizzle.Identity;
            createInfo.Components.B = ComponentSwizzle.Identity;
            createInfo.Components.A = ComponentSwizzle.Identity;
            
            createInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
            createInfo.SubresourceRange.BaseMipLevel = 0;
            createInfo.SubresourceRange.LevelCount = 1;
            createInfo.SubresourceRange.BaseArrayLayer = 0;
            createInfo.SubresourceRange.LayerCount = 1;

            if (Vk.CreateImageView(Device, in createInfo, null, out imageViews[i]) != Result.Success)
            {
                throw new Exception("Failed to create image view!");
            }
        }
    }

    private void CreateRenderPass(out RenderPass renderPass)
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

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };

        RenderPassCreateInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        if (Vk.CreateRenderPass(Device, in renderPassInfo, null, out renderPass) != Result.Success)
        {
            throw new Exception("Failed to create render pass!");
        }
    }

    private void CreateGraphicsPipeline(out Pipeline graphicsPipeline, out PipelineLayout layout, string vertexShaderPath, string fragmentShaderPath)
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
            FrontFace = FrontFace.Clockwise
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

        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo
        };

        if (Vk.CreatePipelineLayout(Device, in pipelineLayoutInfo, null, out layout) != Result.Success)
        {
            throw new Exception("Failed to create pipeline layout!");
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
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0
            };

            if (Vk.CreateGraphicsPipelines(Device, default, 1, in pipelineInfo, null, out graphicsPipeline) != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline!");
            }
        }

        Vk.DestroyShaderModule(Device, vertexShaderModule, null);
        Vk.DestroyShaderModule(Device, fragmentShaderModule, null);
    }

    private ShaderModule CreateShaderModule(byte[] shaderCode)
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

            if (Vk.CreateShaderModule(Device, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader!");
            }
        }

        return shaderModule;
    }

    private void CreateFramebuffers(out Framebuffer[] framebuffers)
    {
        framebuffers = new Framebuffer[swapchainImageViews.Length];

        for (int i = 0; i < swapchainImageViews.Length; i++)
        {
            var attachments = stackalloc[] { swapchainImageViews[i] };

            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass,
                AttachmentCount = 1,
                PAttachments = attachments,
                Width = swapchainExtent.Width,
                Height = swapchainExtent.Height,
                Layers = 1
            };

            if (Vk.CreateFramebuffer(Device, in framebufferInfo, null, out framebuffers[i]) != Result.Success)
            {
                throw new Exception("Failed to create framebuffer!");
            }
        }
    }

    private void CreateCommandPool(out CommandPool commandPool)
    {
        var queueFamilyIndices = FindQueueFamilies(PhysicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamilyIndices.GraphicsFamily!.Value
        };

        if (Vk.CreateCommandPool(Device, in poolInfo, null, out commandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool!");
        }
    }

    private void CreateCommandBuffers(out CommandBuffer[] commandBuffers)
    {
        commandBuffers = new CommandBuffer[MaxFramesInFlight];

        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint) commandBuffers.Length
        };

        fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
        {
            if (Vk.AllocateCommandBuffers(Device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
    }

    private void RecordCommandBuffer(CommandBuffer commandBuffer, uint imageIndex)
    {
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };

        if (Vk.BeginCommandBuffer(commandBuffer, in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }

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

        ClearValue clearColor = new()
        {
            Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f },
        };

        renderPassInfo.ClearValueCount = 1;
        renderPassInfo.PClearValues = &clearColor;

        Vk.CmdBeginRenderPass(commandBuffer, in renderPassInfo, SubpassContents.Inline);

        Vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, graphicsPipeline);

        Viewport viewport = new()
        {
            X = 0.0f,
            Y = 0.0f,
            Width = swapchainExtent.Width,
            Height = swapchainExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        Vk.CmdSetViewport(commandBuffer, 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = swapchainExtent
        };
        Vk.CmdSetScissor(commandBuffer, 0, 1, in scissor);

        Vk.CmdDraw(commandBuffer, 3, 1, 0, 0);

        Vk.CmdEndRenderPass(commandBuffer);

        if (Vk.EndCommandBuffer(commandBuffer) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }
    }

    private void CreateSyncObjects(out Semaphore[] imageAvailableSemaphores, out Semaphore[] renderFinishedSemaphores, out Fence[] inFlightFences)
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
            if (Vk.CreateSemaphore(Device, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                Vk.CreateSemaphore(Device, in semaphoreInfo, null, out renderFinishedSemaphores[i]) != Result.Success ||
                Vk.CreateFence(Device, in fenceInfo, null, out inFlightFences[i]) != Result.Success)
            {
                throw new Exception("Failed to create sync objects!");
            }
        }
    }

    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice physicalDevice)
    {
        QueueFamilyIndices indices = new();

        uint queueFamilyCount = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            Vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref queueFamilyCount, queueFamiliesPtr);
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

    private bool CheckDeviceExtensionSupport(PhysicalDevice physicalDevice)
    {
        uint extensionCount = 0;
        Vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*) null, ref extensionCount, null);

        var availableExtensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
        {
            Vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*) null, ref extensionCount, availableExtensionsPtr);
        }

        var availableExtensionNames = availableExtensions.Select(extension => Marshal.PtrToStringAnsi((nint) extension.ExtensionName)).ToHashSet();

        return deviceExtensions.All(availableExtensionNames.Contains);
    }

    private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
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

    private SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] availableFormats)
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

    private PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] availablePresentModes)
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

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
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

    private string[] GetRequiredExtensions()
    {
        var glfwExtensions = windowSurface.GetRequiredExtensions(out var glfwExtensionCount);
        var extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);
        
        if (EnableValidationLayers)
        {
            return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
        }

        return extensions;
    }

    private bool CheckValidationLayerSupport()
    {
        uint layerCount = 0;
        Vk.EnumerateInstanceLayerProperties(ref layerCount, null);
        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
        {
            Vk.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
        }

        var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((nint) layer.LayerName)).ToHashSet();

        return validationLayers.All(availableLayerNames.Contains);
    }

    private uint DebugCallback(
            DebugUtilsMessageSeverityFlagsEXT messageSeverity,
            DebugUtilsMessageTypeFlagsEXT messageType,
            DebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData)
    {
        Console.WriteLine($"Validation layer: {Marshal.PtrToStringAnsi((nint) pCallbackData->PMessage)}");

        return Vk.False;
    }

    // IDisposable methods
    
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
                Vk.DestroySemaphore(Device, imageAvailableSemaphores[i], null);
                Vk.DestroySemaphore(Device, renderFinishedSemaphores[i], null);
                Vk.DestroyFence(Device, inFlightFences[i], null);
            }

            Vk.DestroyCommandPool(Device, CommandPool, null);

            CleanupSwapchain();

            Vk.DestroyPipelineLayout(Device, pipelineLayout, null);
            Vk.DestroyRenderPass(Device, renderPass, null);

            Vk.DestroyDevice(Device, null);
            Vk.DestroyInstance(instance, null);

            disposedValue = true;
        }
    }
}
