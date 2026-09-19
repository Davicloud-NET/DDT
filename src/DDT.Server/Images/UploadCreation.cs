using DDT.Contracts.Images;

namespace DDT.Server.Images;

// Session is null when the store volume lacks the space; the byte counts then say how much.
public sealed record UploadCreation(ImageUploadSession? Session, bool Created, long RequiredBytes, long AvailableBytes);
