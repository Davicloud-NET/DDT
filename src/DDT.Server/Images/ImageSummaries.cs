using DDT.Contracts.Images;

namespace DDT.Server.Images;

public static class ImageSummaries
{
    public static ImageSummary From(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return new ImageSummary(
            image.Id,
            image.Name,
            image.Kind,
            image.Sha256,
            image.SizeBytes,
            image.WimIndex,
            image.Edition,
            image.Architecture,
            image.Version,
            image.Language,
            image.InstalledBytes,
            image.OriginalFileName,
            image.UploadedUtc,
            image.UploadedByName);
    }
}
