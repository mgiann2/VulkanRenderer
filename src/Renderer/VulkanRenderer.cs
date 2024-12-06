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

namespace Renderer;

public struct QueueFamilyIndices
{
    public uint? GraphicsFamily { get; set; }
    public uint? PresentFamily { get; set; }

    public bool IsComplete()
    {
        return GraphicsFamily.HasValue && PresentFamily.HasValue;
    }
}

struct SwapchainSupportDetails
{
    public SurfaceCapabilitiesKHR Capabilities;
    public SurfaceFormatKHR[] Formats;
    public PresentModeKHR[] PresentModes;
}

struct SwapchainInfo
{
    public KhrSwapchain KhrSwapchain { get; init; }
    public SwapchainKHR Swapchain { get; init; }
    public Image[] Images { get; init; }
    public ImageView[] ImageViews { get; init; }
    public Extent2D Extent { get; init; }
    public Format ImageFormat { get; init; }
}

[StructLayout(LayoutKind.Explicit)]
public struct SceneInfo
{
    [FieldOffset(0)]public Matrix4X4<float> CameraView;
    [FieldOffset(64)]public Matrix4X4<float> CameraProjection;
    [FieldOffset(128)]public Vector3D<float> AmbientLightColor;
    [FieldOffset(140)]public float AmbientLightStrength;
    [FieldOffset(144)]public Vector3D<float> DirectionalLightDirection;
    [FieldOffset(160)]public Vector3D<float> DirectionalLightColor;
}

[StructLayout(LayoutKind.Explicit)]
public struct LightInfo
{
    [FieldOffset(0)]public Matrix4X4<float> Model;
    [FieldOffset(64)]public Vector3D<float> Position;
    [FieldOffset(80)]public Vector3D<float> Color;
}

unsafe public partial class VulkanRenderer
{
    const string AssetsPath = "assets/";
    const string ShadersPath = "compiled_shaders/";

    // TODO: temporary to test lights
    public List<Light> Lights = new List<Light>();

    readonly bool EnableValidationLayers;
    const int MaxFramesInFlight = 2;
    const uint MaxGBufferDescriptorSets = 20;

    const string SphereMeshPath = AssetsPath + "models/sphere/sphere.glb";
    const string SkyboxTexturePath = AssetsPath + "hdris/EveningSkyHDRI.jpg";

