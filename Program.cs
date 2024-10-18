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

        Vertex[] vertices = new Vertex[] 
        {
            new Vertex() { pos = new Vector3D<float>(-0.5f, -0.5f, 0.0f), color = new Vector3D<float>(1.0f, 0.0f, 0.0f), texCoord = new Vector2D<float>(1.0f, 0.0f) },
            new Vertex() { pos = new Vector3D<float>(0.5f, -0.5f, 0.0f), color = new Vector3D<float>(0.0f, 1.0f, 0.0f), texCoord = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex() { pos = new Vector3D<float>(0.5f, 0.5f, 0.0f), color = new Vector3D<float>(0.0f, 0.0f, 1.0f), texCoord = new Vector2D<float>(0.0f, 1.0f) },
            new Vertex() { pos = new Vector3D<float>(-0.5f, 0.5f, 0.0f), color = new Vector3D<float>(1.0f, 1.0f, 1.0f), texCoord = new Vector2D<float>(1.0f, 1.0f) },

            new Vertex() { pos = new Vector3D<float>(-0.5f, -0.5f, -0.5f), color = new Vector3D<float>(1.0f, 0.0f, 0.0f), texCoord = new Vector2D<float>(1.0f, 0.0f) },
            new Vertex() { pos = new Vector3D<float>(0.5f, -0.5f, -0.5f), color = new Vector3D<float>(0.0f, 1.0f, 0.0f), texCoord = new Vector2D<float>(0.0f, 0.0f) },
            new Vertex() { pos = new Vector3D<float>(0.5f, 0.5f, -0.5f), color = new Vector3D<float>(0.0f, 0.0f, 1.0f), texCoord = new Vector2D<float>(0.0f, 1.0f) },
            new Vertex() { pos = new Vector3D<float>(-0.5f, 0.5f, -0.5f), color = new Vector3D<float>(1.0f, 1.0f, 1.0f), texCoord = new Vector2D<float>(1.0f, 1.0f) },
        };

        var vertexBuffer = renderer.CreateVertexBuffer(vertices);

        ushort[] indices = new ushort[] 
        { 
            0, 1, 2, 2, 3, 0,
            4, 5, 6, 6, 7, 4
        };
        var indexBuffer = renderer.CreateIndexBuffer(indices);

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
            renderer.BeginFrame();
            renderer.BeginRenderPass();

            renderer.UpdateUniformBuffer(ubo);
            renderer.Bind(vertexBuffer);
            renderer.Bind(indexBuffer);
            renderer.DrawIndexed(indexBuffer.IndexCount);

            renderer.EndRenderPass();
            renderer.EndFrame();
        };

        window.Closing += () =>
        {
            renderer.DeviceWaitIdle();
        };

        window.Run();

        renderer.DestroyBuffer(vertexBuffer);
        renderer.DestroyBuffer(indexBuffer);
    }

    static float Radians(float angle) => angle * MathF.PI / 180f;
}
