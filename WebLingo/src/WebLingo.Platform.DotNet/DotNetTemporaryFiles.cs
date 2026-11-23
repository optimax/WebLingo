using WebLingo.Platform.Abstractions;
namespace WebLingo.Platform.DotNet;

/// <summary>
/// The server's temporary files on .NET (specification, section 6.12, "Uploaded files"): each a file of its own, in the
/// folder a site's configuration names, or the system's temporary folder, named <c>wasp-upload-</c> and 128 random bits,
/// deleted by the operating system as it is closed (<see cref="FileOptions.DeleteOnClose"/>), so that a process that ends
/// before its request does leaves none behind.
/// </summary>
public sealed class DotNetTemporaryFiles(string? folder = null) : IWebLingoTemporaryFiles
{
    public IWebLingoTemporaryFile Create()
    {
        var where = folder ?? Path.GetTempPath();
        var path = Path.Combine(where, "wasp-upload-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            return new TemporaryFile(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.DeleteOnClose));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, "a temporary file cannot be made in its folder");
        }
    }

    private sealed class TemporaryFile(FileStream stream) : IWebLingoTemporaryFile
    {
        private readonly object _lock = new();
        private bool _disposed;

        public long Length
        {
            get { lock (_lock) return _disposed ? 0 : stream.Length; }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                try
                {
                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(buffer, offset, count);
                }
                catch (IOException exception)
                {
                    throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, exception.Message.TrimEnd('.'));
                }
            }
        }

        public int Read(long position, byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                try
                {
                    stream.Seek(position, SeekOrigin.Begin);
                    return stream.Read(buffer, offset, count);
                }
                catch (IOException exception)
                {
                    throw new WebLingoFileSystemException(WebLingoFileErrorKind.Other, exception.Message.TrimEnd('.'));
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                stream.Dispose();
            }
        }
    }
}
