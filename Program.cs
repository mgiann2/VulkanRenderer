using Silk.NET.Maths;
using Silk.NET.Windowing;

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
    new Vertex() { pos = new Vector2D<float>(-0.5f, -0.5f), color = new Vector3D<float>(1.0f, 0.0f, 0.0f) },
    new Vertex() { pos = new Vector2D<float>(0.5f, -0.5f), color = new Vector3D<float>(0.0f, 1.0f, 0.0f) },
    new Vertex() { pos = new Vector2D<float>(0.5f, 0.5f), color = new Vector3D<float>(0.0f, 0.0f, 1.0f) },
    new Vertex() { pos = new Vector2D<float>(-0.5f, 0.5f), color = new Vector3D<float>(1.0f, 1.0f, 1.0f) },
};

var vertexBuffer = renderer.CreateVertexBuffer(vertices);

ushort[] indices = new ushort[] { 0, 1, 2, 2, 3, 0 };
var indexBuffer = renderer.CreateIndexBuffer(indices);

window.Render += (double deltaTime) =>
{
    renderer.BeginFrame();
    renderer.BeginRenderPass();
    
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
