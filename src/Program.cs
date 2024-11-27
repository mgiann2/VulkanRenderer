using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Renderer;

class Program
{
    const string ComputerModelPath = "assets/models/retro_computer/";
    const string ComputerTexturePath = "textures/retro_computer_setup_Mat_";
    const string MaterialsPath = "assets/materials/";

    const float MouseSensitivity = 0.1f;

    static IWindow? window;
    static VulkanRenderer? renderer;
    static IInputContext? input;

    static Camera? camera;
    static Transform? computerTransform;
    static Model computerModel;

    static Model cubeModel;
    static Transform? cubeTransform;

    static Model quadModel;

    static Light[]? lights;
    
    static Vector2D<float> keyboardMovement = Vector2D<float>.Zero;
    static Vector2D<float> prevMousePosition = Vector2D<float>.Zero;

    public static void Main(string[] args)
    {
        // setup window
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(800, 600),
            Title = "MGSV Renderer"
        };

        window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan");
        }
        window.VSync = true;
        window.WindowBorder = WindowBorder.Fixed;

        window.Render += OnRender;
        window.Closing += OnClose;

        // setup renderer
        renderer = new VulkanRenderer(window!, true);

        // setup input
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

        // load scene objects
        camera = new Camera(new Vector3D<float>(0f, 0.5f, -3f), Vector3D<float>.Zero, Vector3D<float>.One, 45.0f);

        computerModel = renderer!.LoadModel(ComputerModelPath + "scene.gltf",
                                    ComputerModelPath + ComputerTexturePath + "baseColor.png",
                                    ComputerModelPath + ComputerTexturePath + "normal.png",
                                    ComputerModelPath + ComputerTexturePath + "metallicRoughness.png");
        computerTransform = new Transform(Vector3D<float>.Zero, new Vector3D<float>(90.0f, 180.0f, 0.0f), new Vector3D<float>(0.01f, 0.01f, 0.01f));

        var paintedMetalMaterial = renderer.CreateMaterial(MaterialsPath + "PaintedMetal/BaseColor.png",
                                                   MaterialsPath + "PaintedMetal/NormHeight.png",
                                                   MaterialsPath + "PaintedMetal/ARM.png");
        cubeModel = new Model(PrimitiveMesh.CreateCubeMesh(renderer), paintedMetalMaterial);
        cubeTransform = new Transform();

        quadModel = new Model(PrimitiveMesh.CreateQuadMesh(renderer), paintedMetalMaterial);

        lights = new Light[]
        {
            new()
            {
                Position = new Vector3D<float>(-2.0f, 0.0f, 1.0f),
                Color = new Vector3D<float>(0.0f, 2.0f, 0.0f),
            },
            new()
            {
                Position = new Vector3D<float>(2.0f, 0.0f, 1.0f),
                Color = new Vector3D<float>(0.0f, 0.0f, 2.0f)
            }
        }; 
        renderer!.Lights.AddRange(lights);

        window.Run();
    }

    static void OnRender(double deltaTime)
    {
        HandleInput();

        // update rendering info
        var time = window!.Time;
        (var width, var height) = (window.FramebufferSize.X, window.FramebufferSize.Y);

        // move camera
        camera!.Transform.Translate(camera!.Transform.Forward * (float) deltaTime * keyboardMovement.Y);
        camera!.Transform.Translate(camera!.Transform.Right * (float) deltaTime * keyboardMovement.X);

        SceneInfo sceneInfo = new()
        {
            CameraView = camera!.GetViewMatrix(),
            CameraProjection = camera!.GetProjectionMatrix((float) width / height),
            AmbientLightColor = new Vector3D<float>(1.0f),
            AmbientLightStrength = 0.1f
        };
        renderer!.UpdateSceneInfo(sceneInfo);

        // start rendering
        renderer.BeginFrame();

        renderer.DrawModel(computerModel, computerTransform!.Matrix);
        // renderer.DrawModel(cubeModel, cubeTransform!.Matrix);

        renderer.EndFrame();
    }

    static void OnClose()
    {
        renderer!.DeviceWaitIdle();
        renderer!.DestroyModel(computerModel);
    }

    static void OnKeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        if (key == Key.Escape)
            window!.Close();
    }

    static void OnMouseMove(IMouse mouse, System.Numerics.Vector2 mousePos)
    {
        var currMousePos = new Vector2D<float>(mousePos.X, mousePos.Y);
        var mouseMovement = currMousePos - prevMousePosition;

        Vector3D<float> rotation = new Vector3D<float>(-mouseMovement.Y, mouseMovement.X, 0.0f) * MouseSensitivity;
        camera!.Transform.Rotate(rotation);

        prevMousePosition = currMousePos;
    }

    static void HandleInput()
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

    static float Radians(float angle) => angle * MathF.PI / 180f;
}
