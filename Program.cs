﻿using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Assimp;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using StbiSharp;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Buffer = Silk.NET.Vulkan.Buffer;
using File = System.IO.File;

// var app = new MGSVRenderingApp();
// app.Run();

var options = WindowOptions.DefaultVulkan with
{
    Size = new Vector2D<int>(800, 600),
    Title = "MGSV Renderer"
};

var window = Window.Create(options);
window.Initialize();

if (window.VkSurface is null)
{
    throw new Exception("Windowing platform doesn't support Vulkan");
}

var renderer = new VulkanRenderer(window, true);
window.Update += renderer.DrawFrame;
window.Run();

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

struct Vertex 
{
    public Vector3D<float> pos;
    public Vector3D<float> color;
    public Vector2D<float> texCoord;

    public static VertexInputBindingDescription GetBindingDescription()
    {
        VertexInputBindingDescription bindingDescription = new()
        {
            Binding = 0,
            Stride = (uint) Unsafe.SizeOf<Vertex>(),
            InputRate = VertexInputRate.Vertex
        };

        return bindingDescription;
    }

    public static VertexInputAttributeDescription[] GetAttributeDescriptions()
    {
        var attributeDescriptions = new[]
        {
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint) Marshal.OffsetOf<Vertex>(nameof(pos))
            },
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint) Marshal.OffsetOf<Vertex>(nameof(color))
            },
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 2,
                Format = Format.R32G32Sfloat,
                Offset = (uint) Marshal.OffsetOf<Vertex>(nameof(texCoord))
            }
        };

        return attributeDescriptions;
    }
}

struct UniformBufferObject
{
    public Matrix4X4<float> model;
    public Matrix4X4<float> view;
    public Matrix4X4<float> proj;
}

unsafe class MGSVRenderingApp
{
    const int MaxFramesInFlight = 2;

    const int Width = 1920;
    const int Height = 1080;

    const string ModelPath = "models/viking_room.obj";
    const string TexturePath = "textures/viking_room.png";

    bool EnableValidationLayers = true;

