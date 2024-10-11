using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

struct Vertex
{
    public Vector2D<float> pos;
    public Vector3D<float> color;

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
        };

        return attributeDescriptions;
    }
}

unsafe abstract class AbstractBuffer : IDisposable
{
    protected readonly VulkanRenderer renderer;
    protected Buffer buffer;
    protected DeviceMemory bufferMemory;

    private bool disposedValue;

    public AbstractBuffer(VulkanRenderer renderer)
    {
        this.renderer = renderer;
    }

    public abstract void Bind();

    protected void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer newBuffer, out DeviceMemory newBufferMemory)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (renderer.Vk.CreateBuffer(renderer.Device, in bufferInfo, null, out newBuffer) != Result.Success)
        {
            throw new Exception("Failed to create buffer!");
        }

        MemoryRequirements memoryRequirements;
        renderer.Vk.GetBufferMemoryRequirements(renderer.Device, newBuffer, out memoryRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, properties)
        };

        if (renderer.Vk.AllocateMemory(renderer.Device, in allocInfo, null, out newBufferMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate buffer memory!");
        }

        renderer.Vk.BindBufferMemory(renderer.Device, newBuffer, newBufferMemory, 0);
    }

    protected void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = renderer.CommandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        renderer.Vk.AllocateCommandBuffers(renderer.Device, in allocInfo, out commandBuffer);

        CommandBufferBeginInfo beginInfo = new() { SType = StructureType.CommandBufferBeginInfo };

        BufferCopy copyRegion = new() { Size = size };

        renderer.Vk.BeginCommandBuffer(commandBuffer, in beginInfo);
        renderer.Vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, in copyRegion);
        renderer.Vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        renderer.Vk.QueueSubmit(renderer.GraphicsQueue, 1, in submitInfo, default);
        renderer.Vk.DeviceWaitIdle(renderer.Device);

        renderer.Vk.FreeCommandBuffers(renderer.Device, renderer.CommandPool, 1, in commandBuffer);
    }

    protected uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        renderer.Vk.GetPhysicalDeviceMemoryProperties(renderer.PhysicalDevice, out memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
                return (uint) i;
        }

        throw new Exception("Unable to find suitable memory type!");
    }

    ~AbstractBuffer()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }   

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // free unmanaged resources (unmanaged objects) and override finalizer
            renderer.Vk.DestroyBuffer(renderer.Device, buffer, null);
            renderer.Vk.FreeMemory(renderer.Device, bufferMemory, null);

            disposedValue = true;
        }
    }
}

unsafe class VertexBuffer : AbstractBuffer
{
    public uint VertexCount { get; private set; }

    public VertexBuffer(VulkanRenderer renderer, Vertex[] vertices) : base(renderer)
    {
        VertexCount = (uint) vertices.Length;
        ulong bufferSize = (ulong) (Unsafe.SizeOf<Vertex>() * vertices.Length);

        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit,
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                     out stagingBuffer, out stagingBufferMemory);

        // fill staging buffer
        void* data;
        renderer.Vk.MapMemory(renderer.Device, stagingBufferMemory, 0, bufferSize, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        renderer.Vk.UnmapMemory(renderer.Device, stagingBufferMemory);

        CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit, out buffer, out bufferMemory);

        // copy staging buffer to vertex buffer on device
        CopyBuffer(stagingBuffer, buffer, bufferSize);

        renderer.Vk.DestroyBuffer(renderer.Device, stagingBuffer, null);
        renderer.Vk.FreeMemory(renderer.Device, stagingBufferMemory, null);
    }

    public override void Bind()
    {
        Buffer[] vertexBuffers = new[]{ buffer };
        ulong[] offsets = new ulong[] { 0 };

        fixed (Buffer* vertexBuffersPtr = vertexBuffers)
        fixed (ulong* offsetsPtr = offsets)
        {
            renderer.Vk.CmdBindVertexBuffers(renderer.CurrentCommandBuffer, 0, 1, vertexBuffersPtr, offsetsPtr);
        }
    }
}

unsafe class IndexBuffer : AbstractBuffer
{
    public uint IndexCount { get; private set; }

    public IndexBuffer(VulkanRenderer renderer, ushort[] indices) : base(renderer)
    {
        IndexCount = (uint) indices.Length;
        ulong bufferSize = (ulong) (Unsafe.SizeOf<ushort>() * indices.Length);

        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        CreateBuffer(bufferSize, BufferUsageFlags.TransferSrcBit,
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                     out stagingBuffer, out stagingBufferMemory);

        // fill staging buffer
        void* data;
        renderer.Vk.MapMemory(renderer.Device, stagingBufferMemory, 0, bufferSize, 0, &data);
        indices.AsSpan().CopyTo(new Span<ushort>(data, indices.Length));
        renderer.Vk.UnmapMemory(renderer.Device, stagingBufferMemory);

        CreateBuffer(bufferSize, BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndexBufferBit,
                     MemoryPropertyFlags.DeviceLocalBit, out buffer, out bufferMemory);

        // copy staging buffer to index buffer on device
        CopyBuffer(stagingBuffer, buffer, bufferSize);

        renderer.Vk.DestroyBuffer(renderer.Device, stagingBuffer, null);
        renderer.Vk.FreeMemory(renderer.Device, stagingBufferMemory, null);
    }

    public override void Bind()
    {
        renderer.Vk.CmdBindIndexBuffer(renderer.CurrentCommandBuffer, buffer, 0, IndexType.Uint16);
    }
}
