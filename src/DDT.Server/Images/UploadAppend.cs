namespace DDT.Server.Images;

// Offset is the committed offset after the attempt, which the client continues from.
public sealed record UploadAppend(UploadAppendStatus Status, long Offset);