    private readonly string[] validationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    private readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName
    };

    private IWindow? window;
    private Vk? vk;

    private Instance instance;

    private KhrSurface? khrSurface;
    private SurfaceKHR surface;
    private Queue presentQueue;

    private ExtDebugUtils? debugUtils;
    private DebugUtilsMessengerEXT debugMessenger;

    private PhysicalDevice physicalDevice;

    private Device device;
    private Queue graphicsQueue;

    private KhrSwapchain? khrSwapChain;
    private SwapchainKHR swapChain;
    private Image[]? swapChainImages;
    private Format swapChainImageFormat;
    private Extent2D swapChainExtent;

    private ImageView[]? swapChainImageViews;
    private Framebuffer[]? swapChainFramebuffers;

    private RenderPass renderPass;
    private DescriptorSetLayout descriptorSetLayout;
    private PipelineLayout pipelineLayout;
    private Pipeline graphicsPipeline;

    private CommandPool commandPool;
    private CommandBuffer[]? commandBuffers;

    private Image colorImage;
    private DeviceMemory colorImageMemory;
    private ImageView colorImageView;

    private Image depthImage;
    private DeviceMemory depthImageMemory;
    private ImageView depthImageView;

    private uint mipLevels;
    private Image textureImage;
    private DeviceMemory textureImageMemory;
    private ImageView textureImageView;
    Sampler textureSampler;

    private Vertex[]? vertices;
    private uint[]? indices;

    private Buffer vertexBuffer;
    private DeviceMemory vertexBufferMemory;
    private Buffer indexBuffer;
    private DeviceMemory indexBufferMemory;

    private Buffer[]? uniformBuffers;
    private DeviceMemory[]? uniformBuffersMemory;

    private DescriptorPool descriptorPool;
    private DescriptorSet[]? descriptorSets;

    private Semaphore[]? imageAvailableSemaphores;
    private Semaphore[]? renderFinishedSemaphores;
    private Fence[]? inFlightFences;
    private Fence[]? imagesInFlight;
    private uint currentFrame = 0;

    private SampleCountFlags msaaSamples = SampleCountFlags.Count1Bit;

    private bool framebufferResized = false;

    public void Run()
    {
        InitWindow();
        InitVulkan();
        MainLoop();
        CleanUp();
    }

    private void InitWindow()
    {
        // create window
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(Width, Height),
            Title = "MGSV Renderer"
        };

        window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan");
        }

        window.FramebufferResize += FramebufferResizeCallback;
    }

    private void InitVulkan()
    {
        CreateInstance();
        SetupDebugMessenger();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateDescriptorSetLayout();
        CreateGraphicsPipeline();
        CreateCommandPool();
        CreateColorResources();
        CreateDepthResources();
        CreateFramebuffers();
        CreateTextureImage();
        CreateTextureImageView();
        CreateTextureSampler();
        LoadModel();
        CreateVertexBuffer();
        CreateIndexBuffer();
        CreateUniformBuffers();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateCommandBuffers();
        CreateSyncObjects();
    }

    private void MainLoop()
    {
        window!.Render += DrawFrame;
        window!.Run();
        vk!.DeviceWaitIdle(device);
    }

    private void CleanUp()
    {
        CleanUpSwapChains();

        vk!.DestroySampler(device, textureSampler, null);
        vk!.DestroyImageView(device, textureImageView, null);

        vk!.DestroyImage(device, textureImage, null);
        vk!.FreeMemory(device, textureImageMemory, null);

        for (int i = 0; i < swapChainImages!.Length; i++)
        {
            vk!.DestroyBuffer(device, uniformBuffers![i], null);
            vk!.FreeMemory(device, uniformBuffersMemory![i], null);
        }

        vk!.DestroyDescriptorPool(device, descriptorPool, null);

        vk!.DestroyDescriptorSetLayout(device, descriptorSetLayout, null);

        vk!.DestroyBuffer(device, indexBuffer, null);
        vk!.FreeMemory(device, indexBufferMemory, null);

        vk!.DestroyBuffer(device, vertexBuffer, null);
        vk!.FreeMemory(device, vertexBufferMemory, null);

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            vk!.DestroySemaphore(device, imageAvailableSemaphores![i], null);
            vk!.DestroySemaphore(device, renderFinishedSemaphores![i], null);
            vk!.DestroyFence(device, inFlightFences![i], null);
        }

        vk!.DestroyCommandPool(device, commandPool, null);

        vk!.DestroyDevice(device, null);

        if (EnableValidationLayers)
        {
            debugUtils!.DestroyDebugUtilsMessenger(instance, debugMessenger, null);
        }

        khrSurface!.DestroySurface(instance, surface, null);
        vk!.DestroyInstance(instance, null);
        vk!.Dispose();

        window?.Dispose();
    }

    private void CreateInstance()
    {
        vk = Vk.GetApi();

        if (EnableValidationLayers && !CheckValidationLayerSupport())
        {
            throw new Exception("Validation layers requested, but not available!");
        }

        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("MGSV Renderer"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version11
        };

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo
        };

        var extensions = GetRequiredExtensions();
        createInfo.EnabledExtensionCount = (uint) extensions.Length;
        createInfo.PpEnabledExtensionNames = (byte**) SilkMarshal.StringArrayToPtr(extensions);

        if (EnableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);

            DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
            PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
            createInfo.PNext = &debugCreateInfo;
        }
        else
        {
            createInfo.EnabledLayerCount = 0;
            createInfo.PNext = null;
        }

        if (vk.CreateInstance(in createInfo, null, out instance) != Result.Success)
        {
            throw new Exception("Failed to create instance.");
        }

        Marshal.FreeHGlobal((IntPtr)appInfo.PApplicationName);
        Marshal.FreeHGlobal((IntPtr)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

        if (EnableValidationLayers)
        {
            SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
        }
    }

    private void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
    {
        createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
        createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
        createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
        createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT) DebugCallback;
    }

    private void SetupDebugMessenger()
    {
        if (!EnableValidationLayers) return;

        //TryGetInstanceExtension equivilant to method CreateDebugUtilsMessengerEXT from original tutorial.
        if (!vk!.TryGetInstanceExtension(instance, out debugUtils)) return;

        DebugUtilsMessengerCreateInfoEXT createInfo = new();
        PopulateDebugMessengerCreateInfo(ref createInfo);

        if (debugUtils!.CreateDebugUtilsMessenger(instance, in createInfo, null, out debugMessenger) != Result.Success)
        {
            throw new Exception("failed to set up debug messenger!");
        }
    }

    private void CreateSurface()
    {
        if (!vk!.TryGetInstanceExtension(instance, out khrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found.");
        }

        surface = window!.VkSurface!.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }

    private void PickPhysicalDevice() 
    {
        var devices = vk!.GetPhysicalDevices(instance);

        foreach (var device in devices)
        {
            if (IsDeviceSuitable(device))
            {
                physicalDevice = device;
                msaaSamples = GetMaxUsableSampleCount();
                break;
            }
        }

        if (physicalDevice.Handle == 0)
        {
            throw new Exception("Failed to find a suitable GPU");
        }
    }

    private void CreateLogicalDevice()
    {
        QueueFamilyIndices indices = FindQueueFamilies(physicalDevice);

        var uniqueQueueFamilies = new[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };
        uniqueQueueFamilies = uniqueQueueFamilies.Distinct().ToArray();

        using var mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
        var queueCreateInfos = (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

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

        PhysicalDeviceFeatures deviceFeatures = new()
        {
            SamplerAnisotropy = true,
            SampleRateShading = true
        };

        DeviceCreateInfo createInfo = new()
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
            createInfo.EnabledLayerCount = (uint) validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**) SilkMarshal.StringArrayToPtr(validationLayers);
        }
        else
        {
            createInfo.EnabledLayerCount = 0;
        }

        if (vk!.CreateDevice(physicalDevice, in createInfo, null, out device) != Result.Success)
        {
            throw new Exception("Failed to create logical device!");
        }

        vk!.GetDeviceQueue(device, indices.GraphicsFamily.Value, 0, out graphicsQueue);
        vk!.GetDeviceQueue(device, indices.PresentFamily.Value, 0, out presentQueue);

        if (EnableValidationLayers)
        {
            SilkMarshal.Free((nint) createInfo.PpEnabledLayerNames);
        }

        SilkMarshal.Free((nint) createInfo.PpEnabledExtensionNames);
    }

    private void CreateSwapChain()
    {
        var swapChainSupport = QuerySwapChainSupport(physicalDevice);

        SurfaceFormatKHR surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
        PresentModeKHR presentMode = ChooseSwapPresentMode(swapChainSupport.PresentModes);
        Extent2D extent = ChooseSwapExtent(swapChainSupport.Capabilities);

        var imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
        if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
        {
            imageCount = swapChainSupport.Capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR createInfo = new()
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
            createInfo = createInfo with
            {
                ImageSharingMode = SharingMode.Concurrent,
                QueueFamilyIndexCount = 2,
                PQueueFamilyIndices = queueFamilyIndices
            };
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        createInfo = createInfo with
        {
            PreTransform = swapChainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default
        };

        if (!vk!.TryGetDeviceExtension(instance, device, out khrSwapChain))
        {
            throw new NotSupportedException("VK_KHR_swapchain extension not found.");
        }

        if (khrSwapChain!.CreateSwapchain(device, in createInfo, null, out swapChain) != Result.Success)
        {
            throw new Exception("Failed to create swap chain!");
        }

        khrSwapChain.GetSwapchainImages(device, swapChain, ref imageCount, null);
        swapChainImages = new Image[imageCount];
        fixed (Image* swapChainImagesPtr = swapChainImages)
        {
            khrSwapChain.GetSwapchainImages(device, swapChain, ref imageCount, swapChainImagesPtr);
        }

        swapChainImageFormat = surfaceFormat.Format;
        swapChainExtent = extent;
    }

    private void RecreateSwapChain()
    {
        Vector2D<int> framebufferSize = window!.FramebufferSize;

        while (framebufferSize.X == 0 || framebufferSize.Y == 0)
        {
            framebufferSize = window.FramebufferSize;
            window.DoEvents();
        }

        vk!.DeviceWaitIdle(device);

        CleanUpSwapChains();

        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateGraphicsPipeline();
        CreateColorResources();
        CreateDepthResources();
        CreateFramebuffers();
        CreateUniformBuffers();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateCommandBuffers();

        imagesInFlight = new Fence[swapChainImages!.Length];
    }

    private void CleanUpSwapChains()
    {
        vk!.DestroyImageView(device, colorImageView, null);
        vk!.DestroyImage(device, colorImage, null);
        vk!.FreeMemory(device, colorImageMemory, null);

        vk!.DestroyImageView(device, depthImageView, null);
        vk!.DestroyImage(device, depthImage, null);
        vk!.FreeMemory(device, depthImageMemory, null);

        foreach (var framebuffer in swapChainFramebuffers!)
        {
            vk!.DestroyFramebuffer(device, framebuffer, null);
        }

        fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
        {
            vk!.FreeCommandBuffers(device, commandPool, (uint) commandBuffers!.Length, commandBuffersPtr);
        }

        vk!.DestroyPipeline(device, graphicsPipeline, null);
        vk!.DestroyPipelineLayout(device, pipelineLayout, null);
        vk!.DestroyRenderPass(device, renderPass, null);

        foreach (var imageView in swapChainImageViews!)
        {
            vk!.DestroyImageView(device, imageView, null);
        }

        khrSwapChain!.DestroySwapchain(device, swapChain, null);

        for (int i = 0; i < swapChainImages!.Length; i++)
        {
            vk!.DestroyBuffer(device, uniformBuffers![i], null);
            vk!.FreeMemory(device, uniformBuffersMemory![i], null);
        }

        vk!.DestroyDescriptorPool(device, descriptorPool, null);
    }

    private void CreateImageViews()
    {
        swapChainImageViews = new ImageView[swapChainImages!.Length];

        for (int i = 0; i < swapChainImages.Length; i++)
            swapChainImageViews[i] = CreateImageView(swapChainImages[i], swapChainImageFormat, ImageAspectFlags.ColorBit, 1);
    }

    private void CreateRenderPass()
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = swapChainImageFormat,
            Samples = msaaSamples,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        AttachmentDescription depthAttachment = new()
        {
            Format = FindDepthFormat(),
            Samples = msaaSamples,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        AttachmentDescription colorAttachmentResolve = new()
        {
            Format = swapChainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
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

        AttachmentReference depthAttachmentRef = new()
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        AttachmentReference colorAttachmentResolveRef = new()
        {
            Attachment = 2,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
            PDepthStencilAttachment = &depthAttachmentRef,
            PResolveAttachments = &colorAttachmentResolveRef
        };

        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        AttachmentDescription[] attachments = new[] { colorAttachment, depthAttachment, colorAttachmentResolve };

        fixed (AttachmentDescription* attachmentsPtr = attachments)
        {
            RenderPassCreateInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint) attachments.Length,
                PAttachments = attachmentsPtr,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency
            };

            if (vk!.CreateRenderPass(device, in renderPassInfo, null, out renderPass) != Result.Success)
            {
                throw new Exception("Failed to create render pass!");
            }
        }
    }

    private void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding uboLayoutBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PImmutableSamplers = null,
            StageFlags = ShaderStageFlags.VertexBit
        };

        DescriptorSetLayoutBinding samplerLayoutBinding = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = null,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding[] layoutBindings = new[] { uboLayoutBinding, samplerLayoutBinding };
        
        fixed (DescriptorSetLayoutBinding* layoutBindingsPtr = layoutBindings)
        fixed (DescriptorSetLayout* descriptorSetLayoutPtr = &descriptorSetLayout)
        {
            DescriptorSetLayoutCreateInfo layoutInfo = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint) layoutBindings.Length,
                PBindings = layoutBindingsPtr 
            };

            if (vk!.CreateDescriptorSetLayout(device, in layoutInfo, null, descriptorSetLayoutPtr) != Result.Success)
            {
                throw new Exception("Failed to create descriptor set layout!");
            }
        }
    }

    private void CreateGraphicsPipeline()
    {
        var vertShaderCode = File.ReadAllBytes("shaders/vert.spv");
        var fragShaderCode = File.ReadAllBytes("shaders/frag.spv");

        var vertShaderModule = CreateShaderModule(vertShaderCode);
        var fragShaderModule = CreateShaderModule(fragShaderCode);

        PipelineShaderStageCreateInfo vertShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertShaderModule,
            PName = (byte*) SilkMarshal.StringToPtr("main")
        };

        PipelineShaderStageCreateInfo fragShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragShaderModule,
            PName = (byte*) SilkMarshal.StringToPtr("main")
        };

        var shaderStages = stackalloc[]
        {
            vertShaderStageInfo,
            fragShaderStageInfo
        };

        var bindingDescription = Vertex.GetBindingDescription();
        var attributeDescriptions = Vertex.GetAttributeDescriptions();

        fixed (VertexInputAttributeDescription* attributeDescriptionsPtr = attributeDescriptions)
        fixed (DescriptorSetLayout* descriptorSetLayoutPtr = &descriptorSetLayout)
        {
            PipelineVertexInputStateCreateInfo vertexInputInfo = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDescription,
                VertexAttributeDescriptionCount = (uint) attributeDescriptions.Length,
                PVertexAttributeDescriptions = attributeDescriptionsPtr
            };

            PipelineInputAssemblyStateCreateInfo inputAssembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = Vk.False
            };

            Viewport viewport = new()
            {
                X = 0.0f,
                Y = 0.0f,
                Width = (float) swapChainExtent.Width,
                Height = (float) swapChainExtent.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };

            Rect2D scissor = new()
            {
                Offset = { X = 0, Y = 0 },
                Extent = swapChainExtent
            };

            PipelineViewportStateCreateInfo viewportState = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor
            };

            PipelineRasterizationStateCreateInfo rasterizer = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = Vk.False,
                RasterizerDiscardEnable = Vk.False,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.BackBit,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = Vk.False,
            };

            PipelineMultisampleStateCreateInfo multisampling = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = true,
                RasterizationSamples = msaaSamples,
                MinSampleShading = 0.2f
            };

            PipelineColorBlendAttachmentState colorBlendAttachment = new()
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = Vk.False,
            };

            PipelineColorBlendStateCreateInfo colorBlending = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = Vk.False,
                LogicOp = LogicOp.Copy,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            colorBlending.BlendConstants[0] = 0.0f;
            colorBlending.BlendConstants[1] = 0.0f;
            colorBlending.BlendConstants[2] = 0.0f;
            colorBlending.BlendConstants[3] = 0.0f;

            PipelineLayoutCreateInfo pipelineLayoutInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = descriptorSetLayoutPtr,
                PushConstantRangeCount = 0
            };

            if (vk!.CreatePipelineLayout(device, in pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
            {
                throw new Exception("Failed to create pipeline layout!");
            }

            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = Vk.True,
                DepthWriteEnable = Vk.True,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = Vk.False,
                StencilTestEnable = Vk.False
            };

            GraphicsPipelineCreateInfo pipelineInfo = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlending,
                PDepthStencilState = &depthStencil,
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0,
                BasePipelineHandle = default
            };

            if (vk!.CreateGraphicsPipelines(device, default, 1, in pipelineInfo, null, out graphicsPipeline) != Result.Success)
            {
                throw new Exception("Failed to create graphics pipeline!");
            }
        }

        vk!.DestroyShaderModule(device, vertShaderModule, null);
        vk!.DestroyShaderModule(device, fragShaderModule, null);

        SilkMarshal.Free((nint)vertShaderStageInfo.PName);
        SilkMarshal.Free((nint)fragShaderStageInfo.PName);
    }

    private void CreateFramebuffers()
    {
        swapChainFramebuffers = new Framebuffer[swapChainImageViews!.Length];

        for (int i = 0; i < swapChainImageViews!.Length; i++)
        {
            var attachments = new[] { colorImageView, depthImageView, swapChainImageViews[i] }; 

            fixed (ImageView* attachmentsPtr = attachments)
            {
                FramebufferCreateInfo framebufferInfo = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = renderPass,
                    AttachmentCount = (uint) attachments.Length,
                    PAttachments = attachmentsPtr,
                    Width = swapChainExtent.Width,
                    Height = swapChainExtent.Height,
                    Layers = 1
                };

                if (vk!.CreateFramebuffer(device, in framebufferInfo, null, out swapChainFramebuffers[i]) != Result.Success)
                {
                    throw new Exception("Failed to create framebuffer!");
                }
            }
        }
    }

    private void CreateCommandPool()
    {
        var queueFamilyIndices = FindQueueFamilies(physicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamilyIndices.GraphicsFamily!.Value
        };

        if (vk!.CreateCommandPool(device, in poolInfo, null, out commandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool!");
        }
    }

    private void CreateColorResources()
    {
        Format colorFormat = swapChainImageFormat;

        CreateImage(swapChainExtent.Width, swapChainExtent.Height, 1, msaaSamples, colorFormat,
                ImageTiling.Optimal, ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.ColorAttachmentBit,
                MemoryPropertyFlags.DeviceLocalBit, ref colorImage, ref colorImageMemory);
        colorImageView = CreateImageView(colorImage, colorFormat, ImageAspectFlags.ColorBit, 1);
    }

    private void CreateDepthResources()
    {
        Format depthFormat = FindDepthFormat();

        CreateImage(swapChainExtent.Width, swapChainExtent.Height, 1, msaaSamples, depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit, MemoryPropertyFlags.DeviceLocalBit, ref depthImage, ref depthImageMemory);
        depthImageView = CreateImageView(depthImage, depthFormat, ImageAspectFlags.DepthBit, 1);
    }

    private Format FindSupportedFormat(IEnumerable<Format> candidates, ImageTiling tiling, FormatFeatureFlags features)
    {
        foreach (var format in candidates)
        {
            vk!.GetPhysicalDeviceFormatProperties(physicalDevice, format, out var props);

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

    private Format FindDepthFormat()
    {
        return FindSupportedFormat(new[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint },
                ImageTiling.Optimal,
                FormatFeatureFlags.DepthStencilAttachmentBit);
    }

    private void CreateTextureImage()
    {
        using (var stream = File.OpenRead(TexturePath))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            var image = Stbi.LoadFromMemory(memoryStream, 4);

            ulong imageSize = (ulong) (image.Width * image.Height * 4);
            mipLevels = (uint)(Math.Floor(Math.Log2(Math.Max(image.Width, image.Height))) + 1);

            Buffer stagingBuffer = default;
            DeviceMemory stagingBufferMemory = default;
            CreateBuffer(imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref stagingBuffer, ref stagingBufferMemory);

            void* data;
            vk!.MapMemory(device, stagingBufferMemory, 0, imageSize, 0, &data);
            image.Data.CopyTo(new Span<byte>(data, (int)imageSize));
            vk!.UnmapMemory(device, stagingBufferMemory);

            CreateImage((uint) image.Width, (uint) image.Height, mipLevels, SampleCountFlags.Count1Bit, Format.R8G8B8A8Srgb,
                    ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                    MemoryPropertyFlags.DeviceLocalBit, ref textureImage, ref textureImageMemory);

            TransitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, mipLevels);
            CopyBufferToImage(stagingBuffer, textureImage, (uint) image.Width, (uint) image.Height);

            vk!.DestroyBuffer(device, stagingBuffer, null);
            vk!.FreeMemory(device, stagingBufferMemory, null);

            GenerateMipmaps(textureImage, Format.R8G8B8A8Srgb, (uint) image.Width, (uint) image.Height, mipLevels);
        }
    }

    private void GenerateMipmaps(Image image, Format imageFormat, uint texWidth, uint texHeight, uint mipLevels)
    {
        vk!.GetPhysicalDeviceFormatProperties(physicalDevice, imageFormat, out var formatProperties);

        if ((formatProperties.OptimalTilingFeatures & FormatFeatureFlags.SampledImageFilterLinearBit) == 0)
        {
            throw new Exception("Texture image format does not support linear blitting!");
        }

        CommandBuffer commandBuffer = BeginSingleTimeCommand();

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            Image = image,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            SubresourceRange = 
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = 1,
                LevelCount = 1
            }
        };

        var mipWidth = texWidth;
        var mipHeight = texHeight;

        for (uint i = 1; i < mipLevels; i++)
        {
            barrier.SubresourceRange.BaseMipLevel = i - 1;
            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.TransferSrcOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;

            vk!.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit, 0,
                    0, null,
                    0, null,
                    1, in barrier);

            ImageBlit blit = new()
            {
                SrcOffsets = 
                {
                    Element0 = new Offset3D(0,0,0),
                    Element1 = new Offset3D((int)mipWidth, (int)mipHeight, 1)
                },
                SrcSubresource =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = i - 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                DstOffsets = 
                {
                    Element0 = new Offset3D(0,0,0),
                    Element1 = new Offset3D((int)(mipWidth > 1 ? mipWidth / 2 : 1), (int)(mipHeight > 1 ? mipHeight / 2 : 1),1)
                },
                DstSubresource = 
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = i,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            vk!.CmdBlitImage(commandBuffer, image, ImageLayout.TransferSrcOptimal,
                    image, ImageLayout.TransferDstOptimal,
                    1, in blit, Filter.Linear);

            barrier.OldLayout = ImageLayout.TransferSrcOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            vk!.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
                    0, null,
                    0, null,
                    1, in barrier);

            if (mipWidth > 1) mipWidth /= 2;
            if (mipHeight > 1) mipHeight /= 2;
        }

        barrier.SubresourceRange.BaseMipLevel = mipLevels - 1;
        barrier.OldLayout = ImageLayout.TransferDstOptimal;
        barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        barrier.DstAccessMask = AccessFlags.ShaderReadBit;

        vk!.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
                0, null,
                0, null,
                1, in barrier);

        EndSingleTimeCommands(commandBuffer);
    }

    private SampleCountFlags GetMaxUsableSampleCount()
    {
        vk!.GetPhysicalDeviceProperties(physicalDevice, out var physicalDeviceProperties);

        SampleCountFlags counts = physicalDeviceProperties.Limits.FramebufferColorSampleCounts & physicalDeviceProperties.Limits.FramebufferDepthSampleCounts;
        if ((counts & SampleCountFlags.Count64Bit) != 0) return SampleCountFlags.Count64Bit;
        if ((counts & SampleCountFlags.Count32Bit) != 0) return SampleCountFlags.Count32Bit;
        if ((counts & SampleCountFlags.Count16Bit) != 0) return SampleCountFlags.Count16Bit;
        if ((counts & SampleCountFlags.Count8Bit) != 0) return SampleCountFlags.Count8Bit;
        if ((counts & SampleCountFlags.Count4Bit) != 0) return SampleCountFlags.Count4Bit;
        if ((counts & SampleCountFlags.Count2Bit) != 0) return SampleCountFlags.Count2Bit;

        return SampleCountFlags.Count1Bit;
    }

    private void CreateTextureImageView()
    {
        textureImageView = CreateImageView(textureImage, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit, mipLevels);
    }

    private void CreateTextureSampler()
    {
        PhysicalDeviceProperties properties;
        vk!.GetPhysicalDeviceProperties(physicalDevice, &properties);

        SamplerCreateInfo samplerInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = Vk.True,
            MaxAnisotropy = properties.Limits.MaxSamplerAnisotropy,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = Vk.False,
            CompareEnable = Vk.False,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0.0f,
            MinLod = 0.0f,
            MaxLod = Vk.LodClampNone
        };
        
        if (vk!.CreateSampler(device, in samplerInfo, null, out textureSampler) != Result.Success)
        {
            throw new Exception("Failed to create texture sampler!");
        }
    }

    private ImageView CreateImageView(Image image, Format format, ImageAspectFlags aspectFlags, uint mipLevels)
    {
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = 
            {
                AspectMask = aspectFlags,
                BaseMipLevel = 0,
                LevelCount = mipLevels,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView imageView;
        if (vk!.CreateImageView(device, in viewInfo, null, out imageView) != Result.Success)
        {
            throw new Exception("Failed to create image view!");
        }

        return imageView;
    }

    private void CreateImage(uint width, uint height, uint mipLevels, SampleCountFlags numSamples, Format format, ImageTiling tiling, ImageUsageFlags usage, MemoryPropertyFlags properties, ref Image image, ref DeviceMemory imageMemory)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = 
            {
                Width = width,
                Height = height,
                Depth = 1
            },
            MipLevels = mipLevels,
            ArrayLayers = 1,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            Samples = numSamples,
            SharingMode = SharingMode.Exclusive
        };

        fixed (Image* imagePtr = &image)
        {
            if (vk!.CreateImage(device, in imageInfo, null, imagePtr) != Result.Success)
            {
                throw new Exception("Failed to create image!");
            }
        }

        vk!.GetImageMemoryRequirements(device, image, out MemoryRequirements memRequirements);

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* imageMemoryPtr = &imageMemory)
        {
            if (vk!.AllocateMemory(device, in allocateInfo, null, imageMemoryPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate image memory!");
            }
        }

        vk!.BindImageMemory(device, image, imageMemory, 0);
    }

    private void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout, uint mipLevels)
    {
        CommandBuffer commandBuffer = BeginSingleTimeCommand();

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange =
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = mipLevels,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
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

        vk!.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, in barrier);

        EndSingleTimeCommands(commandBuffer);
    }

    private void CopyBufferToImage(Buffer buffer, Image image, uint width, uint height)
    {
        CommandBuffer commandBuffer = BeginSingleTimeCommand();

        BufferImageCopy region = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = 
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1 
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };

        vk!.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, in region);

        EndSingleTimeCommands(commandBuffer);
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, ref Buffer buffer, ref DeviceMemory bufferMemory)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        fixed (Buffer* bufferPtr = &buffer)
        {
            if (vk!.CreateBuffer(device, in bufferInfo, null, bufferPtr) != Result.Success)
            {
                throw new Exception("Failed to create vertex buffer!");
            }
        }

        MemoryRequirements memRequirements = new();
        vk!.GetBufferMemoryRequirements(device, buffer, out memRequirements);

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* bufferMemoryPtr = &bufferMemory)
        {
            if (vk!.AllocateMemory(device, in allocateInfo, null, bufferMemoryPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate vertex buffer memory!");
            }
        }

        vk!.BindBufferMemory(device, buffer, bufferMemory, 0);
    }

    private CommandBuffer BeginSingleTimeCommand()
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = commandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        vk!.AllocateCommandBuffers(device, in allocateInfo, out commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        vk!.BeginCommandBuffer(commandBuffer, in beginInfo);

        return commandBuffer;
    }

    private void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        vk!.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        vk!.QueueSubmit(graphicsQueue, 1, in submitInfo, default);
        vk!.QueueWaitIdle(graphicsQueue);

        vk!.FreeCommandBuffers(device, commandPool, 1, in commandBuffer);
    }

    private void LoadModel()
    {
        using var assimp = Assimp.GetApi();
        var scene = assimp.ImportFile(ModelPath, (uint) PostProcessPreset.TargetRealTimeMaximumQuality);

        var vertexMap = new Dictionary<Vertex, uint>();
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        VisitSceneNode(scene->MRootNode);

        assimp.ReleaseImport(scene);

        this.vertices = vertices.ToArray();
        this.indices = indices.ToArray();

        void VisitSceneNode(Node* node)
        {
            for (int m = 0; m < node->MNumMeshes; m++)
            {
                var mesh = scene->MMeshes[node->MMeshes[m]];

                for (int f = 0; f < mesh->MNumFaces; f++)
                {
                    var face = mesh->MFaces[f];

                    for (int i = 0; i < face.MNumIndices; i++)
                    {
                        uint index = face.MIndices[i];

                        var position = mesh->MVertices[index];
                        var texture = mesh->MTextureCoords[0][(int)index];

                        Vertex vertex = new()
                        {
                            pos = new Vector3D<float>(position.X, position.Y, position.Z),
                            color = new Vector3D<float>(1.0f, 1.0f, 1.0f),
                            texCoord = new Vector2D<float>(texture.X, 1.0f - texture.Y)
                        };

                        if (vertexMap.TryGetValue(vertex, out var meshIndex))
                        {
                            indices.Add(meshIndex);
                        }
                        else
                        {
                            indices.Add((uint) vertices.Count);
                            vertexMap[vertex] = (uint) vertices.Count;
                            vertices.Add(vertex);
                        }
                    }
                }
            }

            for (int c = 0; c < node->MNumChildren; c++)
            {
                VisitSceneNode(node->MChildren[c]);
            }
        }
    }

    private void CreateVertexBuffer()
    {
        ulong bufferSize = (ulong)(Unsafe.SizeOf<Vertex>() * vertices!.Length);

        Buffer stagingBuffer = default;
        DeviceMemory stagingBufferMemory = default;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref stagingBuffer, ref stagingBufferMemory);

        void* data;
        vk!.MapMemory(device, stagingBufferMemory, 0, bufferSize, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        vk!.UnmapMemory(device, stagingBufferMemory);

        CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref vertexBuffer, ref vertexBufferMemory);

        CopyBuffer(stagingBuffer, vertexBuffer, bufferSize);

        vk!.DestroyBuffer(device, stagingBuffer, null);
        vk!.FreeMemory(device, stagingBufferMemory, null);
    }

    private void CreateIndexBuffer()
    {
        ulong bufferSize = (ulong)(Unsafe.SizeOf<uint>() * indices!.Length);

        Buffer stagingBuffer = default;
        DeviceMemory stagingBufferMemory = default;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref stagingBuffer, ref stagingBufferMemory);

        void* data;
        vk!.MapMemory(device, stagingBufferMemory, 0, bufferSize, 0, &data);
        indices.AsSpan().CopyTo(new Span<uint>(data, indices.Length));
        vk!.UnmapMemory(device, stagingBufferMemory);

        CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.DeviceLocalBit, ref indexBuffer, ref indexBufferMemory);

        CopyBuffer(stagingBuffer, indexBuffer, bufferSize);

        vk!.DestroyBuffer(device, stagingBuffer, null);
        vk!.FreeMemory(device, stagingBufferMemory, null);
    }

    private void CreateUniformBuffers()
    {
        ulong bufferSize = (ulong) Unsafe.SizeOf<UniformBufferObject>();

        uniformBuffers = new Buffer[swapChainImages!.Length];
        uniformBuffersMemory = new DeviceMemory[swapChainImages!.Length];

        for (int i = 0; i < swapChainImages!.Length; i++)
        {
            CreateBuffer(bufferSize, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, ref uniformBuffers[i], ref uniformBuffersMemory[i]);
        }
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize[] poolSizes = new DescriptorPoolSize[2];
        poolSizes[0].Type = DescriptorType.UniformBuffer;
        poolSizes[0].DescriptorCount = (uint) swapChainImages!.Length;
        poolSizes[1].Type = DescriptorType.CombinedImageSampler;
        poolSizes[1].DescriptorCount = (uint) swapChainImages!.Length;

        fixed (DescriptorPoolSize* poolSizesPtr = poolSizes)
        fixed (DescriptorPool* descriptorPoolPtr = &descriptorPool)
        {
            DescriptorPoolCreateInfo poolInfo = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint) poolSizes.Length,
                PPoolSizes = poolSizesPtr,
                MaxSets = (uint) swapChainImages.Length
            };

            if (vk!.CreateDescriptorPool(device, in poolInfo, null, descriptorPoolPtr) != Result.Success)
            {
                throw new Exception("Failed to create descriptor pool!");
            }
        }
    }

    private void CreateDescriptorSets()
    {
        var layouts = new DescriptorSetLayout[swapChainImages!.Length];
        Array.Fill(layouts, descriptorSetLayout);

        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocateInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = (uint) swapChainImages!.Length,
                PSetLayouts = layoutsPtr
            };

            descriptorSets = new DescriptorSet[swapChainImages.Length];
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            {
                if (vk!.AllocateDescriptorSets(device, in allocateInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate descriptor sets!");
                }
            }
        }

        for (int i = 0; i < swapChainImages!.Length; i++)
        {
            DescriptorBufferInfo bufferInfo = new()
            {
                Buffer = uniformBuffers![i],
                Offset = 0,
                Range = (ulong) Unsafe.SizeOf<UniformBufferObject>()
            };

            DescriptorImageInfo imageInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = textureImageView,
                Sampler = textureSampler
            };

            var descriptorWrites = new WriteDescriptorSet[]
            {
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.UniformBuffer,
                    DescriptorCount = 1,
                    PBufferInfo = &bufferInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfo
                }
            };

            fixed (WriteDescriptorSet* descriptorWritesPtr = descriptorWrites)
                vk!.UpdateDescriptorSets(device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, null);
        }
    }

    private void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        CommandBuffer commandBuffer = BeginSingleTimeCommand();

        BufferCopy copyRegion = new(){ Size = size };
        vk!.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, in copyRegion);
        
        EndSingleTimeCommands(commandBuffer);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        vk!.GetPhysicalDeviceMemoryProperties(physicalDevice, out PhysicalDeviceMemoryProperties memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                return (uint) i;
            }
        }
        
        throw new Exception("Failed to find suitable memory type!");
    }

    private void CreateCommandBuffers()
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
            if (vk!.AllocateCommandBuffers(device, in allocInfo, commandBuffersPtr) != Result.Success)
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

        if (vk!.BeginCommandBuffer(commandBuffer, in beginInfo) != Result.Success)
        {
            throw new Exception("Failed to begin recording command buffer!");
        }

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = swapChainFramebuffers![imageIndex],
            RenderArea = 
            {
                Offset = { X = 0, Y = 0 },
                Extent = swapChainExtent
            }
        };

        ClearValue[] clearValues = new ClearValue[2];
        clearValues[0].Color = new() { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 };
        clearValues[1].DepthStencil = new() { Depth = 1, Stencil = 0 };

        fixed (ClearValue* clearValuesPtr = clearValues)
        {
            renderPassInfo.ClearValueCount = (uint) clearValues.Length;
            renderPassInfo.PClearValues = clearValuesPtr;

            vk!.CmdBeginRenderPass(commandBuffer, &renderPassInfo, SubpassContents.Inline);
        }


        vk!.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, graphicsPipeline);

        var vertexBuffers = new Buffer[]{ vertexBuffer };
        var offsets = new ulong[] { 0 };

        fixed (ulong* offsetsPtr = offsets)
        fixed (Buffer* vertexBuffersPtr = vertexBuffers)
        {
            vk!.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffersPtr, offsetsPtr);
        }

        vk!.CmdBindIndexBuffer(commandBuffer, indexBuffer, 0, IndexType.Uint32);

        vk!.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, in descriptorSets![imageIndex], 0, null);

        vk!.CmdDrawIndexed(commandBuffer, (uint) indices!.Length, 1, 0, 0, 0);

        vk!.CmdEndRenderPass(commandBuffer);

        if (vk!.EndCommandBuffer(commandBuffer) != Result.Success)
        {
            throw new Exception("Failed to record command buffer!");
        }
    }

    private void CreateSyncObjects()
    {
        imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
        renderFinishedSemaphores = new Semaphore[MaxFramesInFlight];
        inFlightFences = new Fence[MaxFramesInFlight];
        imagesInFlight = new Fence[swapChainImages!.Length];

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
            if (vk!.CreateSemaphore(device, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                vk!.CreateSemaphore(device, in semaphoreInfo, null, out renderFinishedSemaphores[i]) != Result.Success ||
                vk!.CreateFence(device, in fenceInfo, null, out inFlightFences[i]) != Result.Success)
            {
                throw new Exception("Failed to create synchronization objects!");
            }
        }
    }

    private void UpdateUniformBuffer(uint currentImage)
    {
        // var time = (float)window!.Time;
        var time = 0.0f;

        UniformBufferObject ubo = new()
        {
            model = Matrix4X4<float>.Identity * Matrix4X4.CreateFromAxisAngle<float>(new Vector3D<float>(0, 0, 1), time * Radians(90.0f)),
            view = Matrix4X4.CreateLookAt(new Vector3D<float>(2, 2, 2), new Vector3D<float>(0, 0, 0), new Vector3D<float>(0, 0, 1)),
            proj = Matrix4X4.CreatePerspectiveFieldOfView(Radians(45.0f), (float) swapChainExtent.Width / swapChainExtent.Height, 0.1f, 10.0f)
        };
        ubo.proj.M22 *= -1;

        void* data;
        vk!.MapMemory(device, uniformBuffersMemory![currentImage], 0, (ulong)Unsafe.SizeOf<UniformBufferObject>(), 0, &data);
        new Span<UniformBufferObject>(data, 1)[0] = ubo;
        vk!.UnmapMemory(device, uniformBuffersMemory![currentImage]);

        static float Radians(float angle) => angle * MathF.PI / 180f;
    }

    private void DrawFrame(double delta)
    {
        vk!.WaitForFences(device, 1, ref inFlightFences![currentFrame], Vk.True, ulong.MaxValue);

        uint imageIndex = 0;
        Result result = khrSwapChain!.AcquireNextImage(device, swapChain, ulong.MaxValue, imageAvailableSemaphores![currentFrame], default, ref imageIndex);
        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapChain();
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("Failed to acquire swap chain image!");
        }

        UpdateUniformBuffer(imageIndex);

        if (imagesInFlight![imageIndex].Handle != default)
        {
            vk!.WaitForFences(device, 1, ref imagesInFlight[imageIndex], true, ulong.MaxValue);
        }
        imagesInFlight[imageIndex] = inFlightFences[currentFrame];

        // Only reset fence if we are submitting work
        vk!.ResetFences(device, 1, ref inFlightFences![currentFrame]);

        vk!.ResetCommandBuffer(commandBuffers![currentFrame], CommandBufferResetFlags.None);
        RecordCommandBuffer(commandBuffers[currentFrame], imageIndex);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
        };
        
        var waitSemaphores = stackalloc[] { imageAvailableSemaphores![currentFrame] };
        var waitStages = stackalloc[] {PipelineStageFlags.ColorAttachmentOutputBit};
        var buffer = commandBuffers[currentFrame];

        submitInfo = submitInfo with 
        {
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &buffer
        };

        var signalSemaphores = stackalloc[] { renderFinishedSemaphores![currentFrame] };
        submitInfo = submitInfo with 
        {
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores
        };

        if (vk!.QueueSubmit(graphicsQueue, 1, in submitInfo, inFlightFences[currentFrame]) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer!");
        }

        var swapChains = stackalloc[] { swapChain };
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapChains,
            PImageIndices = &imageIndex
        };

        result = khrSwapChain.QueuePresent(presentQueue, in presentInfo);
        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || framebufferResized)
        {
            RecreateSwapChain();
        }
        else if (result != Result.Success)
        {
            throw new Exception("Failed to present swap chain image!");
        }

        currentFrame = (currentFrame + 1) % MaxFramesInFlight;
    }

    private ShaderModule CreateShaderModule(byte[] code)
    {
        ShaderModuleCreateInfo createInfo = new()
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint) code.Length,
        };

        ShaderModule shaderModule;

        fixed (byte* codePtr = code)
        {
            createInfo.PCode = (uint*) codePtr;

            if (vk!.CreateShaderModule(device, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader module!");
            }
        }

        return shaderModule;
    }

    private SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
    {
        foreach (var availableFormat in availableFormats)
        {
            if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return availableFormat;
            }
        }

        return availableFormats[0];
    }

    private PresentModeKHR ChooseSwapPresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes)
    {
        foreach (var availablePresentMode in availablePresentModes)
        {
            if (availablePresentMode == PresentModeKHR.MailboxKhr)
            {
                return availablePresentMode;
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
            var framebufferSize = window!.FramebufferSize;

            Extent2D actualExtent = new()
            {
                Width = (uint)framebufferSize.X,
                Height = (uint)framebufferSize.Y
            };
            
            actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
            actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);

            return actualExtent;
        }
    }

    private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice device)
    {
        var details = new SwapChainSupportDetails();

        khrSurface!.GetPhysicalDeviceSurfaceCapabilities(device, surface, out details.Capabilities);

        uint formatCount = 0;
        khrSurface!.GetPhysicalDeviceSurfaceFormats(device, surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                khrSurface!.GetPhysicalDeviceSurfaceFormats(device, surface, ref formatCount, formatsPtr);
            }
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        khrSurface!.GetPhysicalDeviceSurfacePresentModes(device, surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* presentModesPtr = details.PresentModes)
            {
                khrSurface!.GetPhysicalDeviceSurfacePresentModes(device, surface, ref presentModeCount, presentModesPtr);
            }
        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
    }

    private bool IsDeviceSuitable(PhysicalDevice device)
    {
        var indices = FindQueueFamilies(device);

        bool extensionsSupported = CheckDeviceExtensionSupport(device);

        bool swapChainAdequate = false;
        if (extensionsSupported)
        {
            var swapChainSupport = QuerySwapChainSupport(device);
            swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
        }

        vk!.GetPhysicalDeviceFeatures(device, out PhysicalDeviceFeatures supportedFeatures);

        return indices.IsComplete() && extensionsSupported && swapChainAdequate && supportedFeatures.SamplerAnisotropy;
    }

    private bool CheckDeviceExtensionSupport(PhysicalDevice device)
    {
        uint extensionCount = 0;
        vk!.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extensionCount, null);

        var availableExtensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
        {
            vk!.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extensionCount, availableExtensionsPtr);
        }

        var availableExtensionNames = availableExtensions.Select(extension => Marshal.PtrToStringAnsi((IntPtr) extension.ExtensionName)).ToHashSet();

        return deviceExtensions.All(availableExtensionNames.Contains);
    }

    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
    {
        var indices = new QueueFamilyIndices();
        
        uint queueFamilyCount = 0;
        vk!.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            vk!.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilyCount, queueFamiliesPtr);
        }

        uint i = 0;
        foreach (var queueFamily in queueFamilies)
        {
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            khrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, surface, out var presentSupport);

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

    private string[] GetRequiredExtensions()
    {
        var glfwExtensions = window!.VkSurface!.GetRequiredExtensions(out var glfwExtensionCount);
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
        vk!.EnumerateInstanceLayerProperties(ref layerCount, null);
        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
        {
            vk!.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
        }

        var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((IntPtr)layer.LayerName)).ToHashSet();

        return validationLayers.All(availableLayerNames.Contains);
    }

    private void FramebufferResizeCallback(Vector2D<int> newSize)
    {
        framebufferResized = true;
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
}
