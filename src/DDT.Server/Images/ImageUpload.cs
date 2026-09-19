namespace DDT.Server.Images;

public sealed class ImageUpload
{
    public Guid Id { get; set; }

    public required string FileName { get; set; }

    public long Length { get; set; }

    public long LastModified { get; set; }

    // Bytes that are on disk and flushed, so a chunk that was cut off is sent again from here.
    public long Offset { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    // Set once the upload is in the library, so a repeated completion answers with the same result.
    public string? CompletedSha256 { get; set; }
}
