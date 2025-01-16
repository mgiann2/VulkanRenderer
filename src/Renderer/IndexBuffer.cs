using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Renderer;

unsafe public class IndexBuffer : IDisposable
{
    public Buffer Buffer { get; }
    public DeviceMemory BufferMemory { get; }
    public uint IndexCount { get; }

    private Vk vk = VulkanHelper.Vk;
    private SCDevice scDevice;
    private bool disposedValue;

    public IndexBuffer(SCDevice scDevice, ushort[] indices)
    {
        this.scDevice = scDevice;
        IndexCount = (uint) indices.Length;

        ulong bufferSize = (ulong) (Unsafe.SizeOf<ushort>() * indices.Length);
        
        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(scDevice, bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(scDevice.LogicalDevice, stagingBufferMemory, 0, bufferSize, 0, &data);
        indices.AsSpan().CopyTo(new Span<ushort>(data, indices.Length));
        vk.UnmapMemory(scDevice.LogicalDevice, stagingBufferMemory);

        (Buffer, BufferMemory) = VulkanHelper.CreateBuffer(scDevice, bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit);

        scDevice.CopyBuffer(stagingBuffer, Buffer, bufferSize);

        vk.DestroyBuffer(scDevice.LogicalDevice, stagingBuffer, null);
        vk.FreeMemory(scDevice.LogicalDevice, stagingBufferMemory, null);
    }

    public void Bind(CommandBuffer commandBuffer)
    {
        vk.CmdBindIndexBuffer(commandBuffer, Buffer, 0, IndexType.Uint16);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            // free unmanaged resources (unmanaged objects) and override finalizer
            vk.DestroyBuffer(scDevice.LogicalDevice, Buffer, null);
            vk.FreeMemory(scDevice.LogicalDevice, BufferMemory, null);

            disposedValue = true;
        }
    }

    ~IndexBuffer()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
