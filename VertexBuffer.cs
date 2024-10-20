using Silk.NET.Vulkan;
using Silk.NET.Maths;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public struct Vertex
{
    public Vector3D<float> pos;
    public Vector3D<float> color;
    public Vector2D<float> texCoord;

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

public readonly struct VertexBuffer
{
    public VertexBuffer(Buffer buffer, DeviceMemory bufferMemory, uint vertexCount)
    {
        Buffer = buffer;
        BufferMemory = bufferMemory;
        VertexCount = vertexCount;
    }

    public Buffer Buffer { get; init; }
    public DeviceMemory BufferMemory { get; init; }
    public uint VertexCount { get; init; }
}

unsafe public partial class VulkanRenderer
{
    public VertexBuffer CreateVertexBuffer(Vertex[] vertices)
    {
        ulong bufferSize = (ulong) (Unsafe.SizeOf<Vertex>() * vertices.Length);
        
        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                     out stagingBuffer, out stagingBufferMemory);

        void* data;
        vk.MapMemory(device, stagingBufferMemory, 0, bufferSize, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        vk.UnmapMemory(device, stagingBufferMemory);

        Buffer vertexBuffer;
        DeviceMemory vertexBufferMemory;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit, out vertexBuffer, out vertexBufferMemory);

        CopyBuffer(stagingBuffer, vertexBuffer, bufferSize);

        vk.DestroyBuffer(device, stagingBuffer, null);
        vk.FreeMemory(device, stagingBufferMemory, null);

        return new VertexBuffer(vertexBuffer, vertexBufferMemory, (uint) vertices.Length);
    }

    public void Bind(VertexBuffer vertexBuffer)
    {
        Buffer[] vertexBuffers = new[]{ vertexBuffer.Buffer };
        ulong[] offsets = new ulong[]{ 0 };

        fixed (Buffer* vertexBufferPtr = vertexBuffers)
        fixed(ulong* offsetsPtr = offsets)
        {
            vk.CmdBindVertexBuffers(commandBuffers[currentFrame], 0, 1, vertexBufferPtr, offsetsPtr);
        }
    }

    public void DestroyBuffer(VertexBuffer vertexBuffer)
    {
        vk.DestroyBuffer(device, vertexBuffer.Buffer, null);
        vk.FreeMemory(device, vertexBuffer.BufferMemory, null);
    }
}
