using DDT.Contracts.Images;

namespace DDT.Server.Images;

// One row per image index of an uploaded file, so several rows can share one stored file.
public sealed class Image
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public ImageKind Kind { get; set; }

    // Lower case hex, which is also the stored file's name.
    public required string Sha256 { get; set; }

    public long SizeBytes { get; set; }

    public int WimIndex { get; set; }

    public string? Edition { get; set; }

    public string? Architecture { get; set; }

    public string? Version { get; set; }

    public string? Language { get; set; }

    public long InstalledBytes { get; set; }

    public string? OriginalFileName { get; set; }

    public DateTimeOffset UploadedUtc { get; set; }

    public Guid? UploadedByUserId { get; set; }

    public string? UploadedByName { get; set; }
}
