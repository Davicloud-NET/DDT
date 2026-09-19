namespace DDT.Contracts.Images;

public sealed record ImageSummary(
    Guid Id,
    string Name,
    ImageKind Kind,
    string Sha256,
    long SizeBytes,
    int WimIndex,
    string? Edition,
    string? Architecture,
    string? Version,
    string? Language,
    long InstalledBytes,
    string? OriginalFileName,
    DateTimeOffset UploadedUtc,
    string? UploadedBy);
