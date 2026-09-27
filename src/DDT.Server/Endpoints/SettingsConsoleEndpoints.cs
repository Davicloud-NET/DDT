// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Settings;
using DDT.Server.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

// The graphical console the agents of netbooting machines switch to, uploaded on the settings page as the agent is: a
// zip of the folder Publish-Console.ps1 writes. It runs as SYSTEM in Windows PE and as the shell of DDT's session in the
// installed Windows, so the upload needs the same fresh proof of identity.
public static class SettingsConsoleEndpoints
{
    // The console's three files are about 29 MB, and zipped about 12 MB. The limit is the agent's, for the zip and for
    // what it unpacks to, so a small zip cannot fill the store either.
    public const long MaxConsoleBytes = SettingsEndpoints.MaxAgentBytes;

    public static RouteGroupBuilder MapSettingsConsoleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/agent/console", ReadConsoleAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/agent/console", UploadConsoleAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<AgentBinaryView>> ReadConsoleAsync(
        ConsoleReleaseStore consoles,
        IOptions<AgentReleaseOptions> options,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        ConsoleRelease? release = await consoles.CurrentAsync(cancellationToken).ConfigureAwait(false);
        bool configured = !string.IsNullOrWhiteSpace(options.Value.ConsolePath);

        AuditEvent? upload = configured || release is null
            ? null
            : await database.AuditEvents
                .AsNoTracking()
                .Where(audit => audit.Action == AuditActions.ConsoleUploaded)
                .OrderByDescending(audit => audit.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        return TypedResults.Ok(new AgentBinaryView(
            release?.Files[0].Sha256,
            release?.Files.Sum(file => file.Size),
            upload?.OccurredUtc,
            upload?.ActorName,
            configured ? AgentBinarySource.Configuration : release is null ? AgentBinarySource.None : AgentBinarySource.Uploaded));
    }

    // The zip is taken whether the files are at its root or in one folder, as zipping the folder makes it, and stored with
    // exactly the console's files at the root, written next to the zip it replaces and renamed over it.
    private static async Task<IResult> UploadConsoleAsync(
        HttpContext context,
        ClaimsPrincipal user,
        ConsoleReleaseStore consoles,
        IOptions<AgentReleaseOptions> options,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        DdtDbContext database,
        TimeProvider timeProvider,
        LiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.ConsolePath))
        {
            return ServerProblems.Problem(ServerMessages.SettingsConsoleConfigured.With(), StatusCodes.Status409Conflict);
        }

        if (!await reauthentication.ValidAsync(context, user, users).ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["console"]);
        }

        if (context.Request.ContentLength > MaxConsoleBytes)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxConsoleBytes;
        }

        string path = consoles.PackagePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string received = $"{path}.{Guid.NewGuid():N}.upload";
        string stored = $"{path}.{Guid.NewGuid():N}.upload";
        ConsoleRelease? release;

        try
        {
            await using (FileStream file = new(received, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                if (await CopyAtMostAsync(context.Request.Body, file, MaxConsoleBytes, cancellationToken).ConfigureAwait(false) < 0)
                {
                    return TooLarge();
                }
            }

            switch (await RepackAsync(received, stored, cancellationToken).ConfigureAwait(false))
            {
                case Repacked.NotAConsole:
                    return ServerProblems.Validation("package", ServerMessages.SettingsConsoleNotAPackage.With());
                case Repacked.TooLarge:
                    return TooLarge();
            }

            await using (FileStream file = new(stored, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                release = await ConsoleReleaseStore.ReadAsync(file, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The repacked console is not one.");
            }

            File.Move(stored, path, overwrite: true);
        }
        finally
        {
            File.Delete(received);
            File.Delete(stored);
        }

        string sha256 = release.Files[0].Sha256;
        long size = release.Files.Sum(file => file.Size);

        AuditEvent audit = new()
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = AuditActions.ConsoleUploaded,
            ActorUserId = Principals.UserId(user),
            ActorName = Principals.ActorName(user),
            SubjectId = sha256,
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = $"Uploaded the console with ddt-console.exe of SHA-256 {sha256}, {size} bytes in all. Netbooting machines show it from their next boot.",
        };

        database.AuditEvents.Add(audit);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = new(sha256, size, audit.OccurredUtc, audit.ActorName, AgentBinarySource.Uploaded);
        live.ConsoleChanged(uploaded);

        return TypedResults.Ok(uploaded);
    }

    private enum Repacked
    {
        Done,
        NotAConsole,
        TooLarge,
    }

    // Copies the console's files from the zip received into a new one, and checks that each is a Windows executable.
    private static async Task<Repacked> RepackAsync(string received, string stored, CancellationToken cancellationToken)
    {
        await using FileStream source = new(received, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        ZipArchive input;

        try
        {
            input = await ZipArchive.CreateAsync(source, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Repacked.NotAConsole;
        }

        await using (input)
        {
            if (ConsoleFiles(input) is not { } files)
            {
                return Repacked.NotAConsole;
            }

            long left = MaxConsoleBytes;
            await using FileStream target = new(stored, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            await using ZipArchive output = await ZipArchive.CreateAsync(target, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);

            foreach (string name in ConsoleRelease.FileNames)
            {
                ZipArchiveEntry entry = output.CreateEntry(name, CompressionLevel.Optimal);
                byte[] header = new byte[2];
                long copied;

                try
                {
                    await using Stream from = await files[name].OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using Stream to = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await from.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

                    // Every Windows executable and library starts with the DOS header's MZ.
                    if (header[0] != (byte)'M' || header[1] != (byte)'Z')
                    {
                        return Repacked.NotAConsole;
                    }

                    await to.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                    copied = await CopyAtMostAsync(from, to, left - header.Length, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    return Repacked.NotAConsole;
                }

                if (copied < 0)
                {
                    return Repacked.TooLarge;
                }

                left -= header.Length + copied;
            }
        }

        return Repacked.Done;
    }

    // The console's files by name, whether at the root or all in one folder, or null when the zip holds anything else or
    // misses one.
    private static Dictionary<string, ZipArchiveEntry>? ConsoleFiles(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);
        string? folder = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');

            // Folders are entries of their own, ending in a slash.
            if (path.EndsWith('/'))
            {
                continue;
            }

            int slash = path.LastIndexOf('/');
            string directory = slash < 0 ? string.Empty : path[..slash];
            folder ??= directory;

            string? name = ConsoleRelease.FileNames.FirstOrDefault(known => known.Equals(path[(slash + 1)..], StringComparison.OrdinalIgnoreCase));

            if (directory != folder || directory.Contains('/', StringComparison.Ordinal) || name is null || !files.TryAdd(name, entry))
            {
                return null;
            }
        }

        return files.Count == ConsoleRelease.FileNames.Count ? files : null;
    }

    // The number of bytes copied, or -1 when there were more than limit.
    private static async Task<long> CopyAtMostAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long size = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            size += read;

            if (size > limit)
            {
                return -1;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return size;
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SettingsConsoleTooLarge.With("max", MaxConsoleBytes / (1024 * 1024)),
            StatusCodes.Status413PayloadTooLarge);
}
