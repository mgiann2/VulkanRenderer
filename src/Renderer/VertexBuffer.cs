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

public readonly struct VertexBuffer
{
    public Buffer Buffer { get; init; }
    public DeviceMemory BufferMemory { get; init; }
    public uint VertexCount { get; init; }

    public VertexBuffer(Buffer buffer, DeviceMemory bufferMemory, uint vertexCount)
    {
        Buffer = buffer;
        BufferMemory = bufferMemory;
        VertexCount = vertexCount;
    }
}

unsafe public partial class VulkanRenderer
{
    public VertexBuffer CreateVertexBuffer(Vertex[] vertices)
    {
        ulong bufferSize = (ulong) (Unsafe.SizeOf<Vertex>() * vertices.Length);
        
        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        (stagingBuffer, stagingBufferMemory) = VulkanHelper.CreateBuffer(SCDevice, bufferSize, BufferUsageFlags.TransferSrcBit, 
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        void* data;
        vk.MapMemory(SCDevice.LogicalDevice, stagingBufferMemory, 0, bufferSize, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        vk.UnmapMemory(SCDevice.LogicalDevice, stagingBufferMemory);

        Buffer vertexBuffer;
        DeviceMemory vertexBufferMemory;
        (vertexBuffer, vertexBufferMemory) = VulkanHelper.CreateBuffer(SCDevice, bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit);

        CopyBuffer(stagingBuffer, vertexBuffer, bufferSize);

        vk.DestroyBuffer(SCDevice.LogicalDevice, stagingBuffer, null);
        vk.FreeMemory(SCDevice.LogicalDevice, stagingBufferMemory, null);

        return new VertexBuffer(vertexBuffer, vertexBufferMemory, (uint) vertices.Length);
    }

    public void Bind(VertexBuffer vertexBuffer, CommandBuffer commandBuffer)
    {
        Buffer[] vertexBuffers = new[]{ vertexBuffer.Buffer };
        ulong[] offsets = new ulong[]{ 0 };

        fixed (Buffer* vertexBufferPtr = vertexBuffers)
        fixed (ulong* offsetsPtr = offsets)
        {
            vk.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBufferPtr, offsetsPtr);
        }
    }

    public void DestroyBuffer(VertexBuffer vertexBuffer)
    {
        vk.DestroyBuffer(SCDevice.LogicalDevice, vertexBuffer.Buffer, null);
        vk.FreeMemory(SCDevice.LogicalDevice, vertexBuffer.BufferMemory, null);
    }
}
