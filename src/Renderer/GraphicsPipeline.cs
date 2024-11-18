using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
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

    const uint GeometryPassColorAttachmentCount = 4;
    const uint CompositionPassColorAttachmentCount = 1;

    GraphicsPipeline CreateGeometryPipeline()
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + GeometryVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + GeometryFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(GeometryPassColorAttachmentCount)
                       .SetDepthStencilInfo(true, true, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(materialInfoDescriptorSetLayout)
                       .AddPushConstantRange((uint) Unsafe.SizeOf<Matrix4X4<float>>(), 0, ShaderStageFlags.VertexBit);

        return pipelineBuilder.Build(geometryRenderPass, 0);
    }

    GraphicsPipeline CreateCompositionPipeline()
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + CompositionVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + CompositionFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.BackBit, FrontFace.CounterClockwise)
                       .SetColorBlendingNone(CompositionPassColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(screenTextureDescriptorSetLayout);

        return pipelineBuilder.Build(compositionRenderPass, 0);
    }

    GraphicsPipeline CreateLightingPipeline()
    {
        byte[] vertexShaderCode = File.ReadAllBytes(ShadersPath + LightingVertexShaderFilename);
        byte[] fragmentShaderCode = File.ReadAllBytes(ShadersPath + LightingFragmentShaderFilename);

        GraphicsPipelineBuilder pipelineBuilder = new(device);
        pipelineBuilder.SetShaders(vertexShaderCode, fragmentShaderCode)
                       .SetInputAssemblyInfo(PrimitiveTopology.TriangleList, false)
                       .SetRasterizerInfo(PolygonMode.Fill, CullModeFlags.FrontBit, FrontFace.CounterClockwise)
                       .SetColorBlendingAdditive(CompositionPassColorAttachmentCount)
                       .SetDepthStencilInfo(false, false, CompareOp.Less)
                       .AddDescriptorSetLayout(sceneInfoDescriptorSetLayout)
                       .AddDescriptorSetLayout(screenTextureDescriptorSetLayout)
                       .AddPushConstantRange((uint) Unsafe.SizeOf<LightInfo>(), 0, ShaderStageFlags.VertexBit);

        return pipelineBuilder.Build(compositionRenderPass, 0);
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

        if (vk.CreateDescriptorSetLayout(device, in layoutInfo, null, out var layout) != Result.Success)
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

        if (vk.CreateDescriptorPool(device, in poolInfo, null, out var descriptorPool) != Result.Success)
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
                if (vk.AllocateDescriptorSets(device, in allocInfo, descriptorSetsPtr) != Result.Success)
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

            vk.UpdateDescriptorSets(device, 1, ref descriptorWrite, 0, default);
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

        if (vk.CreateDescriptorSetLayout(device, in layoutInfo, null, out var layout) != Result.Success)
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

        if (vk.CreateDescriptorPool(device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorSet[] CreateMaterialInfoDescriptorSets(ImageView albedoView, ImageView normalView, ImageView aoRoughnessMetalnessView)
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
                if (vk.AllocateDescriptorSets(device, in allocInfo, descriptorSetsPtr) != Result.Success)
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
                vk.UpdateDescriptorSets(device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
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

        if (vk.CreateDescriptorSetLayout(device, in layoutInfo, null, out var layout) != Result.Success)
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

        if (vk.CreateDescriptorPool(device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorSet[] CreateScreenTextureInfoDescriptorSets()
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
                if (vk.AllocateDescriptorSets(device, in allocInfo, descriptorSetsPtr) != Result.Success)
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
                ImageView = gBuffer.Albedo.ImageView,
                Sampler = textureSampler
            };

            DescriptorImageInfo normalInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Normal.ImageView,
                Sampler = textureSampler
            };

            DescriptorImageInfo aoRoughnessMetalnessInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Specular.ImageView,
                Sampler = textureSampler
            };

            DescriptorImageInfo positionInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Position.ImageView,
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
                vk.UpdateDescriptorSets(device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
        }

        return descriptorSets;
    }

    void UpdateScreenTextureDescriptorSets(DescriptorSet[] descriptorSets, GBuffer gBuffer)
    {
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            DescriptorImageInfo albedoInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Albedo.ImageView,
                Sampler = textureSampler
            };

            DescriptorImageInfo normalInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Normal.ImageView,
                Sampler = textureSampler
            };

            DescriptorImageInfo aoRoughnessMetalnessInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Specular.ImageView,
                Sampler = textureSampler
            };

            DescriptorImageInfo positionInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Position.ImageView,
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
                vk.UpdateDescriptorSets(device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
        }
    }
}
