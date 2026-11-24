// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Import;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Import;
using Microsoft.Extensions.Logging;

namespace DDT.Server.WindowsPackages;

// A Languages and Optional Features ISO uploaded in the browser goes into the store's import folder. The languages are
// chosen and imported from there, as from an ISO copied there, and it stays for the next language.
public sealed partial class LanguageIsoUploads(
    ImageStore store,
    ImportFolders folders,
    UploadRefusals refusals,
    ILogger<ImageUploadCompleter> logger)
{
    public async Task<UploadCompletion> PlaceAsync(DdtDbContext database, ImageUpload upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(upload);

        string part = store.PartPath(upload.Id);

        if (await LanguageIsos.ListAsync(part, cancellationToken).ConfigureAwait(false) is null)
        {
            return await refusals.RefuseAsync(database, upload, ServerMessages.IsoNoLanguages.With(), cancellationToken).ConfigureAwait(false);
        }

        string target = FreePath(folders.Default, upload.FileName);
        File.Move(part, target);
        FileInfo placed = new(target);
        LogPlaced(upload.Id, target);

        return await refusals.EndAsync(
            database,
            upload,
            new UploadCompletion(
                UploadCompletionStatus.Added,
                [],
                LanguageIso: new ImportFile(placed.FullName, placed.Name, placed.Length, placed.LastWriteTimeUtc)),
            cancellationToken).ConfigureAwait(false);
    }

    // The upload's name, with a number added when the folder has a file of that name.
    private static string FreePath(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        string name = string.Join('_', Path.GetFileName(fileName.Replace('\\', '/')).Split(Path.GetInvalidFileNameChars()));
        string stem = Path.GetFileNameWithoutExtension(name) is { Length: > 0 } given ? given : "languages";
        string path = Path.Combine(folder, stem + ".iso");

        for (int number = 2; File.Exists(path); number++)
        {
            path = Path.Combine(folder, $"{stem} ({number}).iso");
        }

        return path;
    }

    [LoggerMessage(EventId = 913, Level = LogLevel.Information, Message = "Put the Languages and Optional Features ISO of upload {UploadId} at {Path}")]
    private partial void LogPlaced(Guid uploadId, string path);
}
