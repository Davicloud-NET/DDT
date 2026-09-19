namespace DDT.Server.Images;

public enum UploadCompletionStatus
{
    Added,
    Existing,
    NotFound,
    Busy,
    Incomplete,
    Refused,
    Failed,
    Stopping,
}
