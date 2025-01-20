using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
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
public struct ShadowInfo
{
    [FieldOffset(0)]public Matrix4X4<float> LightSpaceMatrix;
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

    // TODO: temporary to test lights
    public List<Light> Lights = new List<Light>();

    readonly bool EnableValidationLayers;
    const int MaxFramesInFlight = 2;
    const int MaxLights = 128;
    const int CubemapMapSceneInfoDescriptors = 6;
    const uint MaxGBufferDescriptorSets = 20;

    // Very Low Resolution: 256x256 => 0.25Mb per face => 1.5Mb per cubemap => 192Mb for 128 lights
    // Low Resolution: 512x512 => 1Mb per face => 6Mb per cubemap => 0.75Gb for 128 lights
    // Medium Resolution: 1024x1024 => 4Mb per face => 24Mb per cubemap => 3Gb for 128 lights
    // High Resolution : 2048x2048 => 16Mb per face => 96Mb per cubemap => 12Gb for 128 lights
    const uint ShadowMapResolution = 1024;

    const string SphereMeshPath = AssetsPath + "models/sphere/sphere.glb";
    const string SkyboxTexturePath = AssetsPath + "hdris/EveningSkyHDRI.jpg";

    readonly string[] validationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName,
        KhrDynamicRendering.ExtensionName
    };

    Mesh screenQuadMesh;
    Mesh cubeMesh;
    Mesh sphereMesh;
    SceneInfo sceneInfo;

    readonly Vk vk = VulkanHelper.Vk;
    IWindow window;
    public SCDevice SCDevice;

    GBufferAttachments gBufferAttachments;
    CompositionAttachments compositionAttachments;
    BloomAttachments bloomAttachments1;
    BloomAttachments bloomAttachments2;
    SwapChainAttachment[] swapChainAttachments;
    SingleColorAttachment[] environmentMapAttachments;
    SingleColorAttachment[] irradianceMapAttachments;
    DepthOnlyAttachment depthMapAttachment;

    RenderStage geometryRenderStage;
    RenderStage compositionRenderStage;
    RenderStage bloomRenderStage1;
    RenderStage bloomRenderStage2;
    RenderStage postProcessRenderStage;
    RenderStage equirectangularToCubemapRenderStage;
    RenderStage irradianceMapRenderStage;
    RenderStage depthMapRenderStage;

    Buffer[] sceneInfoBuffers;
    DeviceMemory[] sceneInfoBuffersMemory;

    DescriptorSetLayout sceneInfoDescriptorSetLayout;
    DescriptorSetLayout materialInfoDescriptorSetLayout;
    DescriptorSetLayout screenTextureDescriptorSetLayout;
    DescriptorSetLayout singleTextureDescriptorSetLayout;

    DescriptorPool sceneInfoDescriptorPool;
    public DescriptorPool materialInfoDescriptorPool;
    DescriptorPool screenTextureDescriptorPool;
    DescriptorPool singleTextureDescriptorPool;

    DescriptorSet[] sceneInfoDescriptorSets;
    DescriptorSet[] screenTextureInfoDescriptorSets;
    DescriptorSet[] compositionOutputTextureDescriptorSets;
    DescriptorSet[] thresholdTextureDescriptorSets;
    DescriptorSet[] dirShadowMapDescriptorSets;
    DescriptorSet[] bloomPass1OutputTextureDescriptorSets;
    DescriptorSet[] bloomPass2OutputTextureDescriptorSets;
    DescriptorSet skyboxTextureDescriptorSet;
    DescriptorSet irradianceMapDescriptorSet;

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
    GraphicsPipeline depthPipeline;

    Sampler textureSampler;

    Semaphore[] imageAvailableSemaphores;
    Fence[] inFlightFences;

    Cubemap skyboxCubemap;
    Cubemap irradianceCubemap;

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
        textureSampler = VulkanHelper.CreateTextureSampler(SCDevice);

        // Create descriptor pools
        sceneInfoDescriptorPool = CreateSceneInfoDescriptorPool(MaxFramesInFlight + CubemapMapSceneInfoDescriptors);
        materialInfoDescriptorPool = CreateMaterialInfoDescriptorPool(MaxGBufferDescriptorSets);
        screenTextureDescriptorPool = CreateScreenTextureInfoDescriptorPool();
        singleTextureDescriptorPool = CreateSingleTextureDescriptorPool(20);

        // Create descriptor set layouts
        sceneInfoDescriptorSetLayout = CreateSceneInfoDescriptorSetLayout();
        materialInfoDescriptorSetLayout = CreateMaterialInfoDescriptorSetLayout();
        screenTextureDescriptorSetLayout = CreateScreenTexureInfoDescriptorSetLayout();
        singleTextureDescriptorSetLayout = CreateSingleTextureDescriptorSetLayout();

        // Create render stages
        (geometryRenderStage, gBufferAttachments) = CreateGeometryRenderStage();
        (compositionRenderStage, compositionAttachments) = CreateCompositionRenderStage();
        (bloomRenderStage1, bloomAttachments1) = CreateBloomRenderStage();
        (bloomRenderStage2, bloomAttachments2) = CreateBloomRenderStage();
        (postProcessRenderStage, swapChainAttachments) = CreatePostProcessRenderStage();
        (equirectangularToCubemapRenderStage, environmentMapAttachments) = CreateEquirectangularToCubemapRenderStage();
        (irradianceMapRenderStage, irradianceMapAttachments) = CreateIrradianceMapRenderStage();
        (depthMapRenderStage, depthMapAttachment) = CreateDepthMapRenderStage();

        // Create descriptor sets
        sceneInfoDescriptorSets = CreateSceneInfoDescriptorSets(sceneInfoBuffers);
        screenTextureInfoDescriptorSets = CreateScreenTextureInfoDescriptorSets(gBufferAttachments.Albedo.ImageView,
                                                                                gBufferAttachments.Normal.ImageView,
                                                                                gBufferAttachments.AoRoughnessMetalness.ImageView,
                                                                                gBufferAttachments.Position.ImageView);
        compositionOutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionAttachments.Color.ImageView, MaxFramesInFlight);
        thresholdTextureDescriptorSets = CreateSingleTextureDescriptorSets(compositionAttachments.ThresholdedColor.ImageView, MaxFramesInFlight);
        dirShadowMapDescriptorSets = CreateSingleTextureDescriptorSets(depthMapAttachment.Depth.ImageView, MaxFramesInFlight);
        bloomPass1OutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(bloomAttachments1.Color.ImageView, MaxFramesInFlight);
        bloomPass2OutputTextureDescriptorSets = CreateSingleTextureDescriptorSets(bloomAttachments2.Color.ImageView, MaxFramesInFlight);

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
        depthPipeline = CreateDepthPipeline(depthMapRenderStage.RenderPass);

        CreateSyncObjects(out imageAvailableSemaphores, out inFlightFences);

        // Load primitive meshes
        screenQuadMesh = PrimitiveMesh.CreateQuadMesh(this);
        cubeMesh = PrimitiveMesh.CreateCubeMesh(this);
        sphereMesh = new Mesh(this, SphereMeshPath);

        // Generate irradiance cubemap
        skyboxCubemap = CreateSkyboxCubemap();
        irradianceCubemap = CreateIrradianceCubemap(skyboxCubemap);

        // Create skybox descriptor sets
        skyboxTextureDescriptorSet = CreateSingleTextureDescriptorSets(skyboxCubemap.CubemapImageView, 1)[0];
        irradianceMapDescriptorSet = CreateSingleTextureDescriptorSets(irradianceCubemap.CubemapImageView, 1)[0];

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

        solidModelDrawCalls.Clear();
        transparentModelDrawCalls.Clear();
    }

    public void EndFrame()
    {
        if (!isFrameEnded) throw new Exception("Tried to end frame before beginning a new one!");

        // Begin Geometry Render Pass
        // --------------------------
        var geometryCommandBuffer = geometryRenderStage.GetCommandBuffer(currentFrame);
        geometryRenderStage.BeginCommands(currentFrame);
        geometryRenderStage.BeginRenderPass(currentFrame, 0);
        
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

        // Begin directional light shadow render pass
        // ------------------------------------------
        depthMapRenderStage.BeginCommands(currentFrame);
        depthMapRenderStage.BeginRenderPass(currentFrame, 0);
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

        // Begin composition render pass
        // -----------------------------
        compositionRenderStage.BeginCommands(currentFrame);
        compositionRenderStage.BeginRenderPass(currentFrame, 0);
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
            irradianceMapDescriptorSet,
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
        foreach (var light in Lights)
        {
            var lightInfo = light.ToInfo();
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
        bloomRenderStage1.BeginRenderPass(currentFrame, 0);
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
        bloomRenderStage2.BeginRenderPass(currentFrame, 0);
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

        // submit geometry commands
        var geomWaitSemaphores = new Semaphore[] { imageAvailableSemaphores[currentFrame] };
        geometryRenderStage.SubmitCommands(SCDevice.GraphicsQueue, currentFrame, geomWaitSemaphores);

        // submit directional light depth map commands
        var depthMapWaitSemaphores = new Semaphore[] { geometryRenderStage.GetSignalSemaphore(currentFrame) };
        depthMapRenderStage.SubmitCommands(SCDevice.GraphicsQueue, currentFrame, depthMapWaitSemaphores);

        // submit composition commands
        var compWaitSemaphores = new Semaphore[] { depthMapRenderStage.GetSignalSemaphore(currentFrame) };
        compositionRenderStage.SubmitCommands(SCDevice.GraphicsQueue, currentFrame, compWaitSemaphores);

        // submit bloom commands
        var bloom1WaitSemaphores = new Semaphore[] { compositionRenderStage.GetSignalSemaphore(currentFrame) };
        var bloom2WaitSemaphores = new Semaphore[] { bloomRenderStage1.GetSignalSemaphore(currentFrame) };
        bloomRenderStage1.SubmitCommands(SCDevice.GraphicsQueue, currentFrame, bloom1WaitSemaphores);
        bloomRenderStage2.SubmitCommands(SCDevice.GraphicsQueue, currentFrame, bloom2WaitSemaphores);

        // submit post process commands
        var postProcessWaitSemaphores = new Semaphore[] { bloomRenderStage2.GetSignalSemaphore(currentFrame) };
        postProcessRenderStage.SubmitCommands(SCDevice.GraphicsQueue, currentFrame, postProcessWaitSemaphores, inFlightFences[currentFrame]);

        var swapchains = stackalloc[] { SCDevice.SwapchainInfo.Swapchain };
        var postProcessSignalSemaphores = stackalloc[] { postProcessRenderStage.GetSignalSemaphore(currentFrame) };

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

    public void DeviceWaitIdle()
    {
        vk.DeviceWaitIdle(SCDevice.LogicalDevice);
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
        framebufferResized = true;
    }

    Cubemap CreateSkyboxCubemap()
    {
        // Create descriptor sets
        Matrix4X4<float>[] viewMatrices = new Matrix4X4<float>[]
        {
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitX, Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitX, Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitY, Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitY, -Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitZ, Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitZ, Vector3D<float>.UnitY),
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
        DescriptorSet equirectangularMapDescriptorSet = CreateSingleTextureDescriptorSets(skyboxTexture.TextureImageView, 1)[0];

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
        equirectangularToCubemapRenderStage.SubmitCommands(SCDevice.GraphicsQueue, 0, new Semaphore[] {});
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        Image[] environmentMapImages = environmentMapAttachments.Select((attachment) => attachment.Color.Image).ToArray();
        var environmentCubemap = SCDevice.ImagesToCubeMap(environmentMapImages, new Extent2D() { Width = 512, Height = 512 });

        // free resources
        vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, 1, &equirectangularMapDescriptorSet);

        fixed (DescriptorSet* descriptorSetsPtr = sceneInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, sceneInfoDescriptorPool, 6, descriptorSetsPtr);

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
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitX, Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitX, Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitY, Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitY, -Vector3D<float>.UnitZ),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, Vector3D<float>.UnitZ, Vector3D<float>.UnitY),
            Matrix4X4.CreateLookAt(Vector3D<float>.Zero, -Vector3D<float>.UnitZ, Vector3D<float>.UnitY),
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

        var environmentMapDescriptorSet = CreateSingleTextureDescriptorSets(environmentCubemap.CubemapImageView, 1)[0];

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
        irradianceMapRenderStage.SubmitCommands(SCDevice.GraphicsQueue, 0, new Semaphore[] {});
        vk.QueueWaitIdle(SCDevice.GraphicsQueue);

        Image[] irradianceMapImages = irradianceMapAttachments.Select((attachment) => attachment.Color.Image).ToArray();
        var irradianceMap = SCDevice.ImagesToCubeMap(irradianceMapImages, new Extent2D() { Width = 32, Height = 32 });

        // free resources
        vk.FreeDescriptorSets(SCDevice.LogicalDevice, singleTextureDescriptorPool, 1, &environmentMapDescriptorSet);

        fixed (DescriptorSet* descriptorSetsPtr = sceneInfoDescriptorSets)
            vk.FreeDescriptorSets(SCDevice.LogicalDevice, sceneInfoDescriptorPool, 6, descriptorSetsPtr);

        for (int i = 0; i < 6; i++)
        {
            vk.DestroyBuffer(SCDevice.LogicalDevice, uniformBuffers[i], null);
            vk.FreeMemory(SCDevice.LogicalDevice, uniformBuffersMemory[i], null);
        }

        return irradianceMap;
    }

    void CleanupSwapchainObjects()
    {
        vk.DestroyPipeline(SCDevice.LogicalDevice, postProcessPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, postProcessPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, skyboxPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, skyboxPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, lightingPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, lightingPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, compositionPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, compositionPipeline.Layout, null);
        vk.DestroyPipeline(SCDevice.LogicalDevice, geometryPipeline.Pipeline, null);
        vk.DestroyPipelineLayout(SCDevice.LogicalDevice, geometryPipeline.Layout, null);

        geometryRenderStage.Dispose();
        compositionRenderStage.Dispose();
        bloomRenderStage1.Dispose();
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

        SCDevice.RecreateSwapchain(window);

        (geometryRenderStage, gBufferAttachments) = CreateGeometryRenderStage();
        (compositionRenderStage, compositionAttachments) = CreateCompositionRenderStage();
        (bloomRenderStage1, bloomAttachments1) = CreateBloomRenderStage();
        (postProcessRenderStage, swapChainAttachments) = CreatePostProcessRenderStage();

        UpdateScreenTextureDescriptorSets(screenTextureInfoDescriptorSets, gBufferAttachments.Albedo.ImageView,
                                                                           gBufferAttachments.Normal.ImageView,
                                                                           gBufferAttachments.AoRoughnessMetalness.ImageView,
                                                                           gBufferAttachments.Position.ImageView);
        UpdateSingleTextureDescriptorSets(compositionOutputTextureDescriptorSets, compositionAttachments.Color.ImageView);

        geometryPipeline = CreateGeometryPipeline(geometryRenderStage.RenderPass);
        compositionPipeline = CreateCompositionPipeline(compositionRenderStage.RenderPass);
        lightingPipeline = CreateLightingPipeline(compositionRenderStage.RenderPass);
        skyboxPipeline = CreateSkyboxPipeline(compositionRenderStage.RenderPass);
        postProcessPipeline = CreatePostProcessPipeline(postProcessRenderStage.RenderPass);
    }

    (RenderStage, GBufferAttachments) CreateGeometryRenderStage()
    {
        GBufferAttachments gBufferAttachments = new(SCDevice, SCDevice.SwapchainInfo.Extent);

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(gBufferAttachments.Albedo.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBufferAttachments.Normal.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBufferAttachments.AoRoughnessMetalness.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(gBufferAttachments.Position.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(gBufferAttachments.Depth.Format)
                         .AddDependency(Vk.SubpassExternal, 0,
                                        PipelineStageFlags.BottomOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.MemoryReadBit, AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                                        DependencyFlags.ByRegionBit)
                         .AddDependency(0, Vk.SubpassExternal,
                                        PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.ColorAttachmentOutputBit,
                                        AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit, AccessFlags.MemoryReadBit,
                                        DependencyFlags.ByRegionBit);
        RenderPass renderPass = renderPassBuilder.Build();

        RenderStage renderStage = new(SCDevice, renderPass, new[]{ gBufferAttachments }, SCDevice.SwapchainInfo.Extent, 1, MaxFramesInFlight);

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

    (RenderStage, CompositionAttachments) CreateCompositionRenderStage()
    {
        CompositionAttachments compositionAttachments = new(SCDevice, SCDevice.SwapchainInfo.Extent);

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(compositionAttachments.Color.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .AddColorAttachment(compositionAttachments.ThresholdedColor.Format, ImageLayout.ShaderReadOnlyOptimal)
                         .SetDepthStencilAttachment(compositionAttachments.Depth.Format)
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

        RenderStage renderStage = new(SCDevice, renderPass, new[]{ compositionAttachments }, SCDevice.SwapchainInfo.Extent, 1, MaxFramesInFlight);

        var clearColors = new ClearValue[] 
        { 
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, compositionAttachments);
    }

    (RenderStage, BloomAttachments) CreateBloomRenderStage()
    {
        BloomAttachments bloomAttachments = new(SCDevice, SCDevice.SwapchainInfo.Extent);

        RenderPassBuilder renderPassBuilder = new(SCDevice);
        renderPassBuilder.AddColorAttachment(bloomAttachments.Color.Format, ImageLayout.ShaderReadOnlyOptimal)
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

        RenderStage renderStage = new(SCDevice, renderPass, new[]{ bloomAttachments }, SCDevice.SwapchainInfo.Extent, 1, MaxFramesInFlight);

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

        RenderStage renderStage = new(SCDevice, renderPass, swapChainAttachments, SCDevice.SwapchainInfo.Extent, (uint) swapchainImageCount, MaxFramesInFlight);

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
                         .SetDepthStencilAttachment(cubefaceAttachments[0].Depth.Format)
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

        RenderStage renderStage = new(SCDevice, renderPass, cubefaceAttachments, new Extent2D(512, 512), 6, 1);

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
                         .SetDepthStencilAttachment(cubefaceAttachments[0].Depth.Format)
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

        RenderStage renderStage = new(SCDevice, renderPass, cubefaceAttachments, new Extent2D(32, 32), 6, 1);

        var clearColors = new ClearValue[]
        {
            new() { Color = { Float32_0 = 0.0f, Float32_1 = 0.0f, Float32_2 = 0.0f, Float32_3 = 1.0f } },
            new() { DepthStencil = { Depth = 1.0f, Stencil = 0 } }
        };
        renderStage.ClearValues.AddRange(clearColors);

        return (renderStage, cubefaceAttachments);
    }

    (RenderStage, DepthOnlyAttachment) CreateDepthMapRenderStage()
    {
        var shadowMapExtent = new Extent2D{ Width = ShadowMapResolution, Height = ShadowMapResolution };
        DepthOnlyAttachment depthAttachments = new(SCDevice, shadowMapExtent);
        
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

        RenderStage renderStage = new(SCDevice, renderPass, new[] { depthAttachments }, shadowMapExtent, 1, MaxFramesInFlight);

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

    void CreateSyncObjects(out Semaphore[] imageAvailableSemaphores,
                           out Fence[] inFlightFences)
    {
        imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
        inFlightFences = new Fence[MaxFramesInFlight];

        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (vk.CreateSemaphore(SCDevice.LogicalDevice, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                vk.CreateFence(SCDevice.LogicalDevice, in fenceInfo, null, out inFlightFences[i]) != Result.Success)
            {
                throw new Exception("Failed to create sync objects!");
            }
        }
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
        if (!disposedValue)
        {
            // free unmanaged resources unmanaged objects and override finalizer
            for (int i = 0; i < MaxFramesInFlight; i++)
            {
                vk.DestroySemaphore(SCDevice.LogicalDevice, imageAvailableSemaphores[i], null);
                vk.DestroyFence(SCDevice.LogicalDevice, inFlightFences[i], null);
            }

            geometryRenderStage.Dispose();
            compositionRenderStage.Dispose();
            postProcessRenderStage.Dispose();

            vk.DestroySampler(SCDevice.LogicalDevice, textureSampler, null);

            vk.DestroyCommandPool(SCDevice.LogicalDevice, SCDevice.CommandPool, null);

            CleanupSwapchainObjects();

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

