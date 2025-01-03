using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Fence = Silk.NET.Vulkan.Fence;

namespace Renderer;

unsafe public static class VulkanHelper
{
    public static readonly Vk Vk = Vk.GetApi();

    public static ShaderModule CreateShaderModule(Device device, byte[] shaderCode)
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

            if (Vk.CreateShaderModule(device, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader!");
            }
        }

        return shaderModule;
    }

    public static (Buffer, DeviceMemory) CreateBuffer(Device device, PhysicalDevice physicalDevice, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (Vk.CreateBuffer(device, in bufferInfo, null, out var newBuffer) != Result.Success)
        {
            throw new Exception("Failed to create buffer!");
        }

        MemoryRequirements memoryRequirements;
        Vk.GetBufferMemoryRequirements(device, newBuffer, out memoryRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(physicalDevice, memoryRequirements.MemoryTypeBits, properties)
        };

        if (Vk.AllocateMemory(device, in allocInfo, null, out var newBufferMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate buffer memory!");
        }

        Vk.BindBufferMemory(device, newBuffer, newBufferMemory, 0);

        return (newBuffer, newBufferMemory);
    }

    public static uint FindMemoryType(PhysicalDevice physicalDevice, uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        Vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
                return (uint) i;
        }

        throw new Exception("Unable to find suitable memory type!");
    }

    public static (Buffer[], DeviceMemory[]) CreateUniformBuffers(Device device, PhysicalDevice physicalDevice, ulong bufferSize, uint bufferCount)
    {
        var uniformBuffers = new Buffer[bufferCount];
        var uniformBuffersMemory = new DeviceMemory[bufferCount];

        for (int i = 0; i < bufferCount; i++)
        {
            (uniformBuffers[i], uniformBuffersMemory[i]) = CreateBuffer(device, physicalDevice, bufferSize, BufferUsageFlags.UniformBufferBit,
                         MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        return (uniformBuffers, uniformBuffersMemory);
    }

    public static CommandPool CreateCommandPool(Device device, QueueFamilyIndices queueFamilyIndices)
    {
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamilyIndices.GraphicsFamily!.Value
        };

        if (Vk.CreateCommandPool(device, in poolInfo, null, out var commandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool!");
        }

        return commandPool;
    }

    public static Sampler CreateTextureSampler(Device device, PhysicalDevice physicalDevice)
    {
        PhysicalDeviceProperties properties;
        Vk.GetPhysicalDeviceProperties(physicalDevice, out properties);

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

        if (Vk.CreateSampler(device, in samplerInfo, null, out var textureSampler) != Result.Success)
        {
            throw new Exception("Failed to create texture sampler!");
        }

        return textureSampler;
    }

    public static Semaphore CreateSemaphore(Device device)
    {
        SemaphoreCreateInfo createInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        if (Vk.CreateSemaphore(device, in createInfo, null, out var semaphore) != Result.Success)
        {
            throw new Exception("Failed to create semaphore!");
        }

        return semaphore;
    }

    public static Fence CreateFence(Device device, bool isSignaled)
    {
        FenceCreateInfo createInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = isSignaled ? FenceCreateFlags.SignaledBit : FenceCreateFlags.None
        };

        if (Vk.CreateFence(device, in createInfo, null, out var fence) != Result.Success)
        {
            throw new Exception("Failed to create fence!");
        }

        return fence;
    }

    public static (Image, DeviceMemory) CreateImage(Device device, PhysicalDevice physicalDevice, 
            uint width, uint height, Format format, ImageTiling tiling,
            ImageUsageFlags usage, MemoryPropertyFlags properties)
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

        if (Vk.CreateImage(device, in imageInfo, null, out var image) != Result.Success)
        {
            throw new Exception("Failed to create textue image!");
        }

        MemoryRequirements memRequirements;
        Vk.GetImageMemoryRequirements(device, image, out memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = VulkanHelper.FindMemoryType(physicalDevice, memRequirements.MemoryTypeBits, properties)
        };

        if (Vk.AllocateMemory(device, in allocInfo, null, out var imageMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate texture image memory!");
        }

        Vk.BindImageMemory(device, image, imageMemory, 0);

        return (image, imageMemory);
    }

    public static ImageView CreateImageView(Device device, Image image, Format format, ImageAspectFlags aspectFlags)
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

        if (Vk.CreateImageView(device, in viewInfo, null, out var imageView) != Result.Success)
        {
            throw new Exception("Failed to create image view!");
        }
        return imageView;
    }

    public static Format FindDepthFormat(PhysicalDevice physicalDevice)
    {
        var candidates = new Format[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint };
        return FindSupportedFormat(physicalDevice, candidates, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
    }

    private static Format FindSupportedFormat(PhysicalDevice physicalDevice, Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
    {
        foreach (var format in candidates)
        {
            Vk.GetPhysicalDeviceFormatProperties(physicalDevice, format, out FormatProperties props);
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
}
