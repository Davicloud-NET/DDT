namespace DDT.Contracts.Images;

// LastModified is the browser's File.lastModified. With the name and length it recognises the same file selected
// again after a reload, so the upload resumes.
public sealed record CreateImageUploadRequest(string FileName, long Length, long LastModified);
