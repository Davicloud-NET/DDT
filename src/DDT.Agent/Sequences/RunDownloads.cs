// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// Downloads the run's images and packages from the server by their SHA-256, and unpacks packages. tokenWait is how
// long a download waits for the heartbeat's next token after a refused one.
public sealed class RunDownloads(IAgentServer server, RunSession session, AgentLog log, TimeProvider timeProvider, TimeSpan tokenWait)
{
    // Into directory as <sha256><extension>, resuming a part file an earlier attempt left there. Returns the file.
    public async Task<string> DownloadAsync(
        ContentFile content,
        string directory,
        string extension,
        IProgress<int> percent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        string hash = Checked(content);
        string file = Path.Combine(directory, hash + extension);
        Directory.CreateDirectory(directory);

        log.Information($"Downloading {content.Name} ({ByteSize.Format(content.SizeBytes)}).");
        await Downloader().DownloadAsync(content with { Sha256 = hash }, Path.Combine(directory, hash + ".part"), file, percent, cancellationToken)
            .ConfigureAwait(false);

        return file;
    }

    // Into sink, which does something with the bytes as they come, such as write a raw disk image. Throws when they
    // do not match the SHA-256 the server announced.
    public async Task DownloadToAsync(ContentFile content, IDownloadSink sink, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!await Downloader().DownloadAsync(content with { Sha256 = Checked(content) }, sink, percent, cancellationToken).ConfigureAwait(false))
        {
            throw ContentDownloader.Mismatch(content.Name);
        }
    }

    // Downloads the package into cacheDirectory and unpacks it into target, which is replaced. The zip is deleted
    // either way.
    public async Task UnpackAsync(AgentRunPackage package, string cacheDirectory, string target, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        string name = $"package {package.Name}";
        string zip = await DownloadAsync(new ContentFile(name, package.Sha256, package.SizeBytes), cacheDirectory, ".zip", percent, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            long available = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!).AvailableFreeSpace;
            await PackageExtractor.ExtractAsync(zip, target, name, available, cancellationToken).ConfigureAwait(false);
            log.Information($"Unpacked {name} into {target}.");
        }
        finally
        {
            Leftovers.Delete(zip, log);
        }
    }

    // The hash names files on the disk and a route on the server, so it has to be exactly that. Returns it lower case.
    private static string Checked(ContentFile content)
    {
        ArgumentNullException.ThrowIfNull(content.Sha256);

        if (content.Sha256.Length != 64 || !content.Sha256.All(char.IsAsciiHexDigit))
        {
            throw new DeploymentStepException($"The server names {content.Name} by \"{content.Sha256}\", which is no SHA-256. Assign the sequence again.");
        }

        return content.Sha256.ToLowerInvariant();
    }

    private ContentDownloader Downloader() => new(
        (token, content, offset, call) => server.OpenRunFileAsync(session.MachineId, token, new RunFileRange(session.Run.Id, content, offset), call),
        session.Tokens,
        log,
        timeProvider,
        tokenWait);
}
