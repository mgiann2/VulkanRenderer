using Silk.NET.Vulkan;
using Silk.NET.Maths;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Renderer;

public struct Vertex
{
    public Vector3D<float> pos;
    public Vector2D<float> texCoord;
    public Vector3D<float> normal;
    public Vector3D<float> tangent;

    public static VertexInputBindingDescription GetBindingDescription()
    {
        VertexInputBindingDescription bindingDescription = new()
        {
            Binding = 0,
            Stride = (uint)Unsafe.SizeOf<Vertex>(),
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
                Format = Format.R32G32Sfloat,
                Offset = (uint) Marshal.OffsetOf<Vertex>(nameof(texCoord))
            },

            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 2,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint) Marshal.OffsetOf<Vertex>(nameof(normal))
            },

            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 3,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint) Marshal.OffsetOf<Vertex>(nameof(tangent))
            }
        };

        return attributeDescriptions;
    }
}

unsafe public class VertexBuffer : IDisposable
{
    public Buffer Buffer { get; }
    public DeviceMemory BufferMemory { get; }
    public uint VertexCount { get; }

    private Vk vk = VulkanHelper.Vk;
    private SCDevice scDevice;
    private bool disposedValue;

    public VertexBuffer(SCDevice scDevice, Vertex[] vertices)
    {
        this.scDevice = scDevice;
        VertexCount = (uint) vertices.Length;

        ulong bufferSize = (ulong) (Unsafe.SizeOf<Vertex>() * vertices.Length);
        
        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(scDevice, bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(scDevice.LogicalDevice, stagingBufferMemory, 0, bufferSize, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        vk.UnmapMemory(scDevice.LogicalDevice, stagingBufferMemory);

        (Buffer, BufferMemory) = VulkanHelper.CreateBuffer(scDevice, bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit);

        scDevice.CopyBuffer(stagingBuffer, Buffer, bufferSize);

        vk.DestroyBuffer(scDevice.LogicalDevice, stagingBuffer, null);
        vk.FreeMemory(scDevice.LogicalDevice, stagingBufferMemory, null);
    }

    public void Bind(CommandBuffer commandBuffer)
    {
        Buffer* pBuffers = stackalloc[] { Buffer };
        ulong* pOffsets = stackalloc[] { 0ul };
        vk.CmdBindVertexBuffers(commandBuffer, 0, 1, pBuffers, pOffsets);
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

    ~VertexBuffer()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
