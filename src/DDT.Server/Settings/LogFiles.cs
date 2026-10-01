// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Server;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Settings;

// The log files in the store. A Windows service has no console, so it writes them, and the Server page lists them for
// download. A container logs to its console and has none.
public sealed class LogFiles(IOptions<DdtOptions> options)
{
    public const string Pattern = "ddt-*.log";

    public string Folder => FolderIn(options.Value.StorePath);

    public static string FolderIn(string storePath) => Path.GetFullPath(Path.Combine(storePath, "logs"));

    // Newest first
    public IReadOnlyList<LogFileView> List()
    {
        DirectoryInfo folder = new(Folder);

        return folder.Exists
            ? [.. folder.EnumerateFiles(Pattern)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .Select(file => new LogFileView(file.Name, file.Length, file.LastWriteTimeUtc))]
            : [];
    }

    // Only a file the list names, so a request can't name a path. Null for anything else. Opened so that the server
    // can go on writing it.
    public FileStream? Open(string name)
    {
        if (!List().Any(file => string.Equals(file.Name, name, StringComparison.Ordinal)))
        {
            return null;
        }

        try
        {
            return new FileStream(
                Path.Combine(Folder, name),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            // Pruned since the list was read
            return null;
        }
    }
}
