using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

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
        CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                     out stagingBuffer, out stagingBufferMemory);

        void* data;
        vk.MapMemory(device, stagingBufferMemory, 0, bufferSize, 0, &data);
        indices.AsSpan().CopyTo(new Span<ushort>(data, indices.Length));
        vk.UnmapMemory(device, stagingBufferMemory);

        Buffer indexBuffer;
        DeviceMemory indexBufferMemory;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit, out indexBuffer, out indexBufferMemory);

        CopyBuffer(stagingBuffer, indexBuffer, bufferSize);

        vk.DestroyBuffer(device, stagingBuffer, null);
        vk.FreeMemory(device, stagingBufferMemory, null);

        return new IndexBuffer(indexBuffer, indexBufferMemory, (uint) indices.Length);
    }

    public void Bind(IndexBuffer indexBuffer, CommandBuffer commandBuffer)
    {
        vk.CmdBindIndexBuffer(commandBuffer, indexBuffer.Buffer, 0, IndexType.Uint16);
    }

    public void DestroyBuffer(IndexBuffer indexBuffer)
    {
        vk.DestroyBuffer(device, indexBuffer.Buffer, null);
        vk.FreeMemory(device, indexBuffer.BufferMemory, null);
    }
}
