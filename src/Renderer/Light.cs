using Silk.NET.Maths;

namespace Renderer;

public class Light
{
    public Vector3D<float> Position { get; set; }
    public Vector3D<float> Color { get; set; }

    public LightInfo ToInfo()
    {
        return new LightInfo
        {
            Model = Matrix4X4.CreateScale(CalculateVolumeSize()) * Matrix4X4.CreateTranslation(Position),
            Position = Position,
            Color = Color
        };
    }

    private float CalculateVolumeSize()
    {
        // attenuation function is d^2
        // volume radius is when attenuation equals 5 / 256
        float maxIntensity = new float[] { Color.X, Color.Y, Color.Z }.Max();
        return 16 * MathF.Sqrt(maxIntensity / 5);
    }
}
