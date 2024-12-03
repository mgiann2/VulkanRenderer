using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

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

    public static (Buffer[], DeviceMemory[]) CreateUniformBuffers(Device device, PhysicalDevice physicalDevice, ulong bufferSize, uint maxFramesInFlight)
    {
        var uniformBuffers = new Buffer[maxFramesInFlight];
        var uniformBuffersMemory = new DeviceMemory[maxFramesInFlight];

        for (int i = 0; i < maxFramesInFlight; i++)
        {
            (uniformBuffers[i], uniformBuffersMemory[i]) = CreateBuffer(device, physicalDevice, bufferSize, BufferUsageFlags.UniformBufferBit,
                         MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        return (uniformBuffers, uniformBuffersMemory);
    }
}
