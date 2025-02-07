using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Renderer;

[StructLayout(LayoutKind.Explicit)]
public struct SceneInfo
{
    [FieldOffset(0)]public Matrix4X4<float> CameraView;
    [FieldOffset(64)]public Matrix4X4<float> CameraProjection;
    [FieldOffset(128)]public Vector3D<float> CameraPosition;
    [FieldOffset(144)]public Vector3D<float> DirectionalLightDirection;
    [FieldOffset(160)]public Vector3D<float> DirectionalLightColor;
    [FieldOffset(176)]public Matrix4X4<float> LightSpaceMatrix;
}

[StructLayout(LayoutKind.Explicit)]
public struct LightInfo
{
    [FieldOffset(0)]public Matrix4X4<float> Model;
    [FieldOffset(64)]public Vector3D<float> Position;
    [FieldOffset(80)]public Vector3D<float> Color;
}

[StructLayout(LayoutKind.Explicit)]
public struct SolidColorObjectInfo
{
    [FieldOffset(0)]public Matrix4X4<float> Model;
    [FieldOffset(64)]public Vector3D<float> Color;
}

struct DrawCall
{
    public Model Model { get; }
    public Matrix4X4<float> ModelMatrix { get; }
    public Vector3D<float> Color { get; }

    public DrawCall(Model model, Matrix4X4<float> modelMatrix, Vector3D<float> color)
    {
        Model = model;
        ModelMatrix = modelMatrix;
        Color = color;
    }
}

unsafe public partial class VulkanRenderer : IDisposable
{
    const string AssetsPath = "assets/";
    const string ShadersPath = "compiled_shaders/";

    private List<DrawCall> solidModelDrawCalls = new();
    private List<DrawCall> transparentModelDrawCalls = new();

    readonly bool EnableValidationLayers;
    const int MaxFramesInFlight = 2;
    const int CubemapMapSceneInfoDescriptors = 6;
    const int MaxMaterialDescriptorSets = 128;
    const int MaxLights = 32; // Lights assumed to have at most MaxLights number of lights, this should be enforced

    public List<Light> Lights = new List<Light>(); // TODO: Improve interface on how lights are rendered in frame
    private RenderStage[] pointShadowRenderStages = new RenderStage[MaxLights];
    private DepthCubeMapOnlyAttachment[][] pointShadowCubeMaps = new DepthCubeMapOnlyAttachment[MaxLights][];
    private DescriptorSet[][] shadowMatricesDescriptorSets = new DescriptorSet[MaxLights][];
    private DescriptorSet[][] pointShadowDescriptorSets = new DescriptorSet[MaxLights][];

    // Very Low Resolution: 256x256 => 0.25Mb per face => 1.5Mb per cubemap => 192Mb for 128 lights
    // Low Resolution: 512x512 => 1Mb per face => 6Mb per cubemap => 0.75Gb for 128 lights
    // Medium Resolution: 1024x1024 => 4Mb per face => 24Mb per cubemap => 3Gb for 128 lights
    // High Resolution : 2048x2048 => 16Mb per face => 96Mb per cubemap => 12Gb for 128 lights
    const uint ShadowMapResolution = 1024;
    const uint PointShadowMapResolution = 256;

    const string SphereMeshPath = AssetsPath + "models/sphere/sphere.glb";
    const string SkyboxTexturePath = AssetsPath + "hdris/EveningSkyHDRI.jpg";

    Mesh screenQuadMesh;
    Mesh cubeMesh;
    Mesh sphereMesh;
    SceneInfo sceneInfo;

    readonly Vk vk = VulkanHelper.Vk;
    IWindow window;
    public SCDevice SCDevice;

    GBufferAttachments[] gBufferAttachments;
    CompositionAttachments[] compositionAttachments;
    BloomAttachments[] bloomAttachments1;
    BloomAttachments[] bloomAttachments2;
    SwapChainAttachment[] swapChainAttachments;
    SingleColorAttachment[] environmentMapAttachments;
    SingleColorAttachment[] irradianceMapAttachments;
    SingleColorAttachment brdfLUTAttachment;
    DepthOnlyAttachment[] depthMapAttachment;

    RenderPass pointShadowMapRenderPass;

    RenderStage geometryRenderStage;
    RenderStage compositionRenderStage;
    RenderStage bloomRenderStage1;
    RenderStage bloomRenderStage2;
    RenderStage postProcessRenderStage;
    RenderStage equirectangularToCubemapRenderStage;
    RenderStage irradianceMapRenderStage;
    RenderStage brdfLUTTextureRenderStage;
    RenderStage depthMapRenderStage;

    Buffer[] sceneInfoBuffers;
    DeviceMemory[] sceneInfoBuffersMemory;
    Buffer[][] shadowMatricesBuffers = new Buffer[MaxLights][];
    DeviceMemory[][] shadowMatricesMemories = new DeviceMemory[MaxLights][];

    DescriptorSetLayout uniformBufferDescriptorSetLayout;
    DescriptorSetLayout materialInfoDescriptorSetLayout;
    DescriptorSetLayout screenTextureDescriptorSetLayout;
    DescriptorSetLayout iblTexturesDescriptorSetLayout;
    DescriptorSetLayout singleTextureDescriptorSetLayout;

    DescriptorPool uniformBufferDescriptorPool;
    public DescriptorPool materialInfoDescriptorPool;
    DescriptorPool screenTextureDescriptorPool;
    DescriptorPool iblTexturesDescriptorPool;
    DescriptorPool singleTextureDescriptorPool;

    DescriptorSet[] sceneInfoDescriptorSets;
    DescriptorSet[] screenTextureInfoDescriptorSets;
    DescriptorSet[] compositionOutputTextureDescriptorSets;
    DescriptorSet[] thresholdTextureDescriptorSets;
    DescriptorSet[] dirShadowMapDescriptorSets;
    DescriptorSet[] bloomPass1OutputTextureDescriptorSets;
    DescriptorSet[] bloomPass2OutputTextureDescriptorSets;
    DescriptorSet skyboxTextureDescriptorSet;
    DescriptorSet iblTexturesDescriptorSet;

    GraphicsPipeline geometryPipeline;
    GraphicsPipeline compositionPipeline;
    GraphicsPipeline lightingPipeline;
    GraphicsPipeline solidColorPipeine;
    GraphicsPipeline skyboxPipeline;
    GraphicsPipeline bloom1Pipeline;
    GraphicsPipeline bloom2Pipeline;
    GraphicsPipeline postProcessPipeline;
    GraphicsPipeline equirectangularToCubemapPipeline;
    GraphicsPipeline irradianceMapPipeline;
    GraphicsPipeline brdfPipeline;
    GraphicsPipeline depthPipeline;
    GraphicsPipeline pointShadowPipeline;

    Sampler textureSampler;
    Sampler bloomSampler;
    Sampler shadowSampler;

    Semaphore[] imageAvailableSemaphores;
    Semaphore[] geomSemaphores;
    Semaphore[] compSemaphores;
    Semaphore[] bloom1Semaphores;
    Semaphore[] bloom2Semaphores;
    Semaphore[] postProcessSemaphores;
    Fence[] inFlightFences;

    Cubemap skyboxCubemap;
    Cubemap irradianceCubemap;
    ImageView brdfLUTImageView;
    Cubemap prefilteredCubemap;

    uint currentFrame;
    bool framebufferResized = false;
    uint imageIndex;
    bool isFrameEnded = true;

    bool disposedValue;
    