    readonly string[] validationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName
    };

    Mesh screenQuadMesh;
    Mesh skyboxCubeMesh;
    Mesh sphereMesh;

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

    SwapchainInfo swapchainInfo;
    Framebuffer[] swapchainFramebuffers;

    GBuffer gBuffer;
    RenderPass geometryRenderPass;
    Framebuffer geometryFramebuffer;

    FramebufferAttachment compositionColorAttachment;
    FramebufferAttachment compositionDepthAttachment;
    RenderPass compositionRenderPass;
    Framebuffer compositionFramebuffer;

    RenderPass postProcessingRenderPass;

    Buffer[] sceneInfoBuffers;
    DeviceMemory[] sceneInfoBuffersMemory;

    DescriptorSetLayout sceneInfoDescriptorSetLayout;
    DescriptorSetLayout materialInfoDescriptorSetLayout;
    DescriptorSetLayout screenTextureDescriptorSetLayout;
    DescriptorSetLayout singleTextureDescriptorSetLayout;

    DescriptorPool sceneInfoDescriptorPool;
    DescriptorPool materialInfoDescriptorPool;
    DescriptorPool screenTextureDescriptorPool;
    DescriptorPool singleTextureDescriptorPool;

    DescriptorSet[] sceneInfoDescriptorSets;
    DescriptorSet[] screenTextureInfoDescriptorSets;
    DescriptorSet[] postProcessTextureDescriptorSets;

    Texture skyboxTexture;
    DescriptorSet[] skyboxTextureDescriptorSets;


    GraphicsPipeline geometryPipeline;
    GraphicsPipeline compositionPipeline;
    GraphicsPipeline lightingPipeline;
    GraphicsPipeline skyboxPipeline;
    GraphicsPipeline postProcessPipeline;

    CommandPool commandPool;
    CommandBuffer[] geometryCommandBuffers;
    CommandBuffer[] compositionCommandBuffers;
    CommandBuffer[] postProcessingCommandBuffers;

    Sampler textureSampler;

    Semaphore[] imageAvailableSemaphores;
    Semaphore[] renderFinishedSemaphores;
    Semaphore[] geometryPassFinishedSemaphores;
    Semaphore[] compositionPassFinishedSemaphores;
    Fence[] inFlightFences;

    uint currentFrame;
    bool framebufferResized = false;
    uint imageIndex;
    bool isFrameEnded = true;

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
        CreateSwapchain(out swapchainInfo);

        (sceneInfoBuffers, sceneInfoBuffersMemory) = VulkanHelper.CreateUniformBuffers(device, physicalDevice, (ulong) Unsafe.SizeOf<SceneInfo>(), MaxFramesInFlight);
        textureSampler = VulkanHelper.CreateTextureSampler(device, physicalDevice);

        // Create descriptor pools
        sceneInfoDescriptorPool = CreateSceneInfoDescriptorPool();
        materialInfoDescriptorPool = CreateMaterialInfoDescriptorPool(MaxGBufferDescriptorSets);
        screenTextureDescriptorPool = CreateScreenTextureInfoDescriptorPool();
        singleTextureDescriptorPool = CreateSingleTextureDescriptorPool();

        // Create descriptor set layouts
        sceneInfoDescriptorSetLayout = CreateSceneInfoDescriptorSetLayout();
        materialInfoDescriptorSetLayout = CreateMaterialInfoDescriptorSetLayout();
        screenTextureDescriptorSetLayout = CreateScreenTexureInfoDescriptorSetLayout();
        singleTextureDescriptorSetLayout = CreateSingleTextureDescriptorSetLayout();

        // create render passes
        CreateGeometryRenderPass(out gBuffer, out geometryRenderPass, out geometryFramebuffer);
        CreateCompositionRenderPass(out compositionColorAttachment, out compositionDepthAttachment, out compositionRenderPass, out compositionFramebuffer);
        CreatePostProcessingRenderPass(out postProcessingRenderPass, out swapchainFramebuffers);

        // Create composition pass descriptor set
        sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets();
        screenTextureInfoDescriptorSets = CreateScreenTextureInfoDescriptorSets();
        postProcessTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionColorAttachment.ImageView);

        // Create pipelines
        geometryPipeline = CreateGeometryPipeline();
        compositionPipeline = CreateCompositionPipeline();
        lightingPipeline = CreateLightingPipeline();
        skyboxPipeline = CreateSkyboxPipeline();
        postProcessPipeline = CreatePostProcessPipeline();

        // create commnad pool and buffers
        commandPool = VulkanHelper.CreateCommandPool(device, FindQueueFamilies(physicalDevice));
        CreateCommandBuffers(out geometryCommandBuffers, out compositionCommandBuffers, out postProcessingCommandBuffers);

        CreateSyncObjects(out imageAvailableSemaphores, out geometryPassFinishedSemaphores, out compositionPassFinishedSemaphores, out renderFinishedSemaphores, out inFlightFences);

        // Create skybox descriptor sets
        skyboxTexture = CreateTexture(SkyboxTexturePath);
        skyboxTextureDescriptorSets = CreateSingleTextureDescriptorSets(skyboxTexture.TextureImageView);

        // generate quad mesh
        screenQuadMesh = PrimitiveMesh.CreateQuadMesh(this);
        
        // generate cube mesh
        skyboxCubeMesh = PrimitiveMesh.CreateCubeMesh(this);

        // load sphere mesh for lighting
        sphereMesh = LoadMesh(SphereMeshPath);

        window.FramebufferResize += OnFramebufferResize;
    }

    public void BeginFrame()
    {
        if (!isFrameEnded) throw new Exception("Tried to begin frame before ending current frame!");

        vk.WaitForFences(device, 1, ref inFlightFences[currentFrame], true, ulong.MaxValue);

        imageIndex = 0;
        var result = swapchainInfo.KhrSwapchain.AcquireNextImage(device, swapchainInfo.Swapchain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default, ref imageIndex);
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

        vk.ResetCommandBuffer(geometryCommandBuffers[currentFrame], CommandBufferResetFlags.None);
        vk.ResetCommandBuffer(compositionCommandBuffers[currentFrame], CommandBufferResetFlags.None);
        vk.ResetCommandBuffer(postProcessingCommandBuffers[currentFrame], CommandBufferResetFlags.None);
        
        // Begin Geometry Render Pass
        // --------------------------
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };

        if (vk.BeginCommandBuffer(geometryCommandBuffers[currentFrame], in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = geometryRenderPass,
            Framebuffer = geometryFramebuffer,
            RenderArea = new()
            {
                Extent = swapchainInfo.Extent,
                Offset = { X = 0, Y = 0 }
            }
        };

        ClearValue[] clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 0.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };

        renderPassInfo.ClearValueCount = (uint) clearColors.Length;
        fixed (ClearValue* clearColorsPtr = clearColors)
            renderPassInfo.PClearValues = clearColorsPtr;

        vk.CmdBeginRenderPass(geometryCommandBuffers[currentFrame], in renderPassInfo, SubpassContents.Inline);

        vk.CmdBindPipeline(geometryCommandBuffers[currentFrame], PipelineBindPoint.Graphics, geometryPipeline.Pipeline);

        Viewport viewport = new()
        {
            X = 0.0f,
            Y = 0.0f,
            Width = swapchainInfo.Extent.Width,
            Height = swapchainInfo.Extent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        vk.CmdSetViewport(geometryCommandBuffers[currentFrame], 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = swapchainInfo.Extent
        };
        vk.CmdSetScissor(geometryCommandBuffers[currentFrame], 0, 1, in scissor);

        vk.CmdBindDescriptorSets(geometryCommandBuffers[currentFrame], PipelineBindPoint.Graphics,
                                 geometryPipeline.Layout, 0, 1, in sceneInfoDescriptorSets[currentFrame], 0, default);
    }

    public void EndFrame()
    {
        if (!isFrameEnded) throw new Exception("Tried to end frame before beginning a new one!");

        // End geometry render pass
        vk.CmdEndRenderPass(geometryCommandBuffers[currentFrame]);
        if (vk.EndCommandBuffer(geometryCommandBuffers[currentFrame]) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }

        // Begin composition render pass
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };

        if (vk.BeginCommandBuffer(compositionCommandBuffers[currentFrame], in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = compositionRenderPass,
            Framebuffer = compositionFramebuffer,
            RenderArea = new()
            {
                Extent = swapchainInfo.Extent,
                Offset = { X = 0, Y = 0 }
            }
        };

        var clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };

        renderPassInfo.ClearValueCount = (uint) clearColors.Length;
        fixed (ClearValue* clearColorsPtr = clearColors)
            renderPassInfo.PClearValues = clearColorsPtr;

        vk.CmdBeginRenderPass(compositionCommandBuffers[currentFrame], in renderPassInfo, SubpassContents.Inline);

        Viewport viewport = new()
        {
            X = 0.0f,
            Y = 0.0f,
            Width = swapchainInfo.Extent.Width,
            Height = swapchainInfo.Extent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        vk.CmdSetViewport(compositionCommandBuffers[currentFrame], 0, 1, in viewport);

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = swapchainInfo.Extent
        };
        vk.CmdSetScissor(compositionCommandBuffers[currentFrame], 0, 1, in scissor);

        // draw skybox
        var skyboxDescriptorSets = stackalloc[] { sceneInfoDescriptorSets[currentFrame], skyboxTextureDescriptorSets[currentFrame] };
        vk.CmdBindPipeline(compositionCommandBuffers[currentFrame], PipelineBindPoint.Graphics, skyboxPipeline.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffers[currentFrame], PipelineBindPoint.Graphics,
                skyboxPipeline.Layout, 0, 2, skyboxDescriptorSets, 0, default);

        Bind(skyboxCubeMesh.VertexBuffer, compositionCommandBuffers[currentFrame]);
        Bind(skyboxCubeMesh.IndexBuffer, compositionCommandBuffers[currentFrame]);
        vk.CmdDrawIndexed(compositionCommandBuffers[currentFrame], skyboxCubeMesh.IndexBuffer.IndexCount, 1, 0, 0, 0);

        // draw gbuffer objects
        var descriptorSets = stackalloc[] { sceneInfoDescriptorSets[currentFrame], screenTextureInfoDescriptorSets[currentFrame] };

        vk.CmdBindPipeline(compositionCommandBuffers[currentFrame], PipelineBindPoint.Graphics, compositionPipeline.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffers[currentFrame], PipelineBindPoint.Graphics,
                compositionPipeline.Layout, 0, 2, descriptorSets, 0, default);

        Bind(screenQuadMesh.VertexBuffer, compositionCommandBuffers[currentFrame]);
        Bind(screenQuadMesh.IndexBuffer, compositionCommandBuffers[currentFrame]);
        vk.CmdDrawIndexed(compositionCommandBuffers[currentFrame], screenQuadMesh.IndexBuffer.IndexCount, 1, 0, 0, 0);

        // draw point lights
        vk.CmdBindPipeline(compositionCommandBuffers[currentFrame], PipelineBindPoint.Graphics, lightingPipeline.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffers[currentFrame], PipelineBindPoint.Graphics,
                lightingPipeline.Layout, 0, 2, descriptorSets, 0, default);
        Bind(sphereMesh.VertexBuffer, compositionCommandBuffers[currentFrame]);
        Bind(sphereMesh.IndexBuffer, compositionCommandBuffers[currentFrame]);
        foreach (var light in Lights)
        {
            var lightInfo = light.ToInfo();
            vk.CmdPushConstants(compositionCommandBuffers[currentFrame], lightingPipeline.Layout,
                                ShaderStageFlags.VertexBit, 0,
                                (uint) Unsafe.SizeOf<LightInfo>(), &lightInfo);

            vk.CmdDrawIndexed(compositionCommandBuffers[currentFrame], sphereMesh.IndexBuffer.IndexCount, 1, 0, 0, 0);
        }

        vk.CmdEndRenderPass(compositionCommandBuffers[currentFrame]);
        if (vk.EndCommandBuffer(compositionCommandBuffers[currentFrame]) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }

        // Begin post processing render pass 
        if (vk.BeginCommandBuffer(postProcessingCommandBuffers[currentFrame], in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin command buffer!");
        }

        renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = postProcessingRenderPass,
            Framebuffer = swapchainFramebuffers[imageIndex],
            RenderArea = new()
            {
                Extent = swapchainInfo.Extent,
                Offset = { X = 0, Y = 0 }
            }
        };

        clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
        };

        renderPassInfo.ClearValueCount = (uint) clearColors.Length;
        fixed (ClearValue* clearColorsPtr = clearColors)
            renderPassInfo.PClearValues = clearColorsPtr;

        vk.CmdBeginRenderPass(postProcessingCommandBuffers[currentFrame], in renderPassInfo, SubpassContents.Inline);

        vk.CmdSetViewport(postProcessingCommandBuffers[currentFrame], 0, 1, in viewport);
        vk.CmdSetScissor(postProcessingCommandBuffers[currentFrame], 0, 1, in scissor);

        var postProcessDescriptorSet = stackalloc[] { postProcessTextureDescriptorSets[currentFrame] };
        vk.CmdBindPipeline(postProcessingCommandBuffers[currentFrame], PipelineBindPoint.Graphics, postProcessPipeline.Pipeline);
        vk.CmdBindDescriptorSets(postProcessingCommandBuffers[currentFrame], PipelineBindPoint.Graphics,
                postProcessPipeline.Layout, 0, 1, postProcessDescriptorSet, 0, default);

        Bind(screenQuadMesh.VertexBuffer, postProcessingCommandBuffers[currentFrame]);
        Bind(screenQuadMesh.IndexBuffer, postProcessingCommandBuffers[currentFrame]);
        vk.CmdDrawIndexed(postProcessingCommandBuffers[currentFrame], screenQuadMesh.IndexBuffer.IndexCount, 1, 0, 0, 0);

        vk.CmdEndRenderPass(postProcessingCommandBuffers[currentFrame]);
        if (vk.EndCommandBuffer(postProcessingCommandBuffers[currentFrame]) != Result.Success)
        {
            throw new Exception("Failed to end command buffer!");
        }

        // submit geometry commands
        var geomCommandBuffer = geometryCommandBuffers[currentFrame];
        var geomWaitSemaphores = stackalloc[] { imageAvailableSemaphores[currentFrame] };
        var waitStages = stackalloc[] { PipelineStageFlags.ColorAttachmentOutputBit };
        var geomSignalSemaphores = stackalloc[] { geometryPassFinishedSemaphores[currentFrame] };

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = geomWaitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &geomCommandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = geomSignalSemaphores
        };

        if (vk.QueueSubmit(graphicsQueue, 1, in submitInfo, default) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer!");
        }

        // submit composition commands
        var compCommandBuffer = compositionCommandBuffers[currentFrame];
        var compWaitSemaphores = stackalloc[] { geometryPassFinishedSemaphores[currentFrame] };
        var compSignalSemaphores = stackalloc[] { compositionPassFinishedSemaphores[currentFrame] };

        submitInfo = submitInfo with
        {
            PWaitSemaphores = compWaitSemaphores,
            PCommandBuffers = &compCommandBuffer,
            PSignalSemaphores = compSignalSemaphores
        };

        if (vk.QueueSubmit(graphicsQueue, 1, in submitInfo, default) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer!");
        }

        // submit post process commands
        var postProcessCommandBuffer = postProcessingCommandBuffers[currentFrame];
        var postProcessWaitSemaphores= stackalloc[] { compositionPassFinishedSemaphores[currentFrame] };
        var postProcessSignalSemaphores = stackalloc[] { renderFinishedSemaphores[currentFrame] };

        submitInfo = submitInfo with
        {
            PWaitSemaphores = postProcessWaitSemaphores,
            PCommandBuffers = &postProcessCommandBuffer,
            PSignalSemaphores = postProcessSignalSemaphores
        };

        if (vk.QueueSubmit(graphicsQueue, 1, in submitInfo, inFlightFences[currentFrame]) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer!");
        }

        var swapchains = stackalloc[] { swapchainInfo.Swapchain };

        uint idx = imageIndex;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = postProcessSignalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapchains,
            PImageIndices = &idx
        };

        var result = swapchainInfo.KhrSwapchain.QueuePresent(presentQueue, in presentInfo);
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
    }

    public void DeviceWaitIdle()
    {
        vk.DeviceWaitIdle(device);
    }

    public void UpdateSceneInfo(SceneInfo sceneInfo)
    {
        void* data;
        vk.MapMemory(device, sceneInfoBuffersMemory[currentFrame], 0, (ulong) Unsafe.SizeOf<SceneInfo>(), 0, &data);
        new Span<SceneInfo>(data, 1)[0] = sceneInfo;
        vk.UnmapMemory(device, sceneInfoBuffersMemory[currentFrame]);
    }

    void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        var commandBuffer = BeginSingleTimeCommand();

        BufferCopy copyRegion = new() { Size = size };
        vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, in copyRegion);

        EndSingleTimeCommand(commandBuffer);
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

    void CreateSwapchain(out SwapchainInfo swapchainInfo)
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

        SwapchainCreateInfoKHR swapchainCreateInfo = new()
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
            swapchainCreateInfo.ImageSharingMode = SharingMode.Concurrent;
            swapchainCreateInfo.QueueFamilyIndexCount = 2;
            swapchainCreateInfo.PQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            swapchainCreateInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        swapchainCreateInfo.PreTransform = swapchainSupportDetails.Capabilities.CurrentTransform;
        swapchainCreateInfo.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
        swapchainCreateInfo.PresentMode = presentMode;
        swapchainCreateInfo.Clipped = true;
        swapchainCreateInfo.OldSwapchain = default;

        if (!vk.TryGetDeviceExtension(instance, device, out KhrSwapchain khrSwapchain))
        {
            throw new Exception("VK_KHR_swapchain extension not found!");
        }

        if (khrSwapchain.CreateSwapchain(device, in swapchainCreateInfo, null, out var swapchain) != Result.Success)
        {
            throw new Exception("Failded to create swapchain!");
        }

        uint swapchainImageCount = 0;
        khrSwapchain.GetSwapchainImages(device, swapchain, ref swapchainImageCount, null);
        var swapchainImages = new Image[swapchainImageCount];
        fixed (Image* swapchainImagesPtr = swapchainImages)
        {
            khrSwapchain.GetSwapchainImages(device, swapchain, ref swapchainImageCount, swapchainImagesPtr);
        }

        var swapchainImageFormat = surfaceFormat.Format;
        var swapchainExtent = extent;

        // create image views
        var swapchainImageViews = new ImageView[swapchainImages.Length];
        for (int i = 0; i < swapchainImages.Length; i++)
        {
            swapchainImageViews[i] = VulkanHelper.CreateImageView(device, swapchainImages[i], swapchainImageFormat, ImageAspectFlags.ColorBit);
        }

        swapchainInfo = new SwapchainInfo
        {
            KhrSwapchain = khrSwapchain,
            Swapchain = swapchain,
            Images = swapchainImages,
            ImageViews = swapchainImageViews,
            ImageFormat = swapchainImageFormat,
            Extent = swapchainExtent
        };
    }

    void CleanupSwapchain()
    {
        vk.DestroyPipeline(device, postProcessPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(device, postProcessPipeline.Layout, null);
        vk.DestroyPipeline(device, skyboxPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(device, skyboxPipeline.Layout, null);
        vk.DestroyPipeline(device, lightingPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(device, lightingPipeline.Layout, null);
        vk.DestroyPipeline(device, compositionPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(device, compositionPipeline.Layout, null);
        vk.DestroyPipeline(device, geometryPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(device, geometryPipeline.Layout, null);

        DestroyPostProcessingRenderPass(postProcessingRenderPass, swapchainFramebuffers);
        DestroyCompositionRenderPass(compositionColorAttachment, compositionDepthAttachment, compositionRenderPass, compositionFramebuffer);
        DestroyGeomtryRenderPass(gBuffer, geometryRenderPass, geometryFramebuffer);

        foreach (var imageView in swapchainInfo.ImageViews)
        {
            vk.DestroyImageView(device, imageView, null);
        }

        swapchainInfo.KhrSwapchain.DestroySwapchain(device, swapchainInfo.Swapchain, null);
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

        CreateSwapchain(out swapchainInfo);

        CreateGeometryRenderPass(out gBuffer, out geometryRenderPass, out geometryFramebuffer);
        CreateCompositionRenderPass(out compositionColorAttachment, out compositionDepthAttachment, out compositionRenderPass, out compositionFramebuffer);
        CreatePostProcessingRenderPass(out postProcessingRenderPass, out swapchainFramebuffers);

        UpdateScreenTextureDescriptorSets(screenTextureInfoDescriptorSets, gBuffer);
        UpdateSingleTextureDescriptorSets(postProcessTextureDescriptorSets, compositionColorAttachment.ImageView);

        geometryPipeline = CreateGeometryPipeline();
        compositionPipeline = CreateCompositionPipeline();
        lightingPipeline = CreateLightingPipeline();
        skyboxPipeline = CreateSkyboxPipeline();
        postProcessPipeline = CreatePostProcessPipeline();
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

    void CreateCommandBuffers(out CommandBuffer[] geometryCommandBuffers, out CommandBuffer[] compositionCommandBuffers, out CommandBuffer[] postProcessingCommandBuffers)
    {
        geometryCommandBuffers = new CommandBuffer[MaxFramesInFlight];
        compositionCommandBuffers = new CommandBuffer[MaxFramesInFlight];
        postProcessingCommandBuffers = new CommandBuffer[MaxFramesInFlight];

        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint) MaxFramesInFlight 
        };

        fixed (CommandBuffer* commandBuffersPtr = geometryCommandBuffers)
        {
            if (vk.AllocateCommandBuffers(device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }

        fixed (CommandBuffer* commandBuffersPtr = compositionCommandBuffers)
        {
            if (vk.AllocateCommandBuffers(device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }

        fixed (CommandBuffer* commandBuffersPtr = postProcessingCommandBuffers)
        {
            if (vk.AllocateCommandBuffers(device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers!");
            }
        }
    }

    void CreateSyncObjects(out Semaphore[] imageAvailableSemaphores,
                           out Semaphore[] geometryPassFinishedSemaphores,
                           out Semaphore[] compositionPassFinishedSemaphores,
                           out Semaphore[] renderFinishedSemaphores,
                           out Fence[] inFlightFences)
    {
        imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
        geometryPassFinishedSemaphores = new Semaphore[MaxFramesInFlight];
        compositionPassFinishedSemaphores = new Semaphore[MaxFramesInFlight];
        renderFinishedSemaphores = new Semaphore[MaxFramesInFlight];
        inFlightFences = new Fence[MaxFramesInFlight];

        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (vk.CreateSemaphore(device, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                vk.CreateSemaphore(device, in semaphoreInfo, null, out geometryPassFinishedSemaphores[i]) != Result.Success ||
                vk.CreateSemaphore(device, in semaphoreInfo, null, out compositionPassFinishedSemaphores[i]) != Result.Success ||
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

    SwapchainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
    {
        var details = new SwapchainSupportDetails();

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

    static uint DebugCallback(
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

            for (int i = 0; i < MaxFramesInFlight; i++)
            {
                vk.DestroyBuffer(device, sceneInfoBuffers[i], null);
                vk.FreeMemory(device, sceneInfoBuffersMemory[i], null);
            }

            DestroyMesh(screenQuadMesh);
            DestroyMesh(sphereMesh);

            vk.DestroyDevice(device, null);
            vk.DestroyInstance(instance, null);

            disposedValue = true;
        }
    }
}
