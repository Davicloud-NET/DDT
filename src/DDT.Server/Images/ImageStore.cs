// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Images;

// Uploads are staged on the same volume as the library, so finishing one is a rename. The objects are both images and
// packages. Each file is stored once, under its hash.
public sealed partial class ImageStore(IOptions<DdtOptions> options, ILogger<ImageStore> logger)
{
    public string ObjectsDirectory => Path.GetFullPath(Path.Combine(options.Value.StorePath, "images", "objects"));

    public string UploadsDirectory => Path.GetFullPath(Path.Combine(options.Value.StorePath, "images", "uploads"));

    // Hold this while stored files and their rows change together. That covers completing an upload, removing an image
    // or a package, and saving an assignment or a pick that refers to them. Creating an upload session holds it too,
    // because its free space check counts the other sessions.
    public SemaphoreSlim LibraryLock { get; } = new(1, 1);

    public string ObjectPath(string sha256) => Path.Combine(ObjectsDirectory, sha256);

    public string PartPath(Guid uploadId) => Path.Combine(UploadsDirectory, $"{uploadId:N}.part");

    // The import of a disk image keeps the raw disk and its compressed copy here until the copy is in the library.
    public string RawPath(Guid uploadId) => Path.Combine(UploadsDirectory, $"{uploadId:N}.raw");

    public string CompressedPath(Guid uploadId) => Path.Combine(UploadsDirectory, $"{uploadId:N}.zst");

    // Returns every file an upload may have on the volume.
    public IEnumerable<string> UploadFiles(Guid uploadId) => [PartPath(uploadId), RawPath(uploadId), CompressedPath(uploadId)];

    // On Linux, DriveInfo measures the file system of the path it's given. Here that's the store volume.
    public DriveInfo Volume() => new(Directory.CreateDirectory(UploadsDirectory).FullName);

    // Call with LibraryLock held, so no completed upload can add a row for this hash in between.
    public async Task<bool> DeleteObjectIfUnreferencedAsync(DdtDbContext database, string sha256, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        bool referenced = await database.Images.AnyAsync(i => i.Sha256 == sha256, cancellationToken).ConfigureAwait(false)
            || await database.Packages.AnyAsync(p => p.Sha256 == sha256, cancellationToken).ConfigureAwait(false)
            || await ActiveArtifacts.Of(database).AnyAsync(a => a.Sha256 == sha256, cancellationToken).ConfigureAwait(false);

        if (referenced)
        {
            return false;
        }

        string path = ObjectPath(sha256);

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A download still reading the file keeps it open on Windows. The file stays, and an upload of the same
            // content later uses it again.
            LogObjectNotDeleted(path, exception);

            return false;
        }

        return true;
    }

    [LoggerMessage(EventId = 900, Level = LogLevel.Warning, Message = "Could not delete the stored file {Path}, which nothing uses any more")]
    private partial void LogObjectNotDeleted(string path, Exception exception);
}
