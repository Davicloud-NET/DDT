// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Deployments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Images;

// Uploads are staged on the same volume as the library, so finishing one is a rename. The objects are images and
// packages alike, each stored once by its hash.
public sealed partial class ImageStore(IOptions<DdtOptions> options, ILogger<ImageStore> logger)
{
    public string ObjectsDirectory => Path.GetFullPath(Path.Combine(options.Value.StorePath, "images", "objects"));

    public string UploadsDirectory => Path.GetFullPath(Path.Combine(options.Value.StorePath, "images", "uploads"));

    // Held while stored files and the rows that refer to them change together: the commit of a completed upload,
    // the removal of an image or a package, an assignment or a pick that refers to them, and the creation of an
    // upload session, whose free space check counts the others.
    public SemaphoreSlim LibraryLock { get; } = new(1, 1);

    public string ObjectPath(string sha256) => Path.Combine(ObjectsDirectory, sha256);

    public string PartPath(Guid uploadId) => Path.Combine(UploadsDirectory, $"{uploadId:N}.part");

    // On Linux DriveInfo measures the file system of the path it is given, which is the store volume.
    public DriveInfo Volume() => new(Directory.CreateDirectory(UploadsDirectory).FullName);

    // Call with LibraryLock held, so no completed upload can add a row for this hash in between.
    public async Task<bool> DeleteObjectIfUnreferencedAsync(DdtDbContext database, string sha256, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        bool referenced = await database.Images.AnyAsync(i => i.Sha256 == sha256, cancellationToken).ConfigureAwait(false)
            || await database.Packages.AnyAsync(p => p.Sha256 == sha256, cancellationToken).ConfigureAwait(false)
            || await database.Deployments
                .AnyAsync(
                    d => d.Sha256 == sha256 && (d.State == DeploymentState.Assigned || d.State == DeploymentState.Running),
                    cancellationToken)
                .ConfigureAwait(false)
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
