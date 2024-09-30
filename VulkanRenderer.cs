

class VulkanRenderer : IDisposable
{
    private bool disposedValue;

    /// <summary>
    /// Initializes renderer to start drawing new frame
    /// </summary>
    public void BeginFrame()
    {

    }


    /// <summary>
    /// Submits a new frame to be drawn
    /// </summary>
    public void EndFrame()
    {

    }

    ~VulkanRenderer()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // dispose managed objects
            }

            // free unmanaged resources unmanaged objects and override finalizer
            disposedValue = true;
        }
    }
}
