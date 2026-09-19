using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Images;

// Uploads are staged on the same volume as the library, so finishing one is a rename.
public sealed class ImageStore(IOptions<DdtOptions> options)
{
    public string ObjectsDirectory => Path.GetFullPath(Path.Combine(options.Value.StorePath, "images", "objects"));

    public string UploadsDirectory => Path.GetFullPath(Path.Combine(options.Value.StorePath, "images", "uploads"));

    public string ObjectPath(string sha256) => Path.Combine(ObjectsDirectory, sha256);

    public string PartPath(Guid uploadId) => Path.Combine(UploadsDirectory, $"{uploadId:N}.part");
}
