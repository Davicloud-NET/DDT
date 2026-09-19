namespace DDT.Core.Wim;

// Architecture is "x86", "x64", "arm64" or null.
public sealed record WimImageInfo(
    int Index,
    string Name,
    string? Description,
    string? EditionId,
    string? Architecture,
    string? Version,
    string? DefaultLanguage,
    long TotalBytes,
    long HardLinkBytes)
{
    public long InstalledBytes => TotalBytes - HardLinkBytes;
}
