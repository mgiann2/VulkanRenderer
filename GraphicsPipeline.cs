using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

struct GraphicsPipeline
{
    public Pipeline Pipeline { get; init; }
    public PipelineLayout Layout { get; init; }
}

unsafe public partial class VulkanRenderer
{
    GraphicsPipeline CreatePipeline(
            string vertexShaderPath,
            string fragmentShaderPath,
            RenderPass renderPass,
            DescriptorSetLayout[] descriptorSetLayouts)
    {
        byte[] vertexShaderCode = File.ReadAllBytes(vertexShaderPath);
        byte[] fragmentShaderCode = File.ReadAllBytes(fragmentShaderPath);

        var vertexShaderModule = CreateShaderModule(vertexShaderCode);
        var fragmentShaderModule = CreateShaderModule(fragmentShaderCode);

        PipelineShaderStageCreateInfo vertexShaderInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertexShaderModule,
            PName = (byte*) SilkMarshal.StringToPtr("main")
        };

        PipelineShaderStageCreateInfo fragmentShaderInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragmentShaderModule,
            PName = (byte*) SilkMarshal.StringToPtr("main")
        };

        var shaderStagesInfo = stackalloc[] { vertexShaderInfo, fragmentShaderInfo };

        var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor };
        PipelineDynamicStateCreateInfo dynamicStateInfo = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        PipelineInputAssemblyStateCreateInfo assemblyInfo = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };
        
        PipelineViewportStateCreateInfo viewportInfo = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        PipelineRasterizationStateCreateInfo rasterizerInfo = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1.0f,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.CounterClockwise
        };

        PipelineMultisampleStateCreateInfo multisampleInfo = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        PipelineColorBlendAttachmentState colorBlendAttachment = new()
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false
        };

        PipelineColorBlendStateCreateInfo colorBlendInfo = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment
        };

        PipelineDepthStencilStateCreateInfo depthStencil = new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less,
            DepthBoundsTestEnable = false
        };

        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = (uint) descriptorSetLayouts.Length,
        };
        fixed (DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts)
            pipelineLayoutInfo.PSetLayouts = descriptorSetLayoutsPtr;

        if (vk.CreatePipelineLayout(device, in pipelineLayoutInfo, null, out var pipelineLayout) != Result.Success)
        {
            throw new Exception("Failed to create pipeline layout!");
        }

        GraphicsPipelineCreateInfo pipelineInfo = new()
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = shaderStagesInfo,
            PInputAssemblyState = &assemblyInfo,
            PViewportState = &viewportInfo,
            PDepthStencilState = &depthStencil,
            PDynamicState = &dynamicStateInfo,
            PColorBlendState = &colorBlendInfo,
            PMultisampleState = &multisampleInfo,
            PRasterizationState = &rasterizerInfo,
            Layout = pipelineLayout,
            RenderPass = renderPass,
            Subpass = 0
        };

        if (vk.CreateGraphicsPipelines(device, default, 1, in pipelineInfo, null, out var pipeline) != Result.Success)
        {
            throw new Exception("Failed to create graphics pipeline!");
        }

        vk.DestroyShaderModule(device, vertexShaderModule, null);
        vk.DestroyShaderModule(device, fragmentShaderModule, null);

        return new GraphicsPipeline
        {
            Layout = pipelineLayout
        };
    }

    DescriptorSetLayout CreateGBufferDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding uboBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.VertexBit
        };
        
        DescriptorSetLayoutBinding albedoSamplerBinding = new()
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding normalSamplerBinding = new()
        {
            Binding = 2,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutBinding metalnessSamplerBinding = new()
        {
            Binding = 3,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc[] { uboBinding, albedoSamplerBinding, normalSamplerBinding, metalnessSamplerBinding };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings,
        };

        if (vk.CreateDescriptorSetLayout(device, in layoutInfo, null, out var layout) != Result.Success)
        {
            throw new Exception("Failed to create descriptor set layout!");
        }

        return layout;
    }

    DescriptorSetLayout CreateCompositionDescriptorSetLayout()
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

        DescriptorSetLayoutBinding specularSamplerBinding = new()
        {
            Binding = 2,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImmutableSamplers = default,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc[] { albedoSamplerBinding, normalSamplerBinding, specularSamplerBinding };

        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings
        };

        if (vk.CreateDescriptorSetLayout(device, in layoutInfo, null, out var layout) != Result.Success)
        {
            throw new Exception("Failed to create descriptor set layout!");
        }

        return layout;
    }

    DescriptorPool CreateGBufferDescriptorPool(uint maxSets)
    {
        var poolSizes = stackalloc[] 
        {
            new DescriptorPoolSize
            {
                Type = DescriptorType.UniformBuffer,
                DescriptorCount = (uint) MaxFramesInFlight * maxSets
            },
            new DescriptorPoolSize
            {
                Type = DescriptorType.CombinedImageSampler,
                DescriptorCount = (uint) MaxFramesInFlight * 3 * maxSets
            }
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
            MaxSets = (uint) MaxFramesInFlight * maxSets
        };

        if (vk.CreateDescriptorPool(device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorPool CreateCompositionDescriptorPool()
    {
        var poolSizes = stackalloc[] 
        {
            new DescriptorPoolSize
            {
                Type = DescriptorType.CombinedImageSampler,
                DescriptorCount = (uint) MaxFramesInFlight * 3
            }
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = poolSizes,
            MaxSets = (uint) MaxFramesInFlight
        };

        if (vk.CreateDescriptorPool(device, in poolInfo, null, out var descriptorPool) != Result.Success)
        {
            throw new Exception("Failed to create descriptor pool!");
        }

        return descriptorPool;
    }

    DescriptorSet[] CreateCompositionDescriptorSets()
    {
        compositionDescriptorSets = new DescriptorSet[MaxFramesInFlight];

        var layouts = new DescriptorSetLayout[MaxFramesInFlight];
        Array.Fill(layouts, compositionDescriptorSetLayout);
        
        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        {
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                PSetLayouts = layoutsPtr,
                DescriptorSetCount = (uint) layouts.Length,
                DescriptorPool = compositionDescriptorPool
            };
            
            fixed (DescriptorSet* compositionDescriptorSetsPtr = compositionDescriptorSets)
            {
                if (vk.AllocateDescriptorSets(device, in allocInfo, compositionDescriptorSetsPtr) != Result.Success)
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

            DescriptorImageInfo specularInfo = new()
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = gBuffer.Specular.ImageView,
                Sampler = textureSampler
            };

            var descriptorWrites = new WriteDescriptorSet[]
            {
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = compositionDescriptorSets[i],
                    DstBinding = 0,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &albedoInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = compositionDescriptorSets[i],
                    DstBinding = 1,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &normalInfo
                },
                new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = compositionDescriptorSets[i],
                    DstBinding = 2,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &specularInfo
                }
            };

            fixed (WriteDescriptorSet* descriptorWritesPtr = descriptorWrites)
                vk.UpdateDescriptorSets(device, (uint) descriptorWrites.Length, descriptorWritesPtr, 0, default);
        }

        return compositionDescriptorSets;
    }
}
