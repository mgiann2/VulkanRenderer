using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using StbiSharp;
using Buffer = Silk.NET.Vulkan.Buffer;

public struct Texture
{
    public Image TextureImage { get; init; }
    public DeviceMemory TextureImageMemory { get; init; }
    public ImageView TextureImageView { get; init; }
}

public struct Material
{
    public Texture Albedo { get; init; }
    public Texture Normal { get; init; }
    public Texture Metalness { get; init; }
    public DescriptorSet[] DescriptorSets { get; init; }
}

unsafe public partial class VulkanRenderer
{
    public Texture CreateTexture(string filepath)
    {
        Image image;
        DeviceMemory imageMemory;
        ImageView imageView;

        // create image
        using (var stream = File.OpenRead(filepath))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            (image, imageMemory) = CreateTextureImage(memoryStream);
        }

        imageView = CreateTextureImageView(image);

        return new Texture 
        {
            TextureImage = image,
            TextureImageMemory = imageMemory,
            TextureImageView = imageView
        };
    }

    public Material CreateMaterial(string albedoPath, string normalPath, string metalnessPath)
    {
        var albedoTexture = CreateTexture(albedoPath);
        var normalTexture = CreateTexture(normalPath);
        var metalnessTexture = CreateTexture(metalnessPath);

        var descriptorSets = CreateGBufferDescriptorSets(albedoTexture.TextureImageView,
                                                         normalTexture.TextureImageView,
                                                         metalnessTexture.TextureImageView);

        return new Material
        {
            Albedo = albedoTexture,
            Normal = normalTexture,
            Metalness = metalnessTexture,
            DescriptorSets = descriptorSets
        };
    }

    public void BindMaterial(Material material)
    {
        vk.CmdBindDescriptorSets(geometryCommandBuffers[currentFrame], PipelineBindPoint.Graphics,
                                 geometryPipeline.Layout, 0, 1, in material.DescriptorSets[currentFrame], 0, default);
    }

    public void DestroyTexture(Texture texture)
    {
        vk.DestroyImageView(device, texture.TextureImageView, null);
        vk.DestroyImage(device, texture.TextureImage, null);
        vk.FreeMemory(device, texture.TextureImageMemory, null);
    }

    public void DestroyMaterial(Material material)
    {
        fixed (DescriptorSet* descriptorSetsPtr = material.DescriptorSets)
        {
            vk.FreeDescriptorSets(device, gBufferDescriptorPool,
                    (uint) material.DescriptorSets.Length, descriptorSetsPtr);
        }

        DestroyTexture(material.Albedo);
        DestroyTexture(material.Normal);
        DestroyTexture(material.Metalness);
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
}
