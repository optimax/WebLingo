using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using WebLingo.Hosting.DotNet;
using Xunit;
namespace WebLingo.Hosting.Tests;

/// <summary>
/// The hosts' own code, without ASP.NET Core (specification, section 9, "M0.23 parts", part 1): a file's content type by its
/// extension, and a status's reason phrase, the same as ASP.NET Core's, from whose tables they were made, so that a host
/// without ASP.NET Core serves a site as ASP.NET Core's does; a newer ASP.NET Core that adds one fails these, its table then
/// made again.
/// </summary>
public sealed class HostsOwnCodeTests
{
    [Fact]
    public void AFilesContentTypeIsAspNetCoresForEveryExtension()
    {
        var provider = new FileExtensionContentTypeProvider();
        Assert.Equal(provider.Mappings.Count, WebLingoContentTypes.Count);
        foreach (var (extension, type) in provider.Mappings)
        {
            Assert.Equal(type, WebLingoContentTypes.Of("folder/file" + extension));
            Assert.Equal(type, WebLingoContentTypes.Of("FILE" + extension.ToUpperInvariant()));
        }
        foreach (var path in new[] { "readme", "folder.v2/readme", "archive.unknown", "trailing.", "" })
        {
            Assert.Equal(provider.TryGetContentType(path, out var type) ? type : null, WebLingoContentTypes.Of(path));
        }
    }

    [Fact]
    public void AStatusesReasonPhraseIsAspNetCores()
    {
        for (var status = 0; status < 1000; status++) Assert.Equal(ReasonPhrases.GetReasonPhrase(status), WebLingoReasonPhrases.Of(status));
    }
}
