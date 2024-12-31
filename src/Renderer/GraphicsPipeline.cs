using System.Runtime.CompilerServices;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace Renderer;

public struct GraphicsPipeline
{
    public Pipeline Pipeline { get; init; }
    public PipelineLayout Layout { get; init; }
}

unsafe public partial class VulkanRenderer
{
    const string GeometryVertexShaderFilename = "gpass.vert.spv";
    const string GeometryFragmentShaderFilename = "gpass.frag.spv";

    const string CompositionVertexShaderFilename = "composition.vert.spv";
    const string CompositionFragmentShaderFilename = "composition.frag.spv";

    const string LightingVertexShaderFilename = "light.vert.spv";
    const string LightingFragmentShaderFilename = "light.frag.spv";

    const string SolidColorVertexShaderFilename = "solid_color.vert.spv";
    const string SolidColorFragmentShaderFilename = "solid_color.frag.spv";

    const string SkyboxVertexShaderFilename = "cubemap.vert.spv";
    const string SkyboxFragmentShaderFilename = "equirectangular_to_cubemap.frag.spv";

    const string BloomVertexShaderFilename = "bloom_blur.vert.spv";
    const string BloomFragmentShaderFilename = "bloom_blur.frag.spv";
    
    const string PostProcessVertexShaderFilename = "postprocess.vert.spv";
    const string PostProcessFragmentShaderFilename = "postprocess.frag.spv";

    const uint GeometryPassColorAttachmentCount = 4;
    const uint CompositionPassColorAttachmentCount = 2;
    const uint BloomPassColorAttachmentCount = 1;
    const uint PostProcessColorAttachmentCount = 1;

    GraphicsPipeline CreateGeometryPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + GeometryVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + GeometryFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(GeometryPassColorAttachmentCount)
                       .SetDepthStencilInfo(true, true, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(materialInfoDescriptorSetLayout)
                       .AddPushConstantRange((uint) Unsafe.SizeOf<Matrix4X4<float>>(), 0, ShaderStageFlags.VertexBit);

        return pipelineBuilder.Build(renderPass, 0);
    }

    GraphicsPipeline CreateCompositionPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + CompositionVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + CompositionFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(CompositionPassColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(screenTextureDescriptorSetLayout);

        return pipelineBuilder.Build(renderPass, 0);
    }

