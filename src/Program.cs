#nullable disable

using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Renderer;

class Program
{
    private const string ComputerModelPath = "assets/models/retro_computer/";
    private const string ComputerTexturePath = "textures/retro_computer_setup_Mat_";
    private const string MaterialsPath = "assets/materials/";

    private const float MouseSensitivity = 0.1f;

    private static IWindow window;
    private static VulkanRenderer renderer;
    private static IInputContext input;

    private static Camera camera;

    private static Transform computerTransform;
    private static Model computerModel;

    private static Transform floorTransform;
    private static Model floorModel;
    private static Material floorMaterial;

    private static Vector2D<float> keyboardMovement = Vector2D<float>.Zero;
    private static Vector2D<float> prevMousePosition = Vector2D<float>.Zero;

    public static void Main(string[] args)
    {
        SetupWindow();
        renderer = new VulkanRenderer(window, true);

        SetupInput();

        CreateSceneObjects();

        window.Run();
    }

    public static void SetupWindow()
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(1920, 1080),
            Title = "Vulkan Renderer"
        };

        window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan");
        }
        window.WindowBorder = WindowBorder.Resizable;

        window.Render += OnRender;
        window.Closing += OnClose;
    }

    public static void SetupInput()
    {
        input = window!.CreateInput();
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }
        foreach (var mouse in input.Mice)
        {
            mouse.Cursor.CursorMode = CursorMode.Raw;
            mouse.MouseMove += OnMouseMove;
        }

        prevMousePosition = new Vector2D<float>(window.Size.X / 2, window.Size.Y / 2);
    }

    public static void CreateSceneObjects()
    {
        // Create camera
        camera = new Camera(new Vector3D<float>(2.0f, 1.0f, -4.0f), Vector3D<float>.Zero, Vector3D<float>.One, 45.0f);

        // Create models and their transforms
        computerModel = new Model(renderer,
                ComputerModelPath + "scene.gltf",
                ComputerModelPath + ComputerTexturePath + "baseColor.png",
                ComputerModelPath + ComputerTexturePath + "normal.png",
                ComputerModelPath + ComputerTexturePath + "metallicRoughness.png");
        computerTransform = new Transform(Vector3D<float>.Zero, new Vector3D<float>(90.0f, 180.0f, 0.0f), new Vector3D<float>(0.01f, 0.01f, 0.01f));

        floorMaterial = new Material(renderer, MaterialsPath + "ScratchedPaintedMetal/BaseColor.png",
                                     MaterialsPath + "ScratchedPaintedMetal/Normal.png",
                                     MaterialsPath + "ScratchedPaintedMetal/ARM.png");
        floorModel = new Model(PrimitiveMesh.CreateQuadMesh(renderer), floorMaterial);
        floorTransform = new Transform(new Vector3D<float>(0.0f, -0.1f, 0.0f), Vector3D<float>.UnitX * 90.0f, Vector3D<float>.One);

        // Add lights to scene
        Light[] lights = new Light[2]
        {
            new() { Position = new Vector3D<float>(1.0f, 1.0f, 0.0f), Color = new Vector3D<float>(0.0f, 1.0f, 0.0f) },
            new() { Position = new Vector3D<float>(-1.0f, 1.0f, 0.0f), Color = new Vector3D<float>(1.0f, 0.0f, 0.0f) }
        };
        renderer.Lights.AddRange(lights);
    }

    private static void OnRender(double deltaTime)
    {
        HandleInput();

        // update rendering info
        (var width, var height) = (window.FramebufferSize.X, window.FramebufferSize.Y);

        // move camera
        camera.Transform.Translate(camera.Transform.Forward * (float) deltaTime * keyboardMovement.Y);
        camera.Transform.Translate(camera.Transform.Right * (float) deltaTime * keyboardMovement.X);

        SceneInfo sceneInfo = new()
        {
            CameraView = camera.GetViewMatrix(),
            CameraProjection = camera.GetProjectionMatrix((float) width / height),
            CameraPosition = camera.Transform.Position,
            DirectionalLightDirection = new Vector3D<float>(2.0f, -4.0f, 1.0f),
            DirectionalLightColor = new Vector3D<float>(0.35f, 0.25f, 0.2f),
        };
        Matrix4X4<float> lightView = Matrix4X4.CreateLookAt<float>(new Vector3D<float>(-2.0f, 4.0f, -1.0f),
                                                                   Vector3D<float>.Zero,
                                                                   Vector3D<float>.UnitY);
        Matrix4X4<float> lightProj = Matrix4X4.CreateOrthographicOffCenter(-10.0f, 10.0f, 10.0f, -10.0f, 1.0f, 7.5f);
        sceneInfo.LightSpaceMatrix = lightView * lightProj;

        renderer.UpdateSceneInfo(sceneInfo);

        // start rendering
        renderer.BeginFrame();

        renderer.DrawModel(computerModel, computerTransform.Matrix);
        renderer.DrawModel(floorModel, floorTransform.Matrix);

        renderer.EndFrame();
    }

    private static void OnClose()
    {
        computerModel.Mesh.Dispose();
        computerModel.Material.Dispose();
    
        floorModel.Material.Dispose();
        floorModel.Mesh.Dispose();

        renderer.Dispose();
    }

    private static void OnKeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        if (key == Key.Escape)
            window.Close();
    }

    private static void OnMouseMove(IMouse mouse, System.Numerics.Vector2 mousePos)
    {
        var currMousePos = new Vector2D<float>(mousePos.X, mousePos.Y);
        var mouseMovement = currMousePos - prevMousePosition;

        Vector3D<float> rotation = new Vector3D<float>(-mouseMovement.Y, mouseMovement.X, 0.0f) * MouseSensitivity;
        camera.Transform.Rotate(rotation);

        prevMousePosition = currMousePos;
    }

    private static void HandleInput()
    {
        // treat all connected keyboards as a single input source
        Key[] keysToCheck = new[] { Key.W, Key.A, Key.S, Key.D };
        HashSet<Key> keysPressed = new();

        foreach (var keyboard in input!.Keyboards)
        {
            foreach (var key in keysToCheck)
            {
                if (keyboard.IsKeyPressed(key))
                {
                    keysPressed.Add(key);
                }
            }
        }

        // set movement direction
        keyboardMovement = Vector2D<float>.Zero;
        if (keysPressed.Contains(Key.A))
            keyboardMovement.X += 1f;
        if (keysPressed.Contains(Key.D))
            keyboardMovement.X -= 1f;
        if (keysPressed.Contains(Key.W))
            keyboardMovement.Y += 1f;
        if (keysPressed.Contains(Key.S))
            keyboardMovement.Y -= 1f;
    }

    private static float Radians(float angle) => angle * MathF.PI / 180f;
}
