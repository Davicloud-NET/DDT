namespace DDT.Contracts.Images;

public sealed record ImageUploadSession(Guid Id, string FileName, long Length, long LastModified, long Offset, int ChunkBytes);
