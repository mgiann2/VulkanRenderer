using Silk.NET.Maths;
using Silk.NET.Windowing;

var app = new MGSVRenderingApp();
app.Run();

unsafe class MGSVRenderingApp
{
    const int WIDTH = 1920;
    const int HEIGHT = 1080;

    private IWindow? window;

    public void Run()
    {
        InitWindow();
        InitVulkan();
        MainLoop();
        CleanUp();
    }

    private void InitWindow()
    {
        // create window
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(WIDTH, HEIGHT),
            Title = "MGSV Renderer"
        };

        window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan");
        }
    }

    private void InitVulkan()
    {

    }

    private void MainLoop()
    {
        window!.Run();
    }

    private void CleanUp()
    {
        window?.Dispose();
    }
}
