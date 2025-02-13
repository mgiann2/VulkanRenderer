using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
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

public struct SwapchainSupportDetails
{
    public SurfaceCapabilitiesKHR Capabilities;
    public SurfaceFormatKHR[] Formats;
    public PresentModeKHR[] PresentModes;
}

public struct SwapchainInfo
{
    public KhrSwapchain KhrSwapchain { get; init; }
    public SwapchainKHR Swapchain { get; init; }
    public Image[] Images { get; init; }
    public ImageView[] ImageViews { get; init; }
    public Extent2D Extent { get; init; }
    public Format ImageFormat { get; init; }
}

unsafe public class SCDevice : IDisposable
{
    public Device LogicalDevice { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public SwapchainInfo SwapchainInfo { get; private set; }
    public Queue GraphicsQueue { get; }
    public Queue PresentQueue { get; }
    public CommandPool CommandPool { get; }

    public QueueFamilyIndices QueueFamilyIndices => FindQueueFamilies(PhysicalDevice);

    private Vk vk = VulkanHelper.Vk;
    private Instance instance;
    private ExtDebugUtils? debugUtils;
    private DebugUtilsMessengerEXT? debugMessenger;
    private KhrSurface khrSurface;
    private SurfaceKHR surface;

    readonly string[] validationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName
    };

    private readonly bool EnableValidationLayers;
    private bool disposedValue;

    public SCDevice(IWindow window, bool enableValidationLayers)
    {
        if (window.VkSurface == null) throw new Exception("No vk surface exists on window!");

        EnableValidationLayers = enableValidationLayers; 

        instance = CreateInstance(window.VkSurface);
        (debugMessenger, debugUtils) = SetupDebugMessenger();
        (khrSurface, surface) = CreateSurface(window.VkSurface);
        PhysicalDevice = PickPhysicalDevice();
        LogicalDevice = CreateLogicalDevice();

        var indices = FindQueueFamilies(PhysicalDevice);
        vk.GetDeviceQueue(LogicalDevice, indices.GraphicsFamily!.Value, 0, out var graphicsQueue);
        vk.GetDeviceQueue(LogicalDevice, indices.PresentFamily!.Value, 0, out var presentQueue);
        GraphicsQueue = graphicsQueue;
        PresentQueue = presentQueue;

        CommandPool = VulkanHelper.CreateCommandPool(this);

        SwapchainInfo = CreateSwapchain(window);
    }

    public void RecreateSwapchain(IWindow window)
    {
        // destroy old swap chain
        foreach (var imageView in SwapchainInfo.ImageViews)
        {
            vk.DestroyImageView(LogicalDevice, imageView, null);
        }
        SwapchainInfo.KhrSwapchain.DestroySwapchain(LogicalDevice, SwapchainInfo.Swapchain, null);

        SwapchainInfo = CreateSwapchain(window);
    }

