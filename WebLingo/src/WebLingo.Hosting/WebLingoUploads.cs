using WebLingo.Platform.Abstractions;
namespace WebLingo.Hosting;

/// <summary>
/// What a site's request's body may be (specification, section 6.12, "Uploaded files"): its largest, in bytes, a larger
/// one refused with 413 before it is read where its <c>Content-Length</c> says so, and an error where it is read otherwise;
/// how many bytes of a file a form uploads are kept in memory, the rest in a temporary file of the server's,
/// <paramref name="TemporaryFiles"/>, none kept where there are none; and the most its files may take in all.
/// </summary>
public sealed record WebLingoUploads(long MaxBodySize, int InMemory, long MaxFiles, IWebLingoTemporaryFiles? TemporaryFiles)
{
    /// <summary>The largest body by default, 30,000,000 bytes, as IIS's and Kestrel's own limits are.</summary>
    public const long DefaultMaxBodySize = 30_000_000;

    /// <summary>How much of a file is kept in memory by default, 64 KB, as ASP.NET Core keeps a form's file.</summary>
    public const int DefaultInMemory = 64 * 1024;

    /// <summary>The defaults, and no temporary files: a host that keeps none takes no file larger than 64 KB.</summary>
    public static WebLingoUploads Default { get; } = new(DefaultMaxBodySize, DefaultInMemory, DefaultMaxBodySize, null);
}
