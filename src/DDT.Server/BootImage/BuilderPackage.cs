// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DDT.Server.Images;
using DDT.Server.Machines;

namespace DDT.Server.BootImage;

// The builder: a zip that builds this server's boot image on any Windows PC with the ADK and uploads it. It holds the
// build script the server came with, its agent, console and root, the flagged drivers and builder.json.
public sealed class BuilderPackage(BundledReleases bundled, ImageStore store)
{
    public const string ScriptName = "Build-BootImage.ps1";
    public const string FileName = "builder.json";

    private const string ModuleFolder = "BootImage";
    private const string TrimList = "boot-image-trim.txt";
    private const string Drivers = "drivers";

    // Elevates itself, because DISM needs an administrator. fltmc answers only to one.
    private const string Command =
        "@echo off\r\n" +
        "rem Builds DDT's boot image for the server this builder came from, and uploads it there.\r\n" +
        "rem Takes a Windows PC with the Windows ADK and its Windows PE add-on.\r\n" +
        "fltmc >nul 2>&1\r\n" +
        "if errorlevel 1 (\r\n" +
        "    powershell.exe -NoProfile -Command \"Start-Process -FilePath '%~f0' -Verb RunAs\"\r\n" +
        "    exit /b\r\n" +
        ")\r\n" +
        "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0Build-BootImage.ps1\"\r\n" +
        "echo.\r\n" +
        "pause\r\n";

    private string Script => Path.Combine(bundled.Folder, ScriptName);

    private string Module => Path.Combine(bundled.Folder, ModuleFolder);

    // A server run from source has no script next to it, and one built without the agent has nothing to put in.
    public bool Available => File.Exists(Script) && Directory.Exists(Module) && File.Exists(bundled.AgentPath);

    // The zip as a file that goes away when it is closed.
    public async Task<FileStream> CreateAsync(BuilderFile file, string rootPem, HelperDriverList drivers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(drivers);

        Directory.CreateDirectory(store.UploadsDirectory);
        FileStream zip = new(
            Path.Combine(store.UploadsDirectory, $"builder-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
            using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddScriptAsync(archive, cancellationToken).ConfigureAwait(false);
                await AddTextAsync(archive, "Build.cmd", Command, cancellationToken).ConfigureAwait(false);
                await AddTextAsync(archive, "ddt-root.pem", rootPem, cancellationToken).ConfigureAwait(false);
                await AddTextAsync(archive, FileName, JsonSerializer.Serialize(file, BuilderJsonContext.Default.BuilderFile), cancellationToken).ConfigureAwait(false);
                await AddDriversAsync(archive, drivers, cancellationToken).ConfigureAwait(false);
            }

            zip.Position = 0;

            return zip;
        }
        catch
        {
            await zip.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private async Task AddScriptAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        await AddFileAsync(archive, ScriptName, Script, cancellationToken).ConfigureAwait(false);
        await AddFileAsync(archive, "ddt-agent.exe", bundled.AgentPath, cancellationToken).ConfigureAwait(false);

        foreach (string optional in new[] { Path.Combine(bundled.Folder, TrimList), bundled.ConsolePath })
        {
            if (File.Exists(optional))
            {
                await AddFileAsync(archive, Path.GetFileName(optional), optional, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (string path in Directory.EnumerateFiles(Module, "*", SearchOption.AllDirectories))
        {
            string name = $"{ModuleFolder}/{Path.GetRelativePath(Module, path).Replace('\\', '/')}";
            await AddFileAsync(archive, name, path, cancellationToken).ConfigureAwait(false);
        }
    }

    // Unpacked, as Build-BootImage.ps1 -ServerDriverPath takes them, so the builder needs no token to download them.
    private async Task AddDriversAsync(ZipArchive archive, HelperDriverList drivers, CancellationToken cancellationToken)
    {
        if (drivers.Drivers.Count == 0)
        {
            return;
        }

        await AddTextAsync(archive, $"{Drivers}/drivers.json", JsonSerializer.Serialize(drivers, HelperJsonContext.Default.HelperDriverList), cancellationToken).ConfigureAwait(false);

        foreach (HelperDriver driver in drivers.Drivers)
        {
            using ZipArchive package = ZipFile.OpenRead(store.ObjectPath(driver.Sha256));

            foreach (ZipArchiveEntry entry in package.Entries.Where(entry => Stays(entry.FullName)))
            {
                Stream source = entry.Open();

                await using (source.ConfigureAwait(false))
                {
                    string name = $"{Drivers}/{driver.PackageId:D}/{entry.FullName.Replace('\\', '/')}";
                    await AddAsync(archive, name, source, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    // A file's name inside its package: no folder entry, and nothing that leaves the package's folder.
    private static bool Stays(string name) =>
        !name.EndsWith('/')
        && !name.EndsWith('\\')
        && !Path.IsPathRooted(name)
        && !name.Contains(':', StringComparison.Ordinal)
        && !name.Split('/', '\\').Contains("..");

    private static async Task AddFileAsync(ZipArchive archive, string name, string path, CancellationToken cancellationToken)
    {
        FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

        await using (source.ConfigureAwait(false))
        {
            await AddAsync(archive, name, source, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AddTextAsync(ZipArchive archive, string name, string text, CancellationToken cancellationToken)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(text));
        await AddAsync(archive, name, source, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddAsync(ZipArchive archive, string name, Stream source, CancellationToken cancellationToken)
    {
        Stream target = archive.CreateEntry(name, CompressionLevel.Fastest).Open();

        await using (target.ConfigureAwait(false))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }
}
