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
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(SCDevice, bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(SCDevice.LogicalDevice, stagingBufferMemory, 0, bufferSize, 0, &data);
        indices.AsSpan().CopyTo(new Span<ushort>(data, indices.Length));
        vk.UnmapMemory(SCDevice.LogicalDevice, stagingBufferMemory);

        Buffer indexBuffer;
        DeviceMemory indexBufferMemory;
        (indexBuffer, indexBufferMemory) = VulkanHelper.CreateBuffer(SCDevice, bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit);

        SCDevice.CopyBuffer(stagingBuffer, indexBuffer, bufferSize);

        vk.DestroyBuffer(SCDevice.LogicalDevice, stagingBuffer, null);
        vk.FreeMemory(SCDevice.LogicalDevice, stagingBufferMemory, null);

        return new IndexBuffer(indexBuffer, indexBufferMemory, (uint) indices.Length);
    }

    public void Bind(IndexBuffer indexBuffer, CommandBuffer commandBuffer)
    {
        vk.CmdBindIndexBuffer(commandBuffer, indexBuffer.Buffer, 0, IndexType.Uint16);
    }

    public void DestroyBuffer(IndexBuffer indexBuffer)
    {
        vk.DestroyBuffer(SCDevice.LogicalDevice, indexBuffer.Buffer, null);
        vk.FreeMemory(SCDevice.LogicalDevice, indexBuffer.BufferMemory, null);
    }
}
