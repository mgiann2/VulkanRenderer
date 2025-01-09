using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Fence = Silk.NET.Vulkan.Fence;

namespace Renderer;

unsafe public static class VulkanHelper
{
    public static readonly Vk Vk = Vk.GetApi();

    public static ShaderModule CreateShaderModule(SCDevice scDevice, byte[] shaderCode)
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

            if (Vk.CreateShaderModule(scDevice.LogicalDevice, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader!");
            }
        }

        return shaderModule;
    }

    public static (Buffer, DeviceMemory) CreateBuffer(SCDevice scDevice, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (Vk.CreateBuffer(scDevice.LogicalDevice, in bufferInfo, null, out var newBuffer) != Result.Success)
        {
            throw new Exception("Failed to create buffer!");
        }

        MemoryRequirements memoryRequirements;
        Vk.GetBufferMemoryRequirements(scDevice.LogicalDevice, newBuffer, out memoryRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(scDevice, memoryRequirements.MemoryTypeBits, properties)
        };

        if (Vk.AllocateMemory(scDevice.LogicalDevice, in allocInfo, null, out var newBufferMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate buffer memory!");
        }

        Vk.BindBufferMemory(scDevice.LogicalDevice, newBuffer, newBufferMemory, 0);

        return (newBuffer, newBufferMemory);
    }

    public static uint FindMemoryType(SCDevice scDevice, uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        Vk.GetPhysicalDeviceMemoryProperties(scDevice.PhysicalDevice, out memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
                return (uint) i;
        }

        throw new Exception("Unable to find suitable memory type!");
    }

    public static (Buffer[], DeviceMemory[]) CreateUniformBuffers(SCDevice scDevice, ulong bufferSize, uint bufferCount)
    {
        var uniformBuffers = new Buffer[bufferCount];
        var uniformBuffersMemory = new DeviceMemory[bufferCount];

        for (int i = 0; i < bufferCount; i++)
        {
            (uniformBuffers[i], uniformBuffersMemory[i]) = CreateBuffer(scDevice, bufferSize, BufferUsageFlags.UniformBufferBit,
                         MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        return (uniformBuffers, uniformBuffersMemory);
    }

    public static CommandPool CreateCommandPool(SCDevice scDevice)
    {
        var queueFamilyIndices = scDevice.QueueFamilyIndices;

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamilyIndices.GraphicsFamily!.Value
        };

        if (Vk.CreateCommandPool(scDevice.LogicalDevice, in poolInfo, null, out var commandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool!");
        }

        return commandPool;
    }

    public static Sampler CreateTextureSampler(SCDevice scDevice)
    {
        PhysicalDeviceProperties properties;
        Vk.GetPhysicalDeviceProperties(scDevice.PhysicalDevice, out properties);

        SamplerCreateInfo samplerInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
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

        if (Vk.CreateSampler(scDevice.LogicalDevice, in samplerInfo, null, out var textureSampler) != Result.Success)
        {
            throw new Exception("Failed to create texture sampler!");
        }

        return textureSampler;
    }

    public static Semaphore CreateSemaphore(SCDevice scDevice)
    {
        SemaphoreCreateInfo createInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        if (Vk.CreateSemaphore(scDevice.LogicalDevice, in createInfo, null, out var semaphore) != Result.Success)
        {
            throw new Exception("Failed to create semaphore!");
        }

        return semaphore;
    }

    public static Fence CreateFence(SCDevice scDevice, bool isSignaled)
    {
        FenceCreateInfo createInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = isSignaled ? FenceCreateFlags.SignaledBit : FenceCreateFlags.None
        };

        if (Vk.CreateFence(scDevice.LogicalDevice, in createInfo, null, out var fence) != Result.Success)
        {
            throw new Exception("Failed to create fence!");
        }

        return fence;
    }

    public static (Image, DeviceMemory) CreateImage(SCDevice scDevice, 
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

        if (Vk.CreateImage(scDevice.LogicalDevice, in imageInfo, null, out var image) != Result.Success)
        {
            throw new Exception("Failed to create textue image!");
        }

        MemoryRequirements memRequirements;
        Vk.GetImageMemoryRequirements(scDevice.LogicalDevice, image, out memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = VulkanHelper.FindMemoryType(scDevice, memRequirements.MemoryTypeBits, properties)
        };

        if (Vk.AllocateMemory(scDevice.LogicalDevice, in allocInfo, null, out var imageMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate texture image memory!");
        }

        Vk.BindImageMemory(scDevice.LogicalDevice, image, imageMemory, 0);

        return (image, imageMemory);
    }

    public static ImageView CreateImageView(SCDevice scDevice, Image image, Format format, ImageAspectFlags aspectFlags)
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

        if (Vk.CreateImageView(scDevice.LogicalDevice, in viewInfo, null, out var imageView) != Result.Success)
        {
            throw new Exception("Failed to create image view!");
        }
        return imageView;
    }

    public static (Image, DeviceMemory) CreateCubemapImage(SCDevice scDevice, 
            uint width, uint height, Format format, ImageTiling tiling,
            ImageUsageFlags usage, MemoryPropertyFlags properties)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new() { Width = width, Height = height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 6,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit,
            Flags = ImageCreateFlags.CreateCubeCompatibleBit
        };

        if (Vk.CreateImage(scDevice.LogicalDevice, in imageInfo, null, out var image) != Result.Success)
        {
            throw new Exception("Failed to create textue image!");
        }

        MemoryRequirements memRequirements;
        Vk.GetImageMemoryRequirements(scDevice.LogicalDevice, image, out memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = VulkanHelper.FindMemoryType(scDevice, memRequirements.MemoryTypeBits, properties)
        };

        if (Vk.AllocateMemory(scDevice.LogicalDevice, in allocInfo, null, out var imageMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate texture image memory!");
        }

        Vk.BindImageMemory(scDevice.LogicalDevice, image, imageMemory, 0);

        return (image, imageMemory);
    }

    public static ImageView CreateCubemapImageView(SCDevice scDevice, Image image, Format format)
    {
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            Format = format,
            ViewType = ImageViewType.TypeCube,
            SubresourceRange = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 6
            }
        };

        if (Vk.CreateImageView(scDevice.LogicalDevice, in viewInfo, null, out var imageView) != Result.Success)
        {
            throw new Exception("Failed to create image view!");
        }
        return imageView;
    }

    public static Format FindDepthFormat(SCDevice scDevice)
    {
        var candidates = new Format[] { Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint };
        return FindSupportedFormat(scDevice, candidates, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);
    }

    private static Format FindSupportedFormat(SCDevice scDevice, Format[] candidates, ImageTiling tiling, FormatFeatureFlags features)
    {
        foreach (var format in candidates)
        {
            Vk.GetPhysicalDeviceFormatProperties(scDevice.PhysicalDevice, format, out FormatProperties props);
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
