using Silk.NET.Vulkan;
using StbiSharp;
using Buffer = Silk.NET.Vulkan.Buffer;

public struct Texture
{
    public Image TextureImage { get; init; }
    public DeviceMemory TextureImageMemory { get; init; }
    public ImageView TextureImageView { get; init; }
    public DescriptorSet[] DescriptorSets { get; init; }

    public Texture(Image textureImage, DeviceMemory textureImageMemory, ImageView textureImageView, DescriptorSet[] descriptorSets)
    {
        TextureImage = textureImage;
        TextureImageMemory = textureImageMemory;
        TextureImageView = textureImageView;
        DescriptorSets = descriptorSets;
    }
}

unsafe public partial class VulkanRenderer
{
    public Texture CreateTexture(string filepath)
    {
        Image image;
        DeviceMemory imageMemory;
        ImageView imageView;
        DescriptorSet[] descriptorSets;

        // create image
        using (var stream = File.OpenRead(filepath))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            (image, imageMemory) = CreateTextureImage(memoryStream);
        }

        imageView = CreateTextureImageView(image);
        descriptorSets = CreateTextureImageDescriptorSets(imageView);

        return new Texture(image, imageMemory, imageView, descriptorSets);
    }

    public void BindTexture(Texture texture)
    {
        vk.CmdBindDescriptorSets(commandBuffers[currentFrame], PipelineBindPoint.Graphics,
                                 pipelineLayout, 1, 1, in texture.DescriptorSets[currentFrame], 0, default);
    }

    public void DestroyTexture(Texture texture)
    {
        fixed (DescriptorSet* descriptorSetsPtr = texture.DescriptorSets)
        {
            vk.FreeDescriptorSets(device, samplerDescriptorPool, 
                    (uint) texture.DescriptorSets.Length, descriptorSetsPtr);
        }

        vk.DestroyImageView(device, texture.TextureImageView, null);
        vk.DestroyImage(device, texture.TextureImage, null);
        vk.FreeMemory(device, texture.TextureImageMemory, null);
    }

    (Image, DeviceMemory) CreateTextureImage(MemoryStream memoryStream)
    {
        var image = Stbi.LoadFromMemory(memoryStream, 4);

        ulong imageSize = (ulong)(image.Width * image.Height * 4);

        Buffer stagingBuffer;
        DeviceMemory stagingBufferMemory;
        CreateBuffer(imageSize, BufferUsageFlags.TransferSrcBit,
                     MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                     out stagingBuffer, out stagingBufferMemory);

        void* data;
        vk.MapMemory(device, stagingBufferMemory, 0, imageSize, 0, &data);
        image.Data.CopyTo(new Span<byte>(data, (int)imageSize));
        vk.UnmapMemory(device, stagingBufferMemory);

        CreateImage((uint)image.Width, (uint)image.Height, Format.R8G8B8A8Srgb, ImageTiling.Optimal,
                     ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit, MemoryPropertyFlags.DeviceLocalBit,
                     out var textureImage, out var textureImageMemory);

        TransitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        CopyBufferToImage(stagingBuffer, textureImage, (uint)image.Width, (uint)image.Height);
        TransitionImageLayout(textureImage, Format.R8G8B8A8Srgb, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        vk.DestroyBuffer(device, stagingBuffer, null);
        vk.FreeMemory(device, stagingBufferMemory, null);

        return (textureImage, textureImageMemory);
    }

    ImageView CreateTextureImageView(Image textureImage)
    {
        return CreateImageView(textureImage, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit);
    }

    DescriptorSet[] CreateTextureImageDescriptorSets(ImageView imageView)
    {
        var imageDescriptorSets = new DescriptorSet[MaxFramesInFlight];

        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, samplerDescriptorSetLayout);

        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = samplerDescriptorPool,
                DescriptorSetCount = (uint) MaxFramesInFlight,
                PSetLayouts = layoutsPtr
            };

            fixed (DescriptorSet* descriptorSetsPtr = imageDescriptorSets)
            {
                var res = vk.AllocateDescriptorSets(device, in allocInfo, descriptorSetsPtr); 
                if (res != Result.Success)
                {
                    Console.WriteLine(res);
                    throw new Exception("Failed to allocate image descriptor sets!");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo imageInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = imageView,
                Sampler = textureSampler
            };

            WriteDescriptorSet descriptorWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = imageDescriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfo
            };

            vk.UpdateDescriptorSets(device, 1, in descriptorWrite, 0, default);
        }

        return imageDescriptorSets;
    }
}