    public VulkanRenderer(IWindow window, bool enableValidationLayers = false)
    {
        this.window = window;
        
        EnableValidationLayers = enableValidationLayers;
        
        SCDevice = new(window, EnableValidationLayers);

        (sceneInfoBuffers, sceneInfoBuffersMemory) = VulkanHelper.CreateUniformBuffers(SCDevice, (ulong) Unsafe.SizeOf<SceneInfo>(), MaxFramesInFlight);
        for (int i = 0; i < MaxLights; i++)
        {
            (shadowMatricesBuffers[i], shadowMatricesMemories[i]) = VulkanHelper.CreateUniformBuffers(SCDevice, 
                    (ulong) Unsafe.SizeOf<Matrix4X4<float>>() * 6, MaxFramesInFlight);
        }

        textureSampler = VulkanHelper.CreateTextureSampler(SCDevice);
        bloomSampler = VulkanHelper.CreateBloomSampler(SCDevice);
        shadowSampler = VulkanHelper.CreateShadowSampler(SCDevice);
       
        // Create descriptor pools
        uniformBufferDescriptorPool = CreateUniformBufferDescriptorPool(MaxFramesInFlight + CubemapMapSceneInfoDescriptors + MaxLights * MaxFramesInFlight);
        materialInfoDescriptorPool = CreateMaterialInfoDescriptorPool(MaxMaterialDescriptorSets);
        screenTextureDescriptorPool = CreateScreenTextureInfoDescriptorPool();
        iblTexturesDescriptorPool = CreateIBLTexturesDescriptorPool();
        singleTextureDescriptorPool = CreateSingleTextureDescriptorPool(MaxLights * MaxFramesInFlight + 32);

        // Create descriptor set layouts
        uniformBufferDescriptorSetLayout = CreateUniformBufferDescriptorSetLayout();
        materialInfoDescriptorSetLayout = CreateMaterialInfoDescriptorSetLayout();
        screenTextureDescriptorSetLayout = CreateScreenTexureInfoDescriptorSetLayout();
        iblTexturesDescriptorSetLayout = CreateIBLTexturesDescriptorSetLayout();
        singleTextureDescriptorSetLayout = CreateSingleTextureDescriptorSetLayout();

        // Create render stages
        (geometryRenderStage, gBufferAttachments) = CreateGeometryRenderStage();
        (compositionRenderStage, compositionAttachments) = CreateCompositionRenderStage();
        (bloomRenderStage1, bloomAttachments1) = CreateBloomRenderStage();
        (bloomRenderStage2, bloomAttachments2) = CreateBloomRenderStage();
        (postProcessRenderStage, swapChainAttachments) = CreatePostProcessRenderStage();
        (equirectangularToCubemapRenderStage, environmentMapAttachments) = CreateEquirectangularToCubemapRenderStage();
        (irradianceMapRenderStage, irradianceMapAttachments) = CreateIrradianceMapRenderStage();
        (brdfLUTTextureRenderStage, brdfLUTAttachment) = CreateBRDFLUTTextureRenderStage();
        (depthMapRenderStage, depthMapAttachment) = CreateDepthMapRenderStage();
        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.SetDepthStencilAttachment(VulkanHelper.FindDepthFormat(SCDevice), ImageLayout.ShaderReadOnlyOptimal);
        pointShadowMapRenderPass = renderPassBuilder.Build();
        for (int i = 0; i < pointShadowRenderStages.Length; i++)
        {
            (pointShadowRenderStages[i], pointShadowCubeMaps[i]) = CreatePointShadowRenderStage(); 
        }

        // Create descriptor sets
        sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets(sceneInfoBuffers);
        screenTextureInfoDescriptorSets = CreateScreenTextureInfoDescriptorSets(gBufferAttachments);
        compositionOutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionAttachments.Select(att => att.Color.ImageView).ToArray(), textureSampler);
        thresholdTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionAttachments.Select(att => att.ThresholdedColor.ImageView).ToArray(), textureSampler);
        dirShadowMapDescriptorSets = CreateSingleTextureDescriptorSets(depthMapAttachment.Select(att => att.Depth.ImageView).ToArray(), shadowSampler);
        bloomPass1OutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(bloomAttachments1.Select(att => att.Color.ImageView).ToArray(), bloomSampler);
        bloomPass2OutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(bloomAttachments2.Select(att => att.Color.ImageView).ToArray(), bloomSampler);
        for (int i = 0; i < MaxLights; i++)
        {
            shadowMatricesDescriptorSets[i] = CreateShadowMatricesDescriptorSets(shadowMatricesBuffers[i]);
            pointShadowDescriptorSets[i] = CreateSingleTextureDescriptorSets(pointShadowCubeMaps[i].Select(att => att.Depth.ImageView).ToArray(), shadowSampler);
        }

        // Create pipelines
        geometryPipeline = CreateGeometryPipeline(geometryRenderStage.RenderPass);
        compositionPipeline = CreateCompositionPipeline(compositionRenderStage.RenderPass);
        lightingPipeline = CreateLightingPipeline(compositionRenderStage.RenderPass);
        solidColorPipeine = CreateSolidColorPipeline(compositionRenderStage.RenderPass);
        skyboxPipeline = CreateSkyboxPipeline(compositionRenderStage.RenderPass);
        bloom1Pipeline = CreateBloomPipeline(bloomRenderStage1.RenderPass);
        bloom2Pipeline = CreateBloomPipeline(bloomRenderStage2.RenderPass);
        postProcessPipeline = CreatePostProcessPipeline(postProcessRenderStage.RenderPass);
        equirectangularToCubemapPipeline = CreateEquirectangularToCubemapPipeline(equirectangularToCubemapRenderStage.RenderPass);
        irradianceMapPipeline = CreateIrradiancePipeline(irradianceMapRenderStage.RenderPass);
        brdfPipeline = CreateBRDFLUTPipeline(brdfLUTTextureRenderStage.RenderPass);
        depthPipeline = CreateDepthPipeline(depthMapRenderStage.RenderPass);
        pointShadowPipeline = CreatePointShadowPipeline(pointShadowMapRenderPass);

        // Create sync objects
        imageAvailableSemaphores = VulkanHelper.CreateSemaphores(SCDevice, MaxFramesInFlight);
        geomSemaphores = VulkanHelper.CreateSemaphores(SCDevice, MaxFramesInFlight);
        compSemaphores = VulkanHelper.CreateSemaphores(SCDevice, MaxFramesInFlight);
        bloom1Semaphores = VulkanHelper.CreateSemaphores(SCDevice, MaxFramesInFlight);
        bloom2Semaphores = VulkanHelper.CreateSemaphores(SCDevice, MaxFramesInFlight);
        postProcessSemaphores = VulkanHelper.CreateSemaphores(SCDevice, MaxFramesInFlight);
        inFlightFences = VulkanHelper.CreateFences(SCDevice, MaxFramesInFlight, true);

        // Load primitive meshes
        screenQuadMesh = PrimitiveMesh.CreateQuadMesh(this);
        cubeMesh = PrimitiveMesh.CreateCubeMesh(this);
        sphereMesh = new Mesh(this, SphereMeshPath);

        // Generate image based lighting textures
        skyboxCubemap = CreateSkyboxCubemap();
        irradianceCubemap = CreateIrradianceCubemap(skyboxCubemap);
        brdfLUTImageView = CreateBRDFLUTTexture();
        prefilteredCubemap = CreatePrefilteredCubemap(skyboxCubemap);

        // Create skybox descriptor sets
        skyboxTextureDescriptorSet = CreateSingleTextureDescriptorSets(new ImageView[]{ skyboxCubemap.CubemapImageView }, textureSampler)[0];
        iblTexturesDescriptorSet = CreateIBLTexturesDescriptorSet(irradianceCubemap.CubemapImageView,
                                                                   brdfLUTImageView,
                                                                   prefilteredCubemap.CubemapImageView);

        window.FramebufferResize += OnFramebufferResize;
    }

    public void BeginFrame()
    {
        if (!isFrameEnded) throw new Exception("Tried to begin frame before ending current frame!");

        vk.WaitForFences(SCDevice.LogicalDevice, 1, ref inFlightFences[currentFrame], true, ulong.MaxValue);

        imageIndex = 0;
        var swapchainInfo = SCDevice.SwapchainInfo;
        var result = swapchainInfo.KhrSwapchain.AcquireNextImage(SCDevice.LogicalDevice, swapchainInfo.Swapchain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default, ref imageIndex);
        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("Failed to acquire next image!");
        }

        vk.ResetFences(SCDevice.LogicalDevice, 1, ref inFlightFences[currentFrame]);

        geometryRenderStage.ResetCommandBuffer(currentFrame);
        compositionRenderStage.ResetCommandBuffer(currentFrame);
        postProcessRenderStage.ResetCommandBuffer(currentFrame);
        depthMapRenderStage.ResetCommandBuffer(currentFrame);
        for (int i = 0; i < Lights.Count(); i++)
        {
            pointShadowRenderStages[i].ResetCommandBuffer(currentFrame);
        }

        solidModelDrawCalls.Clear();
        transparentModelDrawCalls.Clear();
    }

    public void EndFrame()
    {
        if (!isFrameEnded) throw new Exception("Tried to end frame before beginning a new one!");

        List<CommandBuffer> geomCommandBuffers = new();
        // Generate point shadow maps
        // --------------------------
        Matrix4X4<float> shadowProj = Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, 1.0f, 25.0f);
        for (int i = 0; i < Lights.Count(); i++)
        {
            var lightPos = Lights[i].Position;
            var farPlane = 25.0f;
            Matrix4X4<float>[] shadowMatrices = new Matrix4X4<float>[]
            {
                Matrix4X4.CreateLookAt(lightPos, lightPos + Vector3D<float>.UnitX, -Vector3D<float>.UnitY) * shadowProj,
                Matrix4X4.CreateLookAt(lightPos, lightPos - Vector3D<float>.UnitX, -Vector3D<float>.UnitY) * shadowProj,
                Matrix4X4.CreateLookAt(lightPos, lightPos + Vector3D<float>.UnitY, Vector3D<float>.UnitZ) * shadowProj,
                Matrix4X4.CreateLookAt(lightPos, lightPos - Vector3D<float>.UnitY, -Vector3D<float>.UnitZ) * shadowProj,
                Matrix4X4.CreateLookAt(lightPos, lightPos + Vector3D<float>.UnitZ, -Vector3D<float>.UnitY) * shadowProj,
                Matrix4X4.CreateLookAt(lightPos, lightPos - Vector3D<float>.UnitZ, -Vector3D<float>.UnitY) * shadowProj,
            };
            void* data;
            vk.MapMemory(SCDevice.LogicalDevice, shadowMatricesMemories[i][currentFrame], 0, (ulong) Unsafe.SizeOf<Matrix4X4<float>>() * 6, 0, &data);
            shadowMatrices.CopyTo(new Span<Matrix4X4<float>>(data, 6));
            vk.UnmapMemory(SCDevice.LogicalDevice, shadowMatricesMemories[i][currentFrame]);

            var pointShadowRenderStage = pointShadowRenderStages[i];
            var descriptorSet = shadowMatricesDescriptorSets[i][currentFrame];

            pointShadowRenderStage.BeginCommands(currentFrame);
            var commandBuffer = pointShadowRenderStage.GetCommandBuffer(currentFrame);
            pointShadowRenderStage.BeginRenderPass(currentFrame, currentFrame);

            vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pointShadowPipeline.Pipeline);

            vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, pointShadowPipeline.Layout,
                    0, 1, &descriptorSet, 0, default);
            vk.CmdPushConstants(commandBuffer, pointShadowPipeline.Layout,
                    ShaderStageFlags.FragmentBit, 64, (uint) Unsafe.SizeOf<Vector3D<float>>(), &lightPos);
            vk.CmdPushConstants(commandBuffer, pointShadowPipeline.Layout,
                    ShaderStageFlags.FragmentBit, 76, (uint) Unsafe.SizeOf<float>(), &farPlane);

            foreach (var drawCall in solidModelDrawCalls)
            {
                var model = drawCall.Model;
                var modelMatrix = drawCall.ModelMatrix;

                model.Mesh.Bind(commandBuffer);
                vk.CmdPushConstants(commandBuffer, pointShadowPipeline.Layout,
                                ShaderStageFlags.VertexBit, 0, (uint) Unsafe.SizeOf<Matrix4X4<float>>(), &modelMatrix);
                model.Mesh.Draw(commandBuffer);
            }

            pointShadowRenderStage.EndRenderPass(currentFrame);
            pointShadowRenderStage.EndCommands(currentFrame);
            geomCommandBuffers.Add(commandBuffer);
        }

        // Begin Geometry Render Pass
        // --------------------------
        var geometryCommandBuffer = geometryRenderStage.GetCommandBuffer(currentFrame);
        geometryRenderStage.BeginCommands(currentFrame);
        geometryRenderStage.BeginRenderPass(currentFrame, currentFrame);
        
        vk.CmdBindPipeline(geometryCommandBuffer, PipelineBindPoint.Graphics, geometryPipeline.Pipeline);

        vk.CmdBindDescriptorSets(geometryCommandBuffer, PipelineBindPoint.Graphics,
                                 geometryPipeline.Layout, 0, 1, in sceneInfoDescriptorSets[currentFrame], 0, default);

        foreach (var drawCall in solidModelDrawCalls)
        {
            var model = drawCall.Model;
            var modelMatrix = drawCall.ModelMatrix;

            model.Mesh.Bind(geometryCommandBuffer);
            BindMaterial(model.Material);

            vk.CmdPushConstants(geometryCommandBuffer, geometryPipeline.Layout,
                            ShaderStageFlags.VertexBit, 0, (uint) Unsafe.SizeOf<Matrix4X4<float>>(), &modelMatrix);
            model.Mesh.Draw(geometryCommandBuffer);
        }

        geometryRenderStage.EndRenderPass(currentFrame);
        geometryRenderStage.EndCommands(currentFrame);
        geomCommandBuffers.Add(geometryCommandBuffer);

        // Begin directional light shadow render pass
        // ------------------------------------------
        depthMapRenderStage.BeginCommands(currentFrame);
        depthMapRenderStage.BeginRenderPass(currentFrame, currentFrame);
        var depthMapCommandBuffer = depthMapRenderStage.GetCommandBuffer(currentFrame);

        // draw depth map of directional light
        vk.CmdBindPipeline(depthMapCommandBuffer, PipelineBindPoint.Graphics, depthPipeline.Pipeline);

        foreach (var drawCall  in solidModelDrawCalls)
        {
            var model = drawCall.Model;
            var data = stackalloc[] { drawCall.ModelMatrix, sceneInfo.LightSpaceMatrix };

            model.Mesh.Bind(depthMapCommandBuffer);
            vk.CmdPushConstants(depthMapCommandBuffer, depthPipeline.Layout,
                            ShaderStageFlags.VertexBit, 0, (uint) Unsafe.SizeOf<Matrix4X4<float>>() * 2, data);
            model.Mesh.Draw(depthMapCommandBuffer);
        }

        depthMapRenderStage.EndRenderPass(currentFrame);
        depthMapRenderStage.EndCommands(currentFrame);
        geomCommandBuffers.Add(depthMapCommandBuffer);

        // Begin composition render pass
        // -----------------------------
        compositionRenderStage.BeginCommands(currentFrame);
        compositionRenderStage.BeginRenderPass(currentFrame, currentFrame);
        var compositionCommandBuffer = compositionRenderStage.GetCommandBuffer(currentFrame);

        // draw skybox
        var skyboxDescriptorSets = stackalloc[] { sceneInfoDescriptorSets[currentFrame], skyboxTextureDescriptorSet };
        vk.CmdBindPipeline(compositionCommandBuffer, PipelineBindPoint.Graphics, skyboxPipeline.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffer, PipelineBindPoint.Graphics,
                skyboxPipeline.Layout, 0, 2, skyboxDescriptorSets, 0, default);

        cubeMesh.Bind(compositionCommandBuffer);
        cubeMesh.Draw(compositionCommandBuffer);

        // draw gbuffer objects
        var descriptorSets = stackalloc[] 
        { 
            sceneInfoDescriptorSets[currentFrame],
            screenTextureInfoDescriptorSets[currentFrame],
            iblTexturesDescriptorSet,
            dirShadowMapDescriptorSets[currentFrame]
        };

        vk.CmdBindPipeline(compositionCommandBuffer, PipelineBindPoint.Graphics, compositionPipeline.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffer, PipelineBindPoint.Graphics,
                compositionPipeline.Layout, 0, 4, descriptorSets, 0, default);

        screenQuadMesh.Bind(compositionCommandBuffer);
        screenQuadMesh.Draw(compositionCommandBuffer);

        // draw smalls cubes at each point light position
        vk.CmdBindPipeline(compositionCommandBuffer, PipelineBindPoint.Graphics, solidColorPipeine.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffer, PipelineBindPoint.Graphics,
                solidColorPipeine.Layout, 0, 1, descriptorSets, 0, default);
        cubeMesh.Bind(compositionCommandBuffer);

        var scaleMatrix = Matrix4X4.CreateScale<float>(0.1f);
        foreach (var light in Lights)
        {
            SolidColorObjectInfo lightCubeInfo = new(){ Model = scaleMatrix * Matrix4X4.CreateTranslation(light.Position) , Color = light.Color };

            vk.CmdPushConstants(compositionCommandBuffer, solidColorPipeine.Layout,
                                ShaderStageFlags.VertexBit, 0,
                                (uint) Unsafe.SizeOf<SolidColorObjectInfo>(), &lightCubeInfo);

            cubeMesh.Draw(compositionCommandBuffer);
        }

        // draw point lights
        vk.CmdBindPipeline(compositionCommandBuffer, PipelineBindPoint.Graphics, lightingPipeline.Pipeline);
        vk.CmdBindDescriptorSets(compositionCommandBuffer, PipelineBindPoint.Graphics,
                lightingPipeline.Layout, 0, 2, descriptorSets, 0, default);

        sphereMesh.Bind(compositionCommandBuffer);
        for (int i = 0; i < Lights.Count(); i++)
        {
            var light = Lights[i];
            var lightInfo = light.ToInfo();
            var shadowDescriptorSet = pointShadowDescriptorSets[i][currentFrame];
            
            vk.CmdBindDescriptorSets(compositionCommandBuffer, PipelineBindPoint.Graphics,
                    lightingPipeline.Layout, 2, 1, &shadowDescriptorSet, 0, default);
            vk.CmdPushConstants(compositionCommandBuffer, lightingPipeline.Layout,
                                ShaderStageFlags.VertexBit, 0,
                                (uint) Unsafe.SizeOf<LightInfo>(), &lightInfo);

            sphereMesh.Draw(compositionCommandBuffer);
        }
        compositionRenderStage.EndRenderPass(currentFrame);
        compositionRenderStage.EndCommands(currentFrame);

        // Begin bloom render passes
        // -------------------------
        bloomRenderStage1.BeginCommands(currentFrame);
        bloomRenderStage1.BeginRenderPass(currentFrame, currentFrame);
        var bloom1CommandBuffer = bloomRenderStage1.GetCommandBuffer(currentFrame);

        var bloom1DescriptorSet = stackalloc[] { thresholdTextureDescriptorSets[currentFrame] };
        vk.CmdBindPipeline(bloom1CommandBuffer, PipelineBindPoint.Graphics, bloom1Pipeline.Pipeline);
        vk.CmdBindDescriptorSets(bloom1CommandBuffer, PipelineBindPoint.Graphics,
                bloom1Pipeline.Layout, 0, 1, bloom1DescriptorSet, 0, default);
        bool horizontal = true;
        vk.CmdPushConstants(bloom1CommandBuffer, bloom1Pipeline.Layout,
                            ShaderStageFlags.FragmentBit, 0,
                            4, &horizontal);

        screenQuadMesh.Bind(bloom1CommandBuffer);
        screenQuadMesh.Draw(bloom1CommandBuffer);

        bloomRenderStage1.EndRenderPass(currentFrame);
        bloomRenderStage1.EndCommands(currentFrame);

        bloomRenderStage2.BeginCommands(currentFrame);
        bloomRenderStage2.BeginRenderPass(currentFrame, currentFrame);
        var bloom2CommandBuffer = bloomRenderStage2.GetCommandBuffer(currentFrame);

        var bloom2DescriptorSet = stackalloc[] { bloomPass1OutputTextureDescriptorSets[currentFrame] };
        vk.CmdBindPipeline(bloom2CommandBuffer, PipelineBindPoint.Graphics, bloom2Pipeline.Pipeline);
        vk.CmdBindDescriptorSets(bloom2CommandBuffer, PipelineBindPoint.Graphics,
                bloom2Pipeline.Layout, 0, 1, bloom2DescriptorSet, 0, default);
        horizontal = false;
        vk.CmdPushConstants(bloom2CommandBuffer, bloom2Pipeline.Layout,
                            ShaderStageFlags.FragmentBit, 0,
                            4, &horizontal);

        screenQuadMesh.Bind(bloom2CommandBuffer);
        screenQuadMesh.Draw(bloom2CommandBuffer);

        bloomRenderStage2.EndRenderPass(currentFrame);
        bloomRenderStage2.EndCommands(currentFrame);

        // Begin post processing render pass
        // ---------------------------------
        postProcessRenderStage.BeginCommands(currentFrame);
        postProcessRenderStage.BeginRenderPass(currentFrame, imageIndex);
        var postProcessCommandBuffer = postProcessRenderStage.GetCommandBuffer(currentFrame);

        var postProcessDescriptorSets = stackalloc[] { compositionOutputTextureDescriptorSets[currentFrame], bloomPass2OutputTextureDescriptorSets[currentFrame] };
        vk.CmdBindPipeline(postProcessCommandBuffer, PipelineBindPoint.Graphics, postProcessPipeline.Pipeline);
        vk.CmdBindDescriptorSets(postProcessCommandBuffer, PipelineBindPoint.Graphics,
                postProcessPipeline.Layout, 0, 2, postProcessDescriptorSets, 0, default);

        screenQuadMesh.Bind(postProcessCommandBuffer);
        screenQuadMesh.Draw(postProcessCommandBuffer);

        postProcessRenderStage.EndRenderPass(currentFrame);
        postProcessRenderStage.EndCommands(currentFrame);

        // Submit commands
        // ---------------
        List<SubmitInfo> submitInfos = new();

        // submit geometry and directional light depth map commands
        submitInfos.Add(CreateGraphicsSubmitInfo(geomCommandBuffers.ToArray(),
                                                 new[] { imageAvailableSemaphores[currentFrame] },
                                                 new[] { geomSemaphores[currentFrame] }));

        // submit composition commands
        submitInfos.Add(CreateGraphicsSubmitInfo(new[] { compositionCommandBuffer },
                                                 new[] { geomSemaphores[currentFrame] },
                                                 new[] { compSemaphores[currentFrame] }));

        // submit bloom commands
        submitInfos.Add(CreateGraphicsSubmitInfo(new[] { bloom1CommandBuffer },
                                                 new[] { compSemaphores[currentFrame] },
                                                 new[] { bloom1Semaphores[currentFrame] }));
        submitInfos.Add(CreateGraphicsSubmitInfo(new[] { bloom2CommandBuffer },
                                                 new[] { bloom1Semaphores[currentFrame] },
                                                 new[] { bloom2Semaphores[currentFrame] }));

        // submit post process commands
        submitInfos.Add(CreateGraphicsSubmitInfo(new[] { postProcessCommandBuffer },
                                                 new[] { bloom2Semaphores[currentFrame] },
                                                 new[] { postProcessSemaphores[currentFrame] }));

        SubmitInfo[] submits = submitInfos.ToArray();
        fixed (SubmitInfo* pSubmits = submits)
        {
            Result res = vk.QueueSubmit(SCDevice.GraphicsQueue, (uint) submits.Length,
                                        pSubmits, inFlightFences[currentFrame]);
            if (res != Result.Success)
            {
                throw new Exception("Unable to submit graphics queue");
            }
        }

        var swapchains = stackalloc[] { SCDevice.SwapchainInfo.Swapchain };
        var postProcessSignalSemaphores = stackalloc[] { postProcessSemaphores[currentFrame] };

        uint idx = imageIndex;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = postProcessSignalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapchains,
            PImageIndices = &idx
        };

        var result = SCDevice.SwapchainInfo.KhrSwapchain.QueuePresent(SCDevice.PresentQueue, in presentInfo);
        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || framebufferResized)
        {
            framebufferResized = false;
            RecreateSwapchain();
        }
        else if (result != Result.Success)
        {
            throw new Exception("Failed to present swapchain image!");
        }

        currentFrame = (currentFrame + 1) % MaxFramesInFlight;
    }

    public void DrawModel(Model model, Matrix4X4<float> modelMatrix)
    {
        solidModelDrawCalls.Add(new(model, modelMatrix, Vector3D<float>.Zero));
    }

    public void UpdateSceneInfo(SceneInfo sceneInfo)
    {
        this.sceneInfo = sceneInfo;

        // update scene info buffer
        {
            void* data;
            vk.MapMemory(SCDevice.LogicalDevice, sceneInfoBuffersMemory[currentFrame], 0, (ulong) Unsafe.SizeOf<SceneInfo>(), 0, &data);
            new Span<SceneInfo>(data, 1)[0] = sceneInfo;
            vk.UnmapMemory(SCDevice.LogicalDevice, sceneInfoBuffersMemory[currentFrame]);
        }
    }

    void OnFramebufferResize(Vector2D<int> framebufferSize)
    {
        Console.WriteLine("Resize");
        framebufferResized = true;
    }

    Cubemap CreateSkyboxCubemap()
    {
        // Create descriptor sets
        Matrix4X4<float>[] viewMatrices = new Matrix4X4<float>[]
        {
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitX, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitX, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitY, Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitY, -Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitZ, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitZ, -Vector3D<float>.UnitY),
        };
        Matrix4X4<float> projectionMatrix = Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, 0.1f, 100.0f);

        (Buffer[] uniformBuffers, DeviceMemory[] uniformBuffersMemory) = VulkanHelper.CreateUniformBuffers(SCDevice, (ulong) Unsafe.SizeOf<SceneInfo>(), 6);
        for (int i = 0; i < 6; i++)
        {
            SceneInfo sceneInfo = new()
            {
                CameraView = viewMatrices[i],
                CameraProjection = projectionMatrix
            };

            void* data;
            vk.MapMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], 0, (ulong) Unsafe.SizeOf<SceneInfo>(), MemoryMapFlags.None, &data);
            new Span<SceneInfo>(data, 1)[0] = sceneInfo;
            vk.UnmapMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i]);
        }
        DescriptorSet[] sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets(uniformBuffers);
        var skyboxTexture = new Texture(SCDevice, SkyboxTexturePath);
        DescriptorSet equirectangularMapDescriptorSet = CreateSingleTextureDescriptorSets(new ImageView[]{ skyboxTexture.TextureImageView }, textureSampler)[0];

        // Begin equirectangular to cubemap render pass
        equirectangularToCubemapRenderStage.ResetCommandBuffer(0);
        equirectangularToCubemapRenderStage.BeginCommands(0);
        var cubemapCommandBuffer = equirectangularToCubemapRenderStage.GetCommandBuffer(0);

        for (uint i = 0; i < 6; i++)
        {
            equirectangularToCubemapRenderStage.BeginRenderPass(0, i);

            vk.CmdBindPipeline(cubemapCommandBuffer, PipelineBindPoint.Graphics, equirectangularToCubemapPipeline.Pipeline);
            
            var sceneInfoDescriptorSet = sceneInfoDescriptorSets[i];
            DescriptorSet* descriptorSets = stackalloc[] { sceneInfoDescriptorSet, equirectangularMapDescriptorSet };
            vk.CmdBindDescriptorSets(cubemapCommandBuffer, PipelineBindPoint.Graphics,
                    equirectangularToCubemapPipeline.Layout, 0, 2, descriptorSets, 0, default);

            cubeMesh.Bind(cubemapCommandBuffer);
            cubeMesh.Draw(cubemapCommandBuffer);

            equirectangularToCubemapRenderStage.EndRenderPass(0);
        }

        equirectangularToCubemapRenderStage.EndCommands(0);
        var submitInfo = CreateGraphicsSubmitInfo(new[] { cubemapCommandBuffer },
                                                  new Semaphore[]{}, new Semaphore[]{});
        if (vk.QueueSubmit(SCDevice.GraphicsQueue, 1, &submitInfo, default) != Result.Success)
        {
            throw new Exception("Failed to submit graphics queue!");
        }
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        Image[] environmentMapImages = environmentMapAttachments.Select((attachment) => attachment.Color.Image).ToArray();
        var environmentCubemap = SCDevice.ImagesToCubeMap(environmentMapImages, new Extent2D() { Width = 512, Height = 512 });

        // free resources
        vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, 1, &equirectangularMapDescriptorSet);

        fixed (DescriptorSet* descriptorSetsPtr = sceneInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, uniformBufferDescriptorPool, 6, descriptorSetsPtr);

        for (int i = 0; i < 6; i++)
        {
            vk.DestroyBuffer(SCDevice.LogicalDevice, uniformBuffers[i], null);
            vk.FreeMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], null);
        }

        return environmentCubemap;
    }

    Cubemap CreateIrradianceCubemap(Cubemap environmentCubemap)
    {
        // Create descriptor sets
        Matrix4X4<float>[] viewMatrices = new Matrix4X4<float>[]
        {
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitX, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitX, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitY, Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitY, -Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitZ, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitZ, -Vector3D<float>.UnitY),
        };
        Matrix4X4<float> projectionMatrix = Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, 0.1f, 10.0f);

        (Buffer[] uniformBuffers, DeviceMemory[] uniformBuffersMemory) = VulkanHelper.CreateUniformBuffers(SCDevice, (ulong) Unsafe.SizeOf<SceneInfo>(), 6);
        for (int i = 0; i < 6; i++)
        {
            SceneInfo sceneInfo = new()
            {
                CameraView = viewMatrices[i],
                CameraProjection = projectionMatrix
            };

            void* data;
            vk.MapMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], 0, (ulong) Unsafe.SizeOf<SceneInfo>(), MemoryMapFlags.None, &data);
            new Span<SceneInfo>(data, 1)[0] = sceneInfo;
            vk.UnmapMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i]);
        }
        DescriptorSet[] sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets(uniformBuffers);

        var environmentMapDescriptorSet = CreateSingleTextureDescriptorSets(new ImageView[]{ environmentCubemap.CubemapImageView }, textureSampler)[0];

        // Begin irradiance map render pass
        irradianceMapRenderStage.ResetCommandBuffer(0);
        irradianceMapRenderStage.BeginCommands(0);
        var irradianceCommandBuffer = irradianceMapRenderStage.GetCommandBuffer(0);

        for (uint i = 0; i < 6; i++)
        {
            irradianceMapRenderStage.BeginRenderPass(0, i);

            vk.CmdBindPipeline(irradianceCommandBuffer, PipelineBindPoint.Graphics, irradianceMapPipeline.Pipeline);
            cubeMesh.Bind(irradianceCommandBuffer);

            var sceneInfoDescriptorSet = sceneInfoDescriptorSets[i];
            DescriptorSet* descriptorSets = stackalloc[] { sceneInfoDescriptorSet, environmentMapDescriptorSet };
            vk.CmdBindDescriptorSets(irradianceCommandBuffer, PipelineBindPoint.Graphics,
                    irradianceMapPipeline.Layout, 0, 2, descriptorSets, 0, default);

            cubeMesh.Draw(irradianceCommandBuffer);

            irradianceMapRenderStage.EndRenderPass(0);
        }

        irradianceMapRenderStage.EndCommands(0);
        var submitInfo = CreateGraphicsSubmitInfo(new[] { irradianceCommandBuffer },
                                                  new Semaphore[]{}, new Semaphore[]{});
        if (vk.QueueSubmit(SCDevice.GraphicsQueue, 1, &submitInfo, default) != Result.Success)
        {
            throw new Exception("Failed to submit graphics queue!");
        }
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        Image[] irradianceMapImages = irradianceMapAttachments.Select((attachment) => attachment.Color.Image).ToArray();
        var irradianceMap = SCDevice.ImagesToCubeMap(irradianceMapImages, new Extent2D() { Width = 32, Height = 32 });

        // free resources
        vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, 1, &environmentMapDescriptorSet);

        fixed (DescriptorSet* descriptorSetsPtr = sceneInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, uniformBufferDescriptorPool, 6, descriptorSetsPtr);

        for (int i = 0; i < 6; i++)
        {
            vk.DestroyBuffer(SCDevice.LogicalDevice, uniformBuffers[i], null);
            vk.FreeMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], null);
        }

        return irradianceMap;
    }

    Cubemap CreatePrefilteredCubemap(Cubemap environmentCubemap)
    {
        // Create render stages and pipeline
        uint maxMipLevels = 5;
        RenderStage[] renderStages;
        SingleColorAttachment[][] cubemapAttachments;
        (renderStages, cubemapAttachments) = CreatePrefilteredCubemapRenderStages(maxMipLevels);
        GraphicsPipeline prefilterPipeline = CreatePrefilterPipeline(renderStages[0].RenderPass);

        // Create descriptor sets
        Matrix4X4<float>[] viewMatrices = new Matrix4X4<float>[]
        {
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitX, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitX, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitY, Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitY, -Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitZ, -Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitZ, -Vector3D<float>.UnitY),
        };
        Matrix4X4<float> projectionMatrix = Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, 0.1f, 10.0f);

        (Buffer[] uniformBuffers, DeviceMemory[] uniformBuffersMemory) = VulkanHelper.CreateUniformBuffers(SCDevice, (ulong) Unsafe.SizeOf<SceneInfo>(), 6);
        for (int i = 0; i < 6; i++)
        {
            SceneInfo sceneInfo = new()
            {
                CameraView = viewMatrices[i],
                CameraProjection = projectionMatrix
            };

            void* data;
            vk.MapMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], 0, (ulong) Unsafe.SizeOf<SceneInfo>(), MemoryMapFlags.None, &data);
            new Span<SceneInfo>(data, 1)[0] = sceneInfo;
            vk.UnmapMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i]);
        }
        DescriptorSet[] sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets(uniformBuffers);

        var environmentMapDescriptorSet = CreateSingleTextureDescriptorSets(new ImageView[]{ environmentCubemap.CubemapImageView }, textureSampler)[0];
        
        List<CommandBuffer> commandBuffers = new();
        for (uint mipLevel = 0; mipLevel < maxMipLevels; mipLevel++)
        {
            var renderStage = renderStages[mipLevel];

            // Begin irradiance map render pass
            renderStage.BeginCommands(0);
            var commandBuffer = renderStage.GetCommandBuffer(0);

            for (uint face = 0; face < 6; face++)
            {
                renderStage.BeginRenderPass(0, face);

                vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, prefilterPipeline.Pipeline);
                cubeMesh.Bind(commandBuffer);

                var sceneInfoDescriptorSet = sceneInfoDescriptorSets[face];
                DescriptorSet* descriptorSets = stackalloc[] { sceneInfoDescriptorSet, environmentMapDescriptorSet };
                vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics,
                        prefilterPipeline.Layout, 0, 2, descriptorSets, 0, default);
                float roughness = (float) mipLevel / (float) (maxMipLevels - 1);
                vk.CmdPushConstants(commandBuffer, prefilterPipeline.Layout, ShaderStageFlags.FragmentBit,
                        0, (uint) Unsafe.SizeOf<float>(), &roughness);
                cubeMesh.Draw(commandBuffer);

                renderStage.EndRenderPass(0);
            }

            renderStage.EndCommands(0);
            commandBuffers.Add(commandBuffer);
        }

        var submitInfo = CreateGraphicsSubmitInfo(commandBuffers.ToArray(),
                                                  new Semaphore[]{}, new Semaphore[]{});
        if (vk.QueueSubmit(SCDevice.GraphicsQueue, 1, &submitInfo, default) != Result.Success)
        {
            throw new Exception("Failed to submit graphics queue!");
        }
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        // create prefilter cubemap
        (Image cubemapImage, DeviceMemory cubemapMemory) = VulkanHelper.CreateCubemapImage(SCDevice,
                                                               Format.R16G16B16A16Sfloat, ImageTiling.Optimal,
                                                               ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                                                               MemoryPropertyFlags.DeviceLocalBit,
                                                               128, 128, maxMipLevels);

        SCDevice.TransitionImageLayout(cubemapImage, Format.R16G16B16A16Sfloat,
                                       ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
                                       6, maxMipLevels);
        uint mipSize = 128;
        var singleTimeCommandBuffer = BeginSingleTimeCommand();
        for (uint mipLevel = 0; mipLevel < maxMipLevels; mipLevel++)
        {
            var srcImages = cubemapAttachments[mipLevel].Select(att => att.Color.Image).ToArray();

            for (uint layer = 0; layer < 6; layer++)
            {
                ImageCopy copyRegion = new()
                {
                    SrcOffset = new() { X = 0, Y = 0, Z = 0 },
                    SrcSubresource = new()
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        LayerCount = 1,
                        BaseArrayLayer = 0
                    },
                    DstOffset = new() { X = 0, Y = 0, Z = 0 },
                    DstSubresource = new()
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        LayerCount = 1,
                        BaseArrayLayer = layer,
                        MipLevel = mipLevel
                    },
                    Extent = new() { Width = mipSize, Height = mipSize, Depth = 1 }
                };
                vk.CmdCopyImage(singleTimeCommandBuffer,
                                srcImages[layer], ImageLayout.TransferSrcOptimal,
                                cubemapImage, ImageLayout.TransferDstOptimal,
                                1, &copyRegion);
            }

            if (mipSize > 1) mipSize /= 2;
        }
        EndSingleTimeCommand(singleTimeCommandBuffer);

        SCDevice.TransitionImageLayout(cubemapImage, Format.R16G16B16A16Sfloat,
                                       ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
                                       6, maxMipLevels);

        // free resources
        vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, 1, &environmentMapDescriptorSet);

        fixed (DescriptorSet* descriptorSetsPtr = sceneInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, uniformBufferDescriptorPool, 6, descriptorSetsPtr);

        for (int i = 0; i < 6; i++)
        {
            vk.DestroyBuffer(SCDevice.LogicalDevice, uniformBuffers[i], null);
            vk.FreeMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], null);
        }

        vk.DestroyPipeline(SCDevice.LogicalDevice, prefilterPipeline.Pipeline, null);
        foreach (var renderStage in renderStages)
        {
            renderStage.Dispose();
        }

        return new Cubemap(SCDevice, cubemapImage, cubemapMemory);
    }

    ImageView CreateBRDFLUTTexture()
    {
        var commandBuffer = brdfLUTTextureRenderStage.GetCommandBuffer(0);
        brdfLUTTextureRenderStage.BeginCommands(0);
        brdfLUTTextureRenderStage.BeginRenderPass(0, 0);

        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, brdfPipeline.Pipeline);
        screenQuadMesh.Bind(commandBuffer);

        screenQuadMesh.Draw(commandBuffer);

        brdfLUTTextureRenderStage.EndRenderPass(0);
        brdfLUTTextureRenderStage.EndCommands(0);

        var submitInfo = CreateGraphicsSubmitInfo(new[] { commandBuffer },
                                                  new Semaphore[]{}, new Semaphore[]{});
        if (vk.QueueSubmit(SCDevice.GraphicsQueue, 1, &submitInfo, default) != Result.Success)
        {
            throw new Exception("Failed to submit graphics queue!");
        }
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        return brdfLUTAttachment.Color.ImageView;
    }

    void CleanupSwapchainObjects()
    {
        // Destroy pipelines
        vk.DestroyPipeline(SCDevice.LogicalDevice, postProcessPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, postProcessPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, bloom2Pipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, bloom2Pipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, bloom1Pipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, bloom1Pipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, skyboxPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, skyboxPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, solidColorPipeine.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, solidColorPipeine.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, lightingPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, lightingPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, compositionPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, compositionPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, geometryPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, geometryPipeline.Layout, null);

        // Free descriptor sets
        fixed (DescriptorSet* pDescriptorSets = sceneInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, uniformBufferDescriptorPool, (uint) sceneInfoDescriptorSets.Length, pDescriptorSets);
        fixed (DescriptorSet* pDescriptorSets = screenTextureInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, screenTextureDescriptorPool, (uint) screenTextureInfoDescriptorSets.Length, pDescriptorSets);
        fixed (DescriptorSet* pDescriptorSets = compositionOutputTextureDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, (uint) compositionOutputTextureDescriptorSets.Length, pDescriptorSets);
        fixed (DescriptorSet* pDescriptorSets = thresholdTextureDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, (uint) thresholdTextureDescriptorSets.Length, pDescriptorSets);
        fixed (DescriptorSet* pDescriptorSets = bloomPass1OutputTextureDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, (uint) bloomPass1OutputTextureDescriptorSets.Length, pDescriptorSets);
        fixed (DescriptorSet* pDescriptorSets = bloomPass2OutputTextureDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, (uint) bloomPass2OutputTextureDescriptorSets.Length, pDescriptorSets);

        // Destroy render stages
        geometryRenderStage.Dispose();
        compositionRenderStage.Dispose();
        bloomRenderStage1.Dispose();
        bloomRenderStage2.Dispose();
        postProcessRenderStage.Dispose();
    }

    void RecreateSwapchain()
    {
        Vector2D<int> framebufferSize = window.FramebufferSize;

        // wait for window to be unminimized
        while (framebufferSize.X == 0 || framebufferSize.Y == 0)
        {
            framebufferSize = window.FramebufferSize;
            window.DoEvents();
        }
        
        vk.DeviceWaitIdle(SCDevice.LogicalDevice);

        CleanupSwapchainObjects();
        SCDevice.RecreateSwapchain(window);

        // Create render stages
        (geometryRenderStage, gBufferAttachments) = CreateGeometryRenderStage();
        (compositionRenderStage, compositionAttachments) = CreateCompositionRenderStage();
        (bloomRenderStage1, bloomAttachments1) = CreateBloomRenderStage();
        (bloomRenderStage2, bloomAttachments2) = CreateBloomRenderStage();
        (postProcessRenderStage, swapChainAttachments) = CreatePostProcessRenderStage();

        // Create descriptor sets
        sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets(sceneInfoBuffers);
        screenTextureInfoDescriptorSets = CreateScreenTextureInfoDescriptorSets(gBufferAttachments);
        compositionOutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionAttachments.Select(att => att.Color.ImageView).ToArray(), textureSampler);
        thresholdTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionAttachments.Select(att => att.ThresholdedColor.ImageView).ToArray(), textureSampler);
        bloomPass1OutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(bloomAttachments1.Select(att => att.Color.ImageView).ToArray(), bloomSampler);
        bloomPass2OutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(bloomAttachments2.Select(att => att.Color.ImageView).ToArray(), bloomSampler);

        // Create pipelines
        geometryPipeline = CreateGeometryPipeline(geometryRenderStage.RenderPass);
        compositionPipeline = CreateCompositionPipeline(compositionRenderStage.RenderPass);
        lightingPipeline = CreateLightingPipeline(compositionRenderStage.RenderPass);
        solidColorPipeine = CreateSolidColorPipeline(compositionRenderStage.RenderPass);
        skyboxPipeline = CreateSkyboxPipeline(compositionRenderStage.RenderPass);
        bloom1Pipeline = CreateBloomPipeline(bloomRenderStage1.RenderPass);
        bloom2Pipeline = CreateBloomPipeline(bloomRenderStage2.RenderPass);
        postProcessPipeline = CreatePostProcessPipeline(postProcessRenderStage.RenderPass);
    }

    (RenderStage, GBufferAttachments[]) CreateGeometryRenderStage()
    {
        GBufferAttachments[] gBufferAttachments = new GBufferAttachments[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            gBufferAttachments[i] = new(SCDevice, SCDevice.SwapchainInfo.Extent);
        }

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(gBufferAttachments[0].Albedo.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBufferAttachments[0].Normal.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBufferAttachments[0].AoRoughnessMetalness.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBufferAttachments[0].Position.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(gBufferAttachments[0].Depth.Format)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.BottomOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.MemoryReadBit, AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                                        DependencyFlags.ByRegionBit)
                         .AddDependency(0, Vk.SubpassExternal,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit, AccessFlags.MemoryReadBit,
                                        DependencyFlags.ByRegionBit);
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, gBufferAttachments, MaxFramesInFlight);

        var clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 0.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, gBufferAttachments);
    }

    (RenderStage, CompositionAttachments[]) CreateCompositionRenderStage()
    {
        CompositionAttachments[] compositionAttachments = new CompositionAttachments[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            compositionAttachments[i] = new(SCDevice, SCDevice.SwapchainInfo.Extent);
        }

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(compositionAttachments[0].Color.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(compositionAttachments[0].ThresholdedColor.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(compositionAttachments[0].Depth.Format)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
                                        DependencyFlags.None)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.None, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
                                        DependencyFlags.None);
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, compositionAttachments, MaxFramesInFlight);

        var clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, compositionAttachments);
    }

    (RenderStage, BloomAttachments[]) CreateBloomRenderStage()
    {
        BloomAttachments[] bloomAttachments = new BloomAttachments[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            bloomAttachments[i] = new(SCDevice, SCDevice.SwapchainInfo.Extent);
        }

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(bloomAttachments[0].Color.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
                                        DependencyFlags.None)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.None, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
                                        DependencyFlags.None);
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, bloomAttachments, MaxFramesInFlight);

        var clearColors = new ClearValue[]
        {
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, bloomAttachments);
    }

    (RenderStage, SwapChainAttachment[]) CreatePostProcessRenderStage()
    {
        int swapchainImageCount = SCDevice.SwapchainInfo.ImageViews.Length;
        SwapChainAttachment[] swapChainAttachments = new SwapChainAttachment[swapchainImageCount];
        for (int i = 0; i < swapchainImageCount; i++)
        {
            swapChainAttachments[i] = new SwapChainAttachment(SCDevice, SCDevice.SwapchainInfo.ImageViews[i], SCDevice.SwapchainInfo.ImageFormat);
        }

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(SCDevice.SwapchainInfo.ImageFormat, ImageLayout.PresentSrcKhr)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
                                        DependencyFlags.None)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.None, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
                                        DependencyFlags.None);
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, swapChainAttachments, MaxFramesInFlight);

        var clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, swapChainAttachments);
    }

    (RenderStage, SingleColorAttachment[]) CreateEquirectangularToCubemapRenderStage()
    {
        SingleColorAttachment[] cubefaceAttachments = new SingleColorAttachment[6];
        for (int i = 0; i < 6; i++)
        {
            cubefaceAttachments[i] = new SingleColorAttachment(SCDevice, Format.R16G16B16A16Sfloat, new Extent2D(512, 512));
        }

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(Format.R16G16B16A16Sfloat, ImageLayout.TransferSrcOptimal)
                         .SetDepthStencilAttachment(cubefaceAttachments[0].Depth.Format);
        
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, cubefaceAttachments, 1);

        var clearColors = new ClearValue[]
        {
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, cubefaceAttachments);
    }

    (RenderStage, SingleColorAttachment[]) CreateIrradianceMapRenderStage()
    {
        SingleColorAttachment[] cubefaceAttachments = new SingleColorAttachment[6];
        for (int i = 0; i < 6; i++)
        {
            cubefaceAttachments[i] = new SingleColorAttachment(SCDevice, Format.R16G16B16A16Sfloat, new Extent2D(32, 32));
        }

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(Format.R16G16B16A16Sfloat, ImageLayout.TransferSrcOptimal)
                         .SetDepthStencilAttachment(cubefaceAttachments[0].Depth.Format);
        
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, cubefaceAttachments, 1);

        var clearColors = new ClearValue[]
        {
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, cubefaceAttachments);
    }

    (RenderStage, SingleColorAttachment) CreateBRDFLUTTextureRenderStage()
    {
        SingleColorAttachment brdfLUTAttachment =  new(SCDevice, Format.R16G16Sfloat, new Extent2D(512, 512));

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(Format.R16G16Sfloat, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(brdfLUTAttachment.Depth.Format);

        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, new[]{ brdfLUTAttachment }, 1);

        var clearColors = new ClearValue[]
        {
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, brdfLUTAttachment);
    }

    (RenderStage[], SingleColorAttachment[][]) CreatePrefilteredCubemapRenderStages(uint mipLevels)
    {
        RenderStage[] renderStages = new RenderStage[mipLevels];
        SingleColorAttachment[][] cubemapAttachments = new SingleColorAttachment[mipLevels][];

        uint mipResolution = 128;
        for (uint mipLevel = 0; mipLevel < mipLevels; mipLevel++)
        {
            RenderPassBuilder renderPassBuilder = new(SCDevice);
            renderPassBuilder.AddColorAttachment(Format.R16G16B16A16Sfloat, ImageLayout.TransferSrcOptimal)
                             .SetDepthStencilAttachment(VulkanHelper.FindDepthFormat(SCDevice));
            RenderPass renderPass = renderPassBuilder.Build();

            cubemapAttachments[mipLevel] = new SingleColorAttachment[6];
            for (uint face = 0; face < 6; face++)
            {
                cubemapAttachments[mipLevel][face] = new(SCDevice, Format.R16G16B16A16Sfloat, new Extent2D(mipResolution, mipResolution));
            }
            renderStages[mipLevel] = new(SCDevice, renderPass, cubemapAttachments[mipLevel], 1);

            var clearColors = new ClearValue[]
            {
                new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
                new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
            };
            renderStages[mipLevel].ClearValues.AddRange(clearColors);

            if (mipResolution > 1) mipResolution /= 2;
        }

        return (renderStages, cubemapAttachments);
    }

    (RenderStage, DepthOnlyAttachment[]) CreateDepthMapRenderStage()
    {
        var shadowMapExtent = new Extent2D{ Width = ShadowMapResolution, Height = ShadowMapResolution };
        DepthOnlyAttachment[] depthAttachments = new DepthOnlyAttachment[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            depthAttachments[i] = new(SCDevice, shadowMapExtent);
        }
        
        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.SetDepthStencilAttachment(VulkanHelper.FindDepthFormat(SCDevice), ImageLayout.ShaderReadOnlyOptimal)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                                        AccessFlags.DepthStencilAttachmentWriteBit, AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
                                        DependencyFlags.None)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.None, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
                                        DependencyFlags.None);
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, depthAttachments, MaxFramesInFlight);

        var clearColors = new ClearValue[]
        {
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, depthAttachments);
    }

    (RenderStage, DepthCubeMapOnlyAttachment[]) CreatePointShadowRenderStage()
    {
        var shadowMapExtent = new Extent2D{ Width = PointShadowMapResolution,
                                            Height = PointShadowMapResolution };
        DepthCubeMapOnlyAttachment[] depthAttachments = new DepthCubeMapOnlyAttachment[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            depthAttachments[i] = new(SCDevice, shadowMapExtent);
        }

        RenderStage renderStage = new(SCDevice, pointShadowMapRenderPass, depthAttachments, 6, MaxFramesInFlight);

        var clearColors = new ClearValue[]
        {
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, depthAttachments);
    }

    void BindMaterial(Material material)
    {
        var geometryCommandBuffer = geometryRenderStage.GetCommandBuffer(currentFrame);

        vk.CmdBindDescriptorSets(geometryCommandBuffer, PipelineBindPoint.Graphics,
                                 geometryPipeline.Layout, 1, 1, in material.DescriptorSets[currentFrame], 0, default);
    }

    CommandBuffer BeginSingleTimeCommand()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = SCDevice.CommandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        vk.AllocateCommandBuffers(SCDevice.LogicalDevice, in allocInfo, out commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        vk.BeginCommandBuffer(commandBuffer, in beginInfo);
        return commandBuffer;
    }

    void EndSingleTimeCommand(CommandBuffer commandBuffer)
    {
        vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        vk.QueueSubmit(SCDevice.GraphicsQueue, 1, in submitInfo, default);
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        vk.FreeCommandBuffers(SCDevice.LogicalDevice, SCDevice.CommandPool, 1, in commandBuffer);
    }

    SubmitInfo CreateGraphicsSubmitInfo(CommandBuffer[] commandBuffers, Semaphore[] waitSemaphores,
                                        Semaphore[] signalSemaphores)
    {
        var waitStages = new PipelineStageFlags[waitSemaphores.Length];
        Array.Fill(waitStages, PipelineStageFlags.ColorAttachmentOutputBit);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = (uint) commandBuffers.Length,
            SignalSemaphoreCount = (uint) signalSemaphores.Length,
            WaitSemaphoreCount = (uint) waitSemaphores.Length,
        };
        fixed (CommandBuffer* pCommandBuffers = commandBuffers)
        fixed (Semaphore* pSignalSemaphores = signalSemaphores)
        fixed (Semaphore* pWaitSemaphores = waitSemaphores)
        fixed (PipelineStageFlags* pWaitStages = waitStages)
        {
            submitInfo.PCommandBuffers = pCommandBuffers;
            submitInfo.PSignalSemaphores = pSignalSemaphores;
            submitInfo.PWaitSemaphores = pWaitSemaphores;
            submitInfo.PWaitDstStageMask = pWaitStages;
        }

        return submitInfo;
    }

    // IDisposable Methods
    // -------------------

    ~VulkanRenderer()
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
        vk.DeviceWaitIdle(SCDevice.LogicalDevice);

        if (!disposedValue)
        {
            // free unmanaged resources unmanaged objects and override finalizer
            for (int i = 0; i < MaxFramesInFlight; i++)
            {
                vk.DestroySemaphore(SCDevice.LogicalDevice, imageAvailableSemaphores[i], null);
                vk.DestroyFence(SCDevice.LogicalDevice, inFlightFences[i], null);
            }

            vk.DestroySampler(SCDevice.LogicalDevice, textureSampler, null);

            CleanupSwapchainObjects();


            vk.DestroyCommandPool(SCDevice.LogicalDevice, SCDevice.CommandPool, null);

            for (int i = 0; i < MaxFramesInFlight; i++)
            {
                vk.DestroyBuffer(SCDevice.LogicalDevice, sceneInfoBuffers[i], null);
                vk.FreeMemory(SCDevice.LogicalDevice, sceneInfoBuffersMemory[i], null);
            }

            screenQuadMesh.Dispose();
            sphereMesh.Dispose();
            cubeMesh.Dispose();

            SCDevice.Dispose();

            disposedValue = true;
        }
    }
}

