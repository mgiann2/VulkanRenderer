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
    static Transform? quadTransform;

    static Light[]? lights;
    
    static Vector2D<float> keyboardMovement = Vector2D<float>.Zero;
    static Vector2D<float> prevMousePosition = Vector2D<float>.Zero;

    public static void Main(string[] args)
    {
        // setup window
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(960, 540),
            Title = "Vulkan Renderer"
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
        camera = new Camera(new Vector3D<float>(2.0f, 1.0f, -4.0f), Vector3D<float>.Zero, Vector3D<float>.One, 45.0f);

        computerModel = new Model(renderer,
                ComputerModelPath + "scene.gltf",
                ComputerModelPath + ComputerTexturePath + "baseColor.png",
                ComputerModelPath + ComputerTexturePath + "normal.png",
                ComputerModelPath + ComputerTexturePath + "metallicRoughness.png");
        computerTransform = new Transform(Vector3D<float>.Zero, new Vector3D<float>(90.0f, 180.0f, 0.0f), new Vector3D<float>(0.01f, 0.01f, 0.01f));

        var paintedMetalMaterial = new Material(renderer, MaterialsPath + "PaintedMetal/BaseColor.png",
                                                          MaterialsPath + "PaintedMetal/Normal.png",
                                                          MaterialsPath + "PaintedMetal/ARM.png");
        cubeModel = new Model(PrimitiveMesh.CreateCubeMesh(renderer), paintedMetalMaterial);
        cubeTransform = new Transform(new Vector3D<float>(4.0f, 0.0f, 0.0f), Vector3D<float>.Zero, Vector3D<float>.One);

        quadModel = new Model(PrimitiveMesh.CreateQuadMesh(renderer), paintedMetalMaterial);
        quadTransform = new Transform(new Vector3D<float>(0.0f, -0.51f, 0.0f), new Vector3D<float>(90.0f, 0.0f, 0.0f), new Vector3D<float>(10.0f));

        lights = new Light[]
        {
            new()
            {
                Position = new Vector3D<float>(-2.0f, 2.0f, 0.0f),
                Color = new Vector3D<float>(0.0f, 2.0f, 0.0f),
            },
            new()
            {
                Position = new Vector3D<float>(2.0f, 2.0f, 0.0f),
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
        (var width, var height) = (window!.FramebufferSize.X, window!.FramebufferSize.Y);

        // move camera
        camera!.Transform.Translate(camera!.Transform.Forward * (float) deltaTime * keyboardMovement.Y);
        camera!.Transform.Translate(camera!.Transform.Right * (float) deltaTime * keyboardMovement.X);

        SceneInfo sceneInfo = new()
        {
            CameraView = camera!.GetViewMatrix(),
            CameraProjection = camera!.GetProjectionMatrix((float) width / height),
            CameraPosition = camera!.Transform.Position,
            DirectionalLightDirection = new Vector3D<float>(2.0f, -4.0f, 1.0f),
            DirectionalLightColor = new Vector3D<float>(0.35f, 0.25f, 0.2f),
        };
        Matrix4X4<float> lightView = Matrix4X4.CreateLookAt<float>(new Vector3D<float>(-2.0f, 4.0f, -1.0f),
                                                                   Vector3D<float>.Zero,
                                                                   Vector3D<float>.UnitY);
        Matrix4X4<float> lightProj = Matrix4X4.CreateOrthographicOffCenter(-10.0f, 10.0f, 10.0f, -10.0f, 1.0f, 7.5f);
        sceneInfo.LightSpaceMatrix = lightView * lightProj;

        renderer!.UpdateSceneInfo(sceneInfo);

        // start rendering
        renderer.BeginFrame();

        renderer.DrawModel(computerModel, computerTransform!.Matrix);
        renderer.DrawModel(cubeModel, cubeTransform!.Matrix);
        renderer.DrawModel(quadModel, quadTransform!.Matrix);

        renderer.EndFrame();
    }

    static void OnClose()
    {
        renderer!.DeviceWaitIdle();
        computerModel.Mesh.Dispose();
        cubeModel.Mesh.Dispose();
        quadModel.Mesh.Dispose();
        computerModel.Material.Dispose();
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
