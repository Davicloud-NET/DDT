namespace DDT.Server.Images;

public static class ImageUploadLimits
{
    // Well under the default body limits of Kestrel and IIS; nginx needs its limit raised for any chunk size.
    public const int ChunkBytes = 8 * 1024 * 1024;

    public const int MaxFileNameLength = 256;

    public const int RetryAfterSeconds = 5;

    // Room left on the store volume for the database, the logs and the other uploads' remaining bytes.
    public const long FreeSpaceMargin = 1024L * 1024 * 1024;

    // Kestrel's minimum data rate is averaged over the whole body, so a chunk that stops half way would otherwise
    // hold its session's lock for hours.
    public static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
}
