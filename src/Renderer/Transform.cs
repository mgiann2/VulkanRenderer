using Silk.NET.Maths;

namespace Renderer;

public class Transform
{
    protected Vector3D<float> position;
    protected Vector3D<float> rotation;
    protected Vector3D<float> scale;
    protected Matrix4X4<float> matrix;

    public Vector3D<float> Position
    {
        get { return position; }
        set 
        { 
            position = value;
            UpdateTransform();
        }
    }

    public Vector3D<float> Rotation
    {
        get { return rotation; }
        set 
        {
            rotation = value;
            UpdateTransform();
        }
    }

    public Vector3D<float> Scale
    {
        get { return scale; }
        set 
        {
            scale = value;
            UpdateTransform();
        }
    }

    public Matrix4X4<float> Matrix { get => matrix; }
    
    public Vector3D<float> Right 
    { 
        get
        {
             return new Vector3D<float>(matrix.M11, matrix.M21, matrix.M31);           
        }
    }

    public Vector3D<float> Up
    { 
        get
        {
             return new Vector3D<float>(matrix.M12, matrix.M22, matrix.M32);           
        }
    }

    public Vector3D<float> Forward
    { 
        get
        {
             return new Vector3D<float>(matrix.M13, matrix.M23, matrix.M33);           
        }
    }

    public Transform()
    {
        position = Vector3D<float>.Zero;
        rotation = Vector3D<float>.Zero;
        scale = Vector3D<float>.One;

        UpdateTransform();
    }

    public Transform(Vector3D<float> aPosition, Vector3D<float> aRotation, Vector3D<float> aScale)
    {
        position = aPosition;
        rotation = aRotation;
        scale = aScale;

        UpdateTransform();
    }

    public void Translate(Vector3D<float> translation)
    {
        position += translation;

        UpdateTransform();
    }

    public void Rotate(Vector3D<float> rotateDegrees)
    {
        rotation.X = (rotation.X + rotateDegrees.X) % 360f;
        rotation.Y = (rotation.Y + rotateDegrees.Y) % 360f;
        rotation.Z = (rotation.Z + rotateDegrees.Z) % 360f;

        UpdateTransform();
    }

    protected void UpdateTransform()
    {
        matrix = Matrix4X4<float>.Identity;
        matrix *= Matrix4X4.CreateScale(Scale);

        var rotX = Matrix4X4.CreateRotationX(Radians(Rotation.X));
        var rotY = Matrix4X4.CreateRotationY(Radians(Rotation.Y));
        var rotZ = Matrix4X4.CreateRotationZ(Radians(Rotation.Z));
        matrix *= rotZ * rotY * rotX;

        matrix *= Matrix4X4.CreateTranslation(Position);

        float Radians(float angle) => angle * MathF.PI / 180f; 
    }
}
