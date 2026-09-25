// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// Downloads a file, such as an image or a package, into a sink and resumes it after any interruption: the rest is asked
// for with a Range request from where the sink is. A part file keeps what arrived across restarts of the agent, and
// only gets its final name once its length and SHA-256 match what the server announced; a raw disk image goes onto the
// disk as it arrives. open asks the server for a file by token, SHA-256 and offset.
public sealed class ContentDownloader(
    Func<string, string, long, CancellationToken, Task<AgentImageStream>> open,
    DeploymentTokens tokens,
    AgentLog log,
    TimeProvider timeProvider,
    TimeSpan tokenWait)
{
    // A connection that drops without a reset, as a VPN can, would otherwise wait for ever.
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    // As long as a server outage may last. While the progress reports get through, the tokens stay valid, so a
    // failure of the transfer alone would otherwise be retried for ever.
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);

    private const int BufferSize = 1024 * 1024;

    public static DeploymentStepException Mismatch(string name) => new(
        $"The download of {name} does not match the SHA-256 the server announced. Start again; if it fails again, upload {name} again.");

    // name says what the file is in messages, such as "Windows 11 Pro" or "package Dell drivers".
    public async Task DownloadAsync(
        string name,
        string sha256,
        long sizeBytes,
        string partPath,
        string finalPath,
        IProgress<int> percent,
        CancellationToken cancellationToken)
    {
        bool matches;
        FileDownloadSink part = new(partPath, sizeBytes);

        await using (part.ConfigureAwait(false))
        {
            matches = await DownloadAsync(name, sha256, sizeBytes, part, percent, cancellationToken).ConfigureAwait(false);
        }

        if (!matches)
        {
            File.Delete(partPath);

            throw Mismatch(name);
        }

        File.Move(partPath, finalPath, overwrite: true);
    }

    // True when all sizeBytes arrived and match sha256. The sink's length is the one record of progress: every byte it
    // holds has been hashed, whatever interrupted the transfer.
    public async Task<bool> DownloadAsync(
        string name,
        string sha256,
        long sizeBytes,
        IDownloadSink sink,
        IProgress<int> percent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(percent);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await sink.HashExistingAsync(hash, cancellationToken).ConfigureAwait(false);

        if (sink.Length > 0)
        {
            log.Information($"Resuming the download of {name} at {ByteSize.Format(sink.Length)} of {ByteSize.Format(sizeBytes)}.");
        }

        ByteProgress progress = new(percent, sizeBytes);
        progress.Report(sink.Length);

        int failures = 0;
        bool waitedForToken = false;
        long progressed = timeProvider.GetTimestamp();

        while (sink.Length < sizeBytes)
        {
            string token = tokens.Token;
            long before = sink.Length;
            string? interruption = null;

            try
            {
                await ReceiveAsync(name, token, sha256, sizeBytes, sink, hash, progress, cancellationToken).ConfigureAwait(false);
                waitedForToken = false;

                if (sink.Length < sizeBytes)
                {
                    interruption = "the connection ended early";
                }
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                log.Warning($"The server has less of {name} than was already downloaded. Starting the download over.");
                Restart(sink, hash);
            }
            catch (AgentTokenRejectedException)
            {
                // The heartbeat renews the token; one that expired while the download waited is replaced within a
                // beat. A second refusal, or no new token, means the machine lost its authorization.
                if (waitedForToken || await tokens.WaitForOtherThanAsync(token, tokenWait, timeProvider, cancellationToken).ConfigureAwait(false) is null)
                {
                    throw;
                }

                waitedForToken = true;
            }
            catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
            {
                throw new DeploymentStepException(ServerCallRules.Reason(exception, $"the download of {name}"), exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                interruption = exception is OperationCanceledException
                    ? $"nothing arrived for {StallTimeout.TotalSeconds:0} s"
                    : exception.Message;
            }

            // Only a run of interruptions without progress backs off further and counts towards giving up.
            if (sink.Length > before)
            {
                failures = 0;
                progressed = timeProvider.GetTimestamp();
            }

            if (interruption is not null)
            {
                if (timeProvider.GetElapsedTime(progressed) >= GiveUpAfter)
                {
                    throw new DeploymentStepException(
                        $"The download of {name} made no progress for {GiveUpAfter.TotalMinutes:0} minutes (last: {interruption}).");
                }

                failures++;
                TimeSpan delay = AgentLimits.RetryDelay(failures);
                log.Warning(
                    $"The download of {name} was interrupted at {ByteSize.Format(sink.Length)} ({interruption}). " +
                    $"Resuming in {delay.TotalSeconds:0} s.");
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        return sink.Length == sizeBytes
            && string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void Restart(IDownloadSink sink, IncrementalHash hash)
    {
        sink.Restart();
        hash.GetHashAndReset();
    }

    // One request, appending what arrives to the sink, which may be less than the rest when the connection ends early.
    private async Task ReceiveAsync(
        string name,
        string token,
        string sha256,
        long sizeBytes,
        IDownloadSink sink,
        IncrementalHash hash,
        ByteProgress progress,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource stall = new(Timeout.InfiniteTimeSpan, timeProvider);
        using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);

        long offset = sink.Length;
        stall.CancelAfter(StallTimeout);
        AgentImageStream content = await open(token, sha256, offset, reading.Token).ConfigureAwait(false);

        await using (content.ConfigureAwait(false))
        {
            // A server may ignore the range and send everything, which RFC 9110 allows.
            if (content.Offset == 0 && offset > 0)
            {
                log.Warning($"The server sent all of {name} instead of the missing part. Starting the download over.");
                Restart(sink, hash);
                offset = 0;
                progress.Report(0);
            }

            if (content.Offset != offset)
            {
                throw new DeploymentStepException($"The server sent {name} from byte {content.Offset} when byte {offset} was asked for.");
            }

            if (content.TotalLength >= 0 && content.TotalLength != sizeBytes)
            {
                throw new DeploymentStepException(
                    $"The server's file for {name} holds {content.TotalLength} bytes, but {sizeBytes} were announced. Upload {name} again.");
            }

            byte[] buffer = new byte[BufferSize];

            while (offset < sizeBytes)
            {
                stall.CancelAfter(StallTimeout);
                int read;

                try
                {
                    read = await content.Content.ReadAsync(buffer, reading.Token).ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    // Only a failed receive is worth resuming; a failed write to the disk is not.
                    throw new HttpRequestException(exception.Message, exception);
                }

                if (read == 0)
                {
                    break;
                }

                int usable = (int)Math.Min(read, sizeBytes - offset);

                // The stall counts only the wait for the server. Writing to the sink can take long by itself: a raw disk
                // image's zstd turns a few kilobytes of zeros into gigabytes to write.
                stall.CancelAfter(Timeout.InfiniteTimeSpan);
                await sink.WriteAsync(buffer.AsMemory(0, usable), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, usable);
                offset += usable;
                progress.Report(offset);
            }
        }
    }
}