    public void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout, uint layers = 1, uint mipLevels = 1)
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
                LevelCount = mipLevels,
                BaseArrayLayer = 0,
                LayerCount = layers
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

    public void CopyBufferToImage(Buffer buffer, Image image, uint width, uint height, uint layers)
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
                LayerCount = layers
            },
            ImageOffset = new() { X = 0, Y = 0, Z = 0 },
            ImageExtent = new() { Width = width, Height = height, Depth = 1 }
        };

        vk.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, in region);

        EndSingleTimeCommand(commandBuffer);
    }

    public Cubemap ImagesToCubeMap(Image[] srcImages, Extent2D imageExtent)
    {
        Image dstImage;
        DeviceMemory dstImageMemory;
        (dstImage, dstImageMemory) = VulkanHelper.CreateCubemapImage(this,
                                                               Format.R16G16B16A16Sfloat, ImageTiling.Optimal,
                                                               ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                                                               MemoryPropertyFlags.DeviceLocalBit,
                                                               imageExtent.Width, imageExtent.Width);

        TransitionImageLayout(dstImage, Format.R16G16B16A16Sfloat, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, 6);

        var commandBuffer = BeginSingleTimeCommand();
        for (uint layer = 0; layer < 6; layer++)
        {
            ImageCopy copyRegion = new()
            {
                SrcOffset = new() { X = 0, Y = 0, Z = 0 },
                SrcSubresource = new()
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1,
                    BaseArrayLayer = 0
                },
                DstOffset = new() { X = 0, Y = 0, Z = 0 },
                DstSubresource = new()
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1,
                    BaseArrayLayer = layer
                },
                Extent = new() { Width = imageExtent.Width, Height = imageExtent.Height, Depth = 1 }
            };
            vk.CmdCopyImage(commandBuffer,
                            srcImages[layer], ImageLayout.TransferSrcOptimal,
                            dstImage, ImageLayout.TransferDstOptimal,
                            1, &copyRegion);
        }
        EndSingleTimeCommand(commandBuffer);

        TransitionImageLayout(dstImage, Format.R16G16B16A16Sfloat, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, 6);

        return new Cubemap(this, dstImage, dstImageMemory);
    }

    public void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        var commandBuffer = BeginSingleTimeCommand();

        BufferCopy copyRegion = new() { Size = size };
        vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, in copyRegion);

        EndSingleTimeCommand(commandBuffer);
    }

    private CommandBuffer BeginSingleTimeCommand()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = CommandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        vk.AllocateCommandBuffers(LogicalDevice, in allocInfo, out commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        vk.BeginCommandBuffer(commandBuffer, in beginInfo);
        return commandBuffer;
    }

    private void EndSingleTimeCommand(CommandBuffer commandBuffer)
    {
        vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        vk.QueueSubmit(GraphicsQueue, 1, in submitInfo, default);
        vk.QueueWaitIdle(GraphicsQueue);

        vk.FreeCommandBuffers(LogicalDevice, CommandPool, 1, in commandBuffer);
    }

    // Object creation helper functions
    // --------------------------------

    private Instance CreateInstance(IVkSurface windowSurface)
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

        var extensions = GetRequiredExtensions(windowSurface);

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

        if (vk.CreateInstance(in instanceInfo, null, out var instance) != Result.Success)
        {
            throw new Exception("Failed to create instance!");
        }

        // free unmanaged memory
        Marshal.FreeHGlobal((nint) appInfo.PEngineName);
        Marshal.FreeHGlobal((nint) appInfo.PApplicationName);
        SilkMarshal.Free((nint) instanceInfo.PpEnabledExtensionNames);
        if (EnableValidationLayers) SilkMarshal.Free((nint) instanceInfo.PpEnabledLayerNames);

        return instance;
    }
    
    private (DebugUtilsMessengerEXT?, ExtDebugUtils?) SetupDebugMessenger()
    {
        if (!EnableValidationLayers) return (null, null);
    
        if (!vk.TryGetInstanceExtension(instance, out ExtDebugUtils debugUtils)) return (null, null);

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
        
        if (debugUtils.CreateDebugUtilsMessenger(instance, in messengerInfo, null, out var messenger) != Result.Success)
        {
            throw new Exception("Failed to create debug messenger!");
        }

        return (messenger, debugUtils);
    }

    private (KhrSurface, SurfaceKHR) CreateSurface(IVkSurface windowSurface)
    {
        if (!vk.TryGetInstanceExtension(instance, out KhrSurface khrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found!");
        }
        
        var surface = windowSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();

        return (khrSurface, surface);
    }

    private PhysicalDevice PickPhysicalDevice()
    {
        var devices = vk.GetPhysicalDevices(instance);

        foreach (var device in devices)
        {
            if (IsPhysicalDeviceSuitable(device))
            {
                return device;
            }
        }

        throw new Exception("Unable to find suitable physical device!");
    }

    private Device CreateLogicalDevice()
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
        deviceFeatures.GeometryShader = true;
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

        if (vk.CreateDevice(PhysicalDevice, in deviceInfo, null, out var logicalDevice) != Result.Success)
        {
            throw new Exception("Failed to create logical device!");
        }

        if (EnableValidationLayers) SilkMarshal.Free((nint) deviceInfo.PpEnabledLayerNames);
        SilkMarshal.Free((nint) deviceInfo.PpEnabledExtensionNames);

        return logicalDevice;
    }

    private SwapchainInfo CreateSwapchain(IWindow window)
    {
        var swapchainSupportDetails = QuerySwapChainSupport(PhysicalDevice);

        var surfaceFormat = ChooseSwapSurfaceFormat(swapchainSupportDetails.Formats);
        var presentMode = ChooseSwapPresentMode(swapchainSupportDetails.PresentModes);
        var extent = ChooseSwapExtent(swapchainSupportDetails.Capabilities, window);

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

        var indices = FindQueueFamilies(PhysicalDevice);
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

        if (!vk.TryGetDeviceExtension(instance, LogicalDevice, out KhrSwapchain khrSwapchain))
        {
            throw new Exception("VK_KHR_swapchain extension not found!");
        }

        if (khrSwapchain.CreateSwapchain(LogicalDevice, in swapchainCreateInfo, null, out var swapchain) != Result.Success)
        {
            throw new Exception("Failded to create swapchain!");
        }

        uint swapchainImageCount = 0;
        khrSwapchain.GetSwapchainImages(LogicalDevice, swapchain, ref swapchainImageCount, null);
        var swapchainImages = new Image[swapchainImageCount];
        fixed (Image* swapchainImagesPtr = swapchainImages)
        {
            khrSwapchain.GetSwapchainImages(LogicalDevice, swapchain, ref swapchainImageCount, swapchainImagesPtr);
        }

        var swapchainImageFormat = surfaceFormat.Format;
        var swapchainExtent = extent;

        // create image views
        var swapchainImageViews = new ImageView[swapchainImages.Length];
        for (int i = 0; i < swapchainImages.Length; i++)
        {
            swapchainImageViews[i] = VulkanHelper.CreateImageView(this, swapchainImages[i], swapchainImageFormat, ImageAspectFlags.ColorBit);
        }

        return new SwapchainInfo
        {
            KhrSwapchain = khrSwapchain,
            Swapchain = swapchain,
            Images = swapchainImages,
            ImageViews = swapchainImageViews,
            ImageFormat = swapchainImageFormat,
            Extent = swapchainExtent
        };
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

        PhysicalDeviceFeatures supportedFeatures;
        vk.GetPhysicalDeviceFeatures(physicalDevice, out supportedFeatures);

        return indices.IsComplete() && extensionsSupported && swapChainAdequate && supportedFeatures.SamplerAnisotropy;
    }

    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice physicalDevice)
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

    private bool CheckDeviceExtensionSupport(PhysicalDevice physicalDevice)
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

    private SwapchainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
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
            if (presentMode == PresentModeKHR.FifoKhr)
            {
                return presentMode;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, IWindow window)
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

    private string[] GetRequiredExtensions(IVkSurface windowSurface)
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
        vk.EnumerateInstanceLayerProperties(ref layerCount, null);
        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
        {
            vk.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
        }

        var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((nint) layer.LayerName)).ToHashSet();

        return validationLayers.All(availableLayerNames.Contains);
    }

    private static uint DebugCallback(
            DebugUtilsMessageSeverityFlagsEXT messageSeverity,
            DebugUtilsMessageTypeFlagsEXT messageType,
            DebugUtilsMessengerCallbackDataEXT* pCallbackData,
            void* pUserData)
    {
        if (messageSeverity == DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) 
            Console.WriteLine($"Validation layer: {Marshal.PtrToStringAnsi((nint) pCallbackData->PMessage)}");

        return Vk.False;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            foreach (var imageView in SwapchainInfo.ImageViews)
            {
                vk.DestroyImageView(LogicalDevice, imageView, null);
            }
            SwapchainInfo.KhrSwapchain.DestroySwapchain(LogicalDevice, SwapchainInfo.Swapchain, null);

            vk.DestroyDevice(LogicalDevice, null);
            khrSurface.DestroySurface(instance, surface, null);

            if (debugMessenger is DebugUtilsMessengerEXT messenger && debugUtils is ExtDebugUtils)
            {
                debugUtils.DestroyDebugUtilsMessenger(instance, messenger, null);
            }

            vk.DestroyInstance(instance, null);

            disposedValue = true;
        }
    }

    ~SCDevice()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
