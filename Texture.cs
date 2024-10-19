using Silk.NET.Vulkan;

public class Texture
{
    VulkanRenderer renderer;

    Image image;
    DeviceMemory imageMemory;
    ImageView imageView;
    DescriptorSet[] descriptorSets;

    bool isFreed = false;

    public Texture(VulkanRenderer renderer, string filepath)
    {
        this.renderer = renderer;

        // create image
        using (FileStream stream = File.OpenRead(filepath))
        using (MemoryStream memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            (image, imageMemory) = renderer.CreateTextureImage(memoryStream);
        }
        imageView = renderer.CreateTextureImageView(image);

        // create descriptor set
        descriptorSets = renderer.CreateTextureImageDescriptorSets(imageView);
    }

    public void Use()
    {
        if (isFreed) throw new Exception("Texture has been freed!");
        renderer.BindTextureDescriptorSet(descriptorSets);
    }

    public void FreeMemory()
    {

    }
}
