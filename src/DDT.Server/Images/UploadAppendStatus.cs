namespace DDT.Server.Images;

public enum UploadAppendStatus
{
    Appended,
    NotFound,
    BeyondLength,
    Completed,
    Busy,
    OffsetMismatch,
    Restarted,
    CutOff,
    Stalled,
    DiskFull,
}
