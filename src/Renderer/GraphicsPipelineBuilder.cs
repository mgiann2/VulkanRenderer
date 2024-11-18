using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Renderer;

unsafe public class GraphicsPipelineBuilder
{
    Vk vk;
    Device device;

    PipelineShaderStageCreateInfo[] shaderStagesInfo = new PipelineShaderStageCreateInfo[] { };
    PipelineInputAssemblyStateCreateInfo? assemblyInfo;
    PipelineRasterizationStateCreateInfo? rasterizerInfo;
    PipelineColorBlendStateCreateInfo? colorBlendInfo;
    PipelineDepthStencilStateCreateInfo? depthStencilInfo;
    List<PushConstantRange> pushConstantRanges = new List<PushConstantRange>();
    List<DescriptorSetLayout> descriptorSetLayouts = new List<DescriptorSetLayout>();

    public GraphicsPipelineBuilder(Device device) 
    {
        vk = Vk.GetApi();
        this.device = device;
    }

    public GraphicsPipelineBuilder SetShaders(byte[] vertexShaderCode, byte[] fragmentShaderCode)
    {
        foreach (var shaderStageInfo in shaderStagesInfo)
        {
            vk.DestroyShaderModule(device, shaderStageInfo.Module, null);
        }

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

        shaderStagesInfo = new PipelineShaderStageCreateInfo[] { vertexShaderInfo, fragmentShaderInfo };

        return this;
    }

    public GraphicsPipelineBuilder SetInputAssemblyInfo(PrimitiveTopology topology, bool primitiveRestartEnabled)
    {
        assemblyInfo = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = topology,
            PrimitiveRestartEnable = primitiveRestartEnabled 
        };

