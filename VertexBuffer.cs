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

unsafe class VertexBuffer : IDisposable
{
    public ulong Size;

    private readonly Vk vk;
    private readonly Device device;
    private readonly PhysicalDevice physicalDevice;
    private Buffer buffer;
    private DeviceMemory bufferMemory;

    private bool disposedValue;

    public VertexBuffer(VulkanRenderer renderer, Vertex[] vertices)
    {
        vk = renderer.Vk;
        device = renderer.Device;
        physicalDevice = renderer.PhysicalDevice;
        Size = (ulong) (Unsafe.SizeOf<Vertex>() * vertices.Length);

        // create buffer
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = Size,
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, in bufferInfo, null, out buffer) != Result.Success)
        {
            throw new Exception("Unable to create vertex buffer!");
        }

        // allocate memory
        MemoryRequirements memRequirements;
        vk.GetBufferMemoryRequirements(device, buffer, out memRequirements);
        
        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };

        if (vk.AllocateMemory(device, in allocInfo, null, out bufferMemory) != Result.Success)
        {
            throw new Exception("Failed to allocate memory for vertex buffer!");
        }

        vk.BindBufferMemory(device, buffer, bufferMemory, 0);

        // fill vertex buffer
        void* data;
        vk.MapMemory(device, bufferMemory, 0, bufferInfo.Size, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        vk.UnmapMemory(device, bufferMemory);
    }

    public void Bind(CommandBuffer commandBuffer)
    {
        Buffer[] vertexBuffers = new[]{ buffer };
        ulong[] offsets = new ulong[] { 0 };

        fixed (Buffer* vertexBuffersPtr = vertexBuffers)
        fixed (ulong* offsetsPtr = offsets)
        {
            vk.CmdBindVertexBuffers(commandBuffer, 0, 1, vertexBuffersPtr, offsetsPtr);
        }
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
                return (uint) i;
        }

        throw new Exception("Unable to find suitable memory type!");
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~VertexBuffer()
    {
       Dispose(disposing: false);
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
            vk.DestroyBuffer(device, buffer, null);
            vk.FreeMemory(device, bufferMemory, null);

            disposedValue = true;
        }
    }
}