    GraphicsPipeline CreateLightingPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + LightingVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + LightingFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.FrontBit, FrontFace.CounterClockwise)
                       .SetColorBlendingAdditive(CompositionPassColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(screenTextureDescriptorSetLayout)
                       .AddPushConstantRange((uint) Unsafe.SizeOf<LightInfo>(), 0, ShaderStageFlags.VertexBit);

        return pipelineBuilder.Build(renderPass, 0);
    }

    GraphicsPipeline CreateSolidColorPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + SolidColorVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + SolidColorFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(CompositionPassColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddPushConstantRange((uint) Unsafe.SizeOf<SolidColorObjectInfo>(), 0, ShaderStageFlags.VertexBit);

        return pipelineBuilder.Build(renderPass, 0);

    }

    GraphicsPipeline CreateSkyboxPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + SkyboxVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + SkyboxFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.FrontBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(CompositionPassColorAttachmentCount)
                       .SetDepthStencilInfo(true, true, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(singleTextureDescriptorSetLayout);

        return pipelineBuilder.Build(renderPass, 0);
    }

    GraphicsPipeline CreateBloomPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + BloomVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + BloomFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(BloomPassColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(singleTextureDescriptorSetLayout)
                       .AddPushConstantRange(4, 0, ShaderStageFlags.FragmentBit);

        return pipelineBuilder.Build(renderPass, 0);
    }

    GraphicsPipeline CreatePostProcessPipeline(RenderPass renderPass)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + PostProcessVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + PostProcessFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(Device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(PostProcessColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(singleTextureDescriptorSetLayout)
                       .AddDescriptorSetLayout(singleTextureDescriptorSetLayout);

        return pipelineBuilder.Build(renderPass, 0);
    }

    // Scene Info Descriptor Set
    // -------------------------

    DescriptorSetLayout CreateSceneInfoDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding uboBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.VertexBit
        };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &uboBinding,
        };

        if (vk.CreateDescriptorSetLayout(Device, in layoutInfo, null, out var layout) != Result.Success)
        {
            throw new Exception("Failed to create descriptor set layout!");
        }

        return layout;
    }

    DescriptorPool CreateSceneInfoDescriptorPool()
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = (uint) MaxFramesInFlight
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = (uint) MaxFramesInFlight,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
        };

        if (vk.CreateDescriptorPool(Device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorSet[] CreateSceneInfoDescriptorSets()
    {
        var descriptorSets = new DescriptorSet[MaxFramesInFlight];

        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, sceneInfoDescriptorSetLayout);
        
        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                PSetLayouts = layoutsPtr,
                DescriptorSetCount = (uint) layouts.Length,
                DescriptorPool = sceneInfoDescriptorPool
            };
            
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            {
                if (vk.AllocateDescriptorSets(Device, in allocInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate descriptor sets!");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorBufferInfo bufferInfo = new()
            {
                Buffer = sceneInfoBuffers[i],
                Offset = 0,
                Range = (ulong) Unsafe.SizeOf<SceneInfo>()
            };

            WriteDescriptorSet descriptorWrite = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };

            vk.UpdateDescriptorSets(Device, 1, ref descriptorWrite, 0, default);
        }

        return descriptorSets;
    }

    // Material Info Descriptor Set
    // ----------------------------
    
    DescriptorSetLayout CreateMaterialInfoDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding albedoSamplerBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding normalSamplerBinding = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding aoRoughnessMetalnessSamplerBinding = new()
        {
            Binding = 2,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = new DescriptorSetLayoutBinding[] { albedoSamplerBinding, normalSamplerBinding, aoRoughnessMetalnessSamplerBinding };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint) bindings.Length
        };
        fixed (DescriptorSetLayoutBinding* bindingsPtr = bindings)
            layoutInfo.PBindings = bindingsPtr;

        if (vk.CreateDescriptorSetLayout(Device, in layoutInfo, null, out var layout) != Result.Success)
        {
            throw new Exception("Failed to create descriptor set layout!");
        }

        return layout;
    }

    DescriptorPool CreateMaterialInfoDescriptorPool(uint maxSets)
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint) MaxFramesInFlight * 3 * maxSets
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = (uint) MaxFramesInFlight * maxSets,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
        };

        if (vk.CreateDescriptorPool(Device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    public DescriptorSet[] CreateMaterialInfoDescriptorSets(ImageView albedoView, ImageView normalView, ImageView aoRoughnessMetalnessView)
    {
        var descriptorSets = new DescriptorSet[MaxFramesInFlight];

        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, materialInfoDescriptorSetLayout);
        
        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                PSetLayouts = layoutsPtr,
                DescriptorSetCount = (uint) layouts.Length,
                DescriptorPool = materialInfoDescriptorPool
            };
            
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            {
                if (vk.AllocateDescriptorSets(Device, in allocInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate descriptor sets!");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo albedoInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = albedoView,
                Sampler = textureSampler
            };

            DescriptorImageInfo normalInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = normalView,
                Sampler = textureSampler
            };

            DescriptorImageInfo aoRoughnessMetalnessInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = aoRoughnessMetalnessView,
                Sampler = textureSampler
            };

            var descriptorWrites = new WriteDescriptorSet[]
            {
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &albedoInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &normalInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 2,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &aoRoughnessMetalnessInfo
                }
            };

            fixed (WriteDescriptorSet* descriptorWritesPtr = descriptorWrites)
                vk.UpdateDescriptorSets(Device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
        }

        return descriptorSets;
    }

    // Screen Texture Info Descriptor Set
    // ----------------------------------

    DescriptorSetLayout CreateScreenTexureInfoDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding albedoSamplerBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding normalSamplerBinding = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding aoRoughnessMetalnessSamplerBinding = new()
        {
            Binding = 2,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding positionSamplerBinding = new()
        {
            Binding = 3,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc[] { albedoSamplerBinding, normalSamplerBinding, aoRoughnessMetalnessSamplerBinding, positionSamplerBinding };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };

        if (vk.CreateDescriptorSetLayout(Device, in layoutInfo, null, out var layout) != Result.Success)
        {
            throw new Exception("Failed to create descriptor set layout!");
        }

        return layout;
    }

    DescriptorPool CreateScreenTextureInfoDescriptorPool()
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint) MaxFramesInFlight * 4
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = (uint) MaxFramesInFlight
        };

        if (vk.CreateDescriptorPool(Device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorSet[] CreateScreenTextureInfoDescriptorSets(ImageView albedo, ImageView normal, ImageView aoRoughnessMetalness, ImageView position)
    {
        var descriptorSets = new DescriptorSet[MaxFramesInFlight];

        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, screenTextureDescriptorSetLayout);
        
        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                PSetLayouts = layoutsPtr,
                DescriptorSetCount = (uint) layouts.Length,
                DescriptorPool = screenTextureDescriptorPool
            };
            
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            {
                if (vk.AllocateDescriptorSets(Device, in allocInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate descriptor sets!");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo albedoInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = albedo,
                Sampler = textureSampler
            };

            DescriptorImageInfo normalInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = normal,
                Sampler = textureSampler
            };

            DescriptorImageInfo aoRoughnessMetalnessInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = aoRoughnessMetalness,
                Sampler = textureSampler
            };

            DescriptorImageInfo positionInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = position,
                Sampler = textureSampler
            };

            var descriptorWrites = new WriteDescriptorSet[]
            {
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &albedoInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &normalInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 2,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &aoRoughnessMetalnessInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 3,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &positionInfo
                }
            };

            fixed (WriteDescriptorSet* descriptorWritesPtr = descriptorWrites)
                vk.UpdateDescriptorSets(Device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
        }

        return descriptorSets;
    }

    void UpdateScreenTextureDescriptorSets(DescriptorSet[] descriptorSets, ImageView albedo, ImageView normal, ImageView aoRoughnessMetalness, ImageView position)
    {
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo albedoInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = albedo,
                Sampler = textureSampler
            };

            DescriptorImageInfo normalInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = normal,
                Sampler = textureSampler
            };

            DescriptorImageInfo aoRoughnessMetalnessInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = aoRoughnessMetalness,
                Sampler = textureSampler
            };

            DescriptorImageInfo positionInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = position,
                Sampler = textureSampler
            };

            var descriptorWrites = new WriteDescriptorSet[]
            {
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &albedoInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &normalInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 2,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &aoRoughnessMetalnessInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSets[i],
                    DstBinding = 3,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &positionInfo
                }
            };

            fixed (WriteDescriptorSet* descriptorWritesPtr = descriptorWrites)
                vk.UpdateDescriptorSets(Device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
        }
    }

    // Skybox Texture Descriptor Set
    // -----------------------------

    DescriptorSetLayout CreateSingleTextureDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding colorSamplerBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &colorSamplerBinding
        };

        if (vk.CreateDescriptorSetLayout(Device, in layoutInfo, null, out var layout) != Result.Success)
        {
            throw new Exception("Failed to create descriptor set layout!");
        }

        return layout;
    }

    DescriptorPool CreateSingleTextureDescriptorPool(uint maxSets)
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint) MaxFramesInFlight * maxSets
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = (uint) MaxFramesInFlight * maxSets,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
        };

        if (vk.CreateDescriptorPool(Device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorSet[] CreateSingleTextureDescriptorSets(ImageView imageView)
    {
        var descriptorSets = new DescriptorSet[MaxFramesInFlight];

        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, singleTextureDescriptorSetLayout);
        
        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                PSetLayouts = layoutsPtr,
                DescriptorSetCount = (uint) layouts.Length,
                DescriptorPool = singleTextureDescriptorPool
            };
            
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            {
                if (vk.AllocateDescriptorSets(Device, in allocInfo, descriptorSetsPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate descriptor sets!");
                }
            }
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo colorInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = imageView,
                Sampler = textureSampler
            };

            WriteDescriptorSet descriptorWrite = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &colorInfo
            };

            vk.UpdateDescriptorSets(Device, 1, &descriptorWrite, 0, default);
        }

        return descriptorSets;
    }

    void UpdateSingleTextureDescriptorSets(DescriptorSet[] descriptorSets, ImageView imageView)
    {
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo colorInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = imageView,
                Sampler = textureSampler
            };

            WriteDescriptorSet descriptorWrite = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &colorInfo
            };

            vk.UpdateDescriptorSets(Device, 1, &descriptorWrite, 0, default);        
        }
    }
}

