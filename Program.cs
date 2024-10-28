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

        // var model = renderer.LoadModel("models/viking_room.obj", "textures/viking_room.png");

        window.Render += (double deltaTime) =>
        {
            // update rendering info
            var time = window.Time;
            (var width, var height) = (window.FramebufferSize.X, window.FramebufferSize.Y);
            UniformBufferObject ubo = new()
            {
                model = Matrix4X4<float>.Identity * Matrix4X4.CreateFromAxisAngle(new Vector3D<float>(0, 0, 1), (float)time * Radians(90.0f)),
                view = Matrix4X4.CreateLookAt(new Vector3D<float>(2, 2, 2), new Vector3D<float>(0, 0, 0), new Vector3D<float>(0, 0, 1)),
                proj = Matrix4X4.CreatePerspectiveFieldOfView(Radians(45.0f), (float)width / height, 0.1f, 10.0f)
            };
            ubo.proj.M22 *= -1;

            // start rendering
            // renderer.BeginFrame();
            // renderer.BeginRenderPass();
            //
            // renderer.UpdateUniformBuffer(ubo);
            //
            // renderer.Bind(model.IndexBuffer);
            // renderer.Bind(model.VertexBuffer);
            // renderer.BindTexture(model.Texture);
            // renderer.DrawIndexed(model.IndexBuffer.IndexCount);
            //
            // renderer.EndRenderPass();
            // renderer.EndFrame();
        };

        window.Closing += () =>
        {
            renderer.DeviceWaitIdle();
            // renderer.UnloadModel(model);
        };

        window.Run();
    }

    static float Radians(float angle) => angle * MathF.PI / 180f;
}
