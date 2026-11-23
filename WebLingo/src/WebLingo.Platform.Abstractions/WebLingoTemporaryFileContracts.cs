namespace WebLingo.Platform.Abstractions;

/// <summary>
/// The server's temporary files (specification, section 6.12, "Uploaded files"), where a request's upload larger than the
/// site keeps in memory is kept until its request ends: each made empty, written, read, and removed, by its host, never a
/// script. Safe on several threads at once, each file on one at a time.
/// </summary>
public interface IWebLingoTemporaryFiles
{
    /// <summary>A new temporary file, empty, open for writing; a <see cref="WebLingoFileSystemException"/> if it cannot be made.</summary>
    IWebLingoTemporaryFile Create();
}

/// <summary>A temporary file: written, then read from any position; <see cref="IDisposable.Dispose"/> removes it, once.</summary>
public interface IWebLingoTemporaryFile : IDisposable
{
    /// <summary>Its length in bytes.</summary>
    long Length { get; }

    /// <summary>Writes <paramref name="count"/> bytes of <paramref name="buffer"/> from <paramref name="offset"/> at its end.</summary>
    void Write(byte[] buffer, int offset, int count);

    /// <summary>Reads up to <paramref name="count"/> bytes from <paramref name="position"/>: how many it read, 0 at its end.</summary>
    int Read(long position, byte[] buffer, int offset, int count);
}