        return this;
    }

    public GraphicsPipelineBuilder SetRasterizerInfo(PolygonMode polygonMode, CullModeFlags cullMode, FrontFace frontFace)
    {
        rasterizerInfo = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = polygonMode,
            LineWidth = 1.0f,
            CullMode = cullMode,
            FrontFace = frontFace
        };

        return this;
    }

    public GraphicsPipelineBuilder SetColorBlendingNone(uint attachmentCount)
    {
        var colorBlendAttachments = new PipelineColorBlendAttachmentState[attachmentCount];
        for (int i = 0; i < attachmentCount; i++)
        {
            colorBlendAttachments[i] = new()
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false 
            };
        }

        fixed (PipelineColorBlendAttachmentState* colorBlendAttachmentsPtr = colorBlendAttachments)
        {
            colorBlendInfo = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = attachmentCount,
                PAttachments = colorBlendAttachmentsPtr
            };
        }

        return this;
    }

    public GraphicsPipelineBuilder SetColorBlendingAdditive(uint attachmentCount)
    {
        var colorBlendAttachments = new PipelineColorBlendAttachmentState[attachmentCount];
        for (int i = 0; i < attachmentCount; i++)
        {
            colorBlendAttachments[i] = new()
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.One,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add
            };
        }

        fixed (PipelineColorBlendAttachmentState* colorBlendAttachmentsPtr = colorBlendAttachments)
        {
            colorBlendInfo = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = attachmentCount,
                PAttachments = colorBlendAttachmentsPtr
            };
        }

        return this;
    }

    public GraphicsPipelineBuilder SetDepthStencilInfo(bool enableDepthTest, bool enableDepthWrite, CompareOp depthCompareOp)
    {
        depthStencilInfo = new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = enableDepthTest,
            DepthWriteEnable = enableDepthWrite,
            DepthCompareOp = depthCompareOp,
            DepthBoundsTestEnable = false
        };

        return this;
    }

    public GraphicsPipelineBuilder AddPushConstantRange(uint size, uint offset, ShaderStageFlags stageFlags)
    {
        pushConstantRanges.Add(new() { Size = size, Offset = offset, StageFlags = stageFlags});

        return this;
    }

    public GraphicsPipelineBuilder ClearPushConstantRange()
    {
        pushConstantRanges.Clear();

        return this;
    }

    public GraphicsPipelineBuilder AddDescriptorSetLayout(DescriptorSetLayout descriptorSetLayout)
    {
        descriptorSetLayouts.Add(descriptorSetLayout);

        return this;
    }

    public GraphicsPipelineBuilder ClearDescriptorSetLayouts()
    {
        descriptorSetLayouts.Clear();

        return this;
    }

    public GraphicsPipeline Build(RenderPass renderPass, uint subpass)
    {
        var unsetInfos = GetUnsetPipelineInfos();
        if (unsetInfos.Length > 0)
        {
            throw new Exception($"Failed to build graphics pipeline! Missing build infos: {String.Join(", ", unsetInfos)}");
        }

        var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor };
        PipelineDynamicStateCreateInfo dynamicStateInfo = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        PipelineViewportStateCreateInfo viewportInfo = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        PipelineMultisampleStateCreateInfo multisampleInfo = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var bindingDescription = Vertex.GetBindingDescription();
        var attributeDescriptions = Vertex.GetAttributeDescriptions();
        PipelineVertexInputStateCreateInfo vertexInfo = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &bindingDescription,
            VertexAttributeDescriptionCount = (uint) attributeDescriptions.Length
        };
        fixed (VertexInputAttributeDescription* attributeDescriptionsPtr = attributeDescriptions)
            vertexInfo.PVertexAttributeDescriptions = attributeDescriptionsPtr;

        PipelineLayoutCreateInfo layoutInfo = new();
        fixed (DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts.ToArray())
        fixed (PushConstantRange* pushConstantRangesPtr = pushConstantRanges.ToArray())
        {
            layoutInfo = layoutInfo with
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint) descriptorSetLayouts.Count(),
                PSetLayouts = descriptorSetLayoutsPtr,
                PushConstantRangeCount = (uint) pushConstantRanges.Count(),
                PPushConstantRanges = pushConstantRangesPtr
            };
        }

        if (vk.CreatePipelineLayout(device, in layoutInfo, null, out var pipelineLayout) != Result.Success)
        {
            throw new Exception("Failed to create pipeline layout!");
        }

        // all states below should not be null due to check at beginnig of method
        var assemblyState = assemblyInfo ?? new();
        var rasterizationState = rasterizerInfo ?? new();
        var colorBlendState = colorBlendInfo ?? new();
        var depthStencilState = depthStencilInfo ?? new();
        GraphicsPipelineCreateInfo pipelineInfo = new();
        fixed (PipelineShaderStageCreateInfo* shaderStagesInfoPtr = shaderStagesInfo)
        {
            pipelineInfo = pipelineInfo with
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = (uint) shaderStagesInfo.Length,
                PStages = shaderStagesInfoPtr,
                PInputAssemblyState = &assemblyState,
                PViewportState = &viewportInfo,
                PRasterizationState = &rasterizationState,
                PMultisampleState = &multisampleInfo,
                PColorBlendState = &colorBlendState,
                PDepthStencilState = &depthStencilState,
                PDynamicState = &dynamicStateInfo,
                PVertexInputState = &vertexInfo,
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = subpass
            };
        }

        if (vk.CreateGraphicsPipelines(device, default, 1, in pipelineInfo, null, out var pipeline) != Result.Success)
        {
            throw new Exception("Failed to create graphics pipeline!");
        }

        return new GraphicsPipeline{ Pipeline = pipeline, Layout = pipelineLayout };
    }

    string[] GetUnsetPipelineInfos()
    {
        var unsetInfos = new List<string>();   
        
        if (shaderStagesInfo.Length < 1)
            unsetInfos.Add("ShaderStagesInfo");
        
        if (!assemblyInfo.HasValue)
            unsetInfos.Add("InputAssemblyInfo");

        if (!rasterizerInfo.HasValue)
            unsetInfos.Add("RasterizerInfo");

        if (!colorBlendInfo.HasValue)
            unsetInfos.Add("ColorBlendInfo");

        if (!depthStencilInfo.HasValue)
            unsetInfos.Add("DepthStencilInfo");

        return unsetInfos.ToArray();
    }

    ShaderModule CreateShaderModule(byte[] shaderCode)
    {
        ShaderModuleCreateInfo createInfo = new()
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint) shaderCode.Length,
        };

        ShaderModule shaderModule;

        fixed (byte* shaderCodePtr = shaderCode)
        {
            createInfo.PCode = (uint*) shaderCodePtr;

            if (vk.CreateShaderModule(device, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader!");
            }
        }

        return shaderModule;
    }
}
