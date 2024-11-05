using Silk.NET.Maths;

public class Camera
{
    public Transform Transform { get; set; }
    public float Fov { get; set; }

    public Camera()
    {
        Transform = new Transform();
        Fov = 60.0f;
    }

    public Camera(Vector3D<float> position, Vector3D<float> rotation, Vector3D<float> scale, float fov)
    {
        Transform = new Transform(position, rotation, scale);
        Fov = fov;
    }

    public Matrix4X4<float> GetViewMatrix()
    {
        return Matrix4X4.CreateLookAt(Transform.Position, Transform.Position + Transform.Forward, Transform.Up); 
    }

    public Matrix4X4<float> GetProjectionMatrix(float aspectRatio, float nearPlaneDistance = 0.1f, float farPlaneDistance = 10.0f)
    {
        var proj = Matrix4X4.CreatePerspectiveFieldOfView(Fov * MathF.PI / 180.0f, aspectRatio, nearPlaneDistance, farPlaneDistance);
        proj.M22 *= -1f; // flip y-axis since positive y points down
        return proj;
    }
}
