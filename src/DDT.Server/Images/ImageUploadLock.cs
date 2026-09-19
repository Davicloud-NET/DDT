namespace DDT.Server.Images;

// Released by disposing it, exactly once however often that happens.
public sealed class ImageUploadLock : IDisposable
{
    private readonly ImageUploadLocks _owner;
    private int _released;

    internal ImageUploadLock(ImageUploadLocks owner, Guid uploadId)
    {
        _owner = owner;
        UploadId = uploadId;
    }

    public Guid UploadId { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _owner.Exit(this);
        }
    }
}
