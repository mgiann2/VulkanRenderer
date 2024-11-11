using Silk.NET.Maths;
using Silk.NET.Windowing;

class Program
{
    public static void Main(string[] args)
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(800, 600),
            Title = "MGSV Renderer"
        };

        var window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan");
        }

        var renderer = new VulkanRenderer(window, true);

        var model = renderer.LoadModel("models/retro_computer/scene.gltf",
                                       "models/retro_computer/textures/retro_computer_setup_Mat_baseColor.png",
                                       "models/retro_computer/textures/retro_computer_setup_Mat_normal.png",
                                       "models/retro_computer/textures/retro_computer_setup_Mat_metallicRoughness.png");
        var camera = new Camera();
        camera.Transform.Position = new Vector3D<float>(0f, 0.5f, -3f);
        camera.Fov = 45.0f;

        var modelTransform = new Transform(Vector3D<float>.Zero, new Vector3D<float>(-90.0f, 0.0f, 0.0f), new Vector3D<float>(0.01f, 0.01f, 0.01f));

        window.Render += (double deltaTime) =>
        {
            // update rendering info
            var time = window.Time;
            (var width, var height) = (window.FramebufferSize.X, window.FramebufferSize.Y);

            SceneInfo sceneInfo = new()
            {
                CameraView = camera.GetViewMatrix(),
                CameraProjection = camera.GetProjectionMatrix((float) width / height),
                AmbientLightColor = new Vector3D<float>(1.0f),
                AmbientLightStrength = 0.1f
            };
            renderer.UpdateSceneInfo(sceneInfo);
            var modelMatrix = modelTransform.Matrix * Matrix4X4.CreateRotationY(Radians(90.0f) * (float)time);

            // start rendering
            renderer.BeginFrame();

            renderer.DrawModel(model, modelMatrix);

            renderer.EndFrame();
        };

        window.Closing += () =>
        {
            renderer.DeviceWaitIdle();
            renderer.UnloadModel(model);
        };

        window.Run();
    }

    static float Radians(float angle) => angle * MathF.PI / 180f;
}
