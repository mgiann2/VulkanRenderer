using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Renderer;

public readonly struct IndexBuffer
{
    public Buffer Buffer { get; init; }
    public DeviceMemory BufferMemory { get; init; }
    public uint IndexCount { get; init; }

    public IndexBuffer(Buffer buffer, DeviceMemory bufferMemory, uint indexCount)
    {
        Buffer = buffer;
        BufferMemory = bufferMemory;
        IndexCount = indexCount;
    }
}

unsafe public partial class VulkanRenderer
{
    public IndexBuffer CreateIndexBuffer(ushort[] indices)
    {
        ulong bufferSize = (ulong) (Unsafe.SizeOf<ushort>() * indices.Length);
        
        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(Device, PhysicalDevice, bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(Device, stagingBufferMemory, 0, bufferSize, 0, &data);
        indices.AsSpan().CopyTo(new Span<ushort>(data, indices.Length));
        vk.UnmapMemory(Device, stagingBufferMemory);

        Buffer indexBuffer;
        DeviceMemory indexBufferMemory;
        (indexBuffer, indexBufferMemory) = VulkanHelper.CreateBuffer(Device, PhysicalDevice, bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit);

        CopyBuffer(stagingBuffer, indexBuffer, bufferSize);

        vk.DestroyBuffer(Device, stagingBuffer, null);
        vk.FreeMemory(Device, stagingBufferMemory, null);

        return new IndexBuffer(indexBuffer, indexBufferMemory, (uint) indices.Length);
    }

    public void Bind(IndexBuffer indexBuffer, CommandBuffer commandBuffer)
    {
        vk.CmdBindIndexBuffer(commandBuffer, indexBuffer.Buffer, 0, IndexType.Uint16);
    }

    public void DestroyBuffer(IndexBuffer indexBuffer)
    {
        vk.DestroyBuffer(Device, indexBuffer.Buffer, null);
        vk.FreeMemory(Device, indexBuffer.BufferMemory, null);
    }
}
