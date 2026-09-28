// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// Downloads a file into a sink and resumes it after any interruption with a Range request from the sink's length.
// open asks the server for a file by token, SHA-256 and offset.
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

    // The part file keeps what arrived across restarts of the agent, and gets finalPath only once it matches.
    public async Task DownloadAsync(ContentFile file, string partPath, string finalPath, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        bool matches;
        FileDownloadSink part = new(partPath, file.SizeBytes);

        await using (part.ConfigureAwait(false))
        {
            matches = await DownloadAsync(file, part, percent, cancellationToken).ConfigureAwait(false);
        }

        if (!matches)
        {
            File.Delete(partPath);

            throw Mismatch(file.Name);
        }

        File.Move(partPath, finalPath, overwrite: true);
    }

    // True when all of the file arrived and matches its SHA-256. The sink's length is the one record of progress: every
    // byte it holds has been hashed, whatever interrupted the transfer.
    public async Task<bool> DownloadAsync(ContentFile file, IDownloadSink sink, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrEmpty(file.Name);
        ArgumentException.ThrowIfNullOrEmpty(file.Sha256);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(percent);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await sink.HashExistingAsync(hash, cancellationToken).ConfigureAwait(false);

        if (sink.Length > 0)
        {
            log.Information($"Resuming the download of {file.Name} at {ByteSize.Format(sink.Length)} of {ByteSize.Format(file.SizeBytes)}.");
        }

        Transfer transfer = new(sink, hash, new ByteProgress(percent, file.SizeBytes), timeProvider.GetTimestamp());
        transfer.Progress.Report(sink.Length);

        while (sink.Length < file.SizeBytes)
        {
            long before = sink.Length;
            string? interruption = await TryReceiveAsync(file, transfer, cancellationToken).ConfigureAwait(false);

            // Only a run of interruptions without progress backs off further and counts towards giving up.
            if (sink.Length > before)
            {
                transfer.Failures = 0;
                transfer.Progressed = timeProvider.GetTimestamp();
            }

            if (interruption is not null)
            {
                await WaitToResumeAsync(file, transfer, interruption, cancellationToken).ConfigureAwait(false);
            }
        }

        return sink.Length == file.SizeBytes
            && string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    // What interrupted the transfer, or null when it completed or starts over.
    private async Task<string?> TryReceiveAsync(ContentFile file, Transfer transfer, CancellationToken cancellationToken)
    {
        string token = tokens.Token;

        try
        {
            await ReceiveAsync(file, token, transfer, cancellationToken).ConfigureAwait(false);
            transfer.WaitedForToken = false;

            return transfer.Sink.Length < file.SizeBytes ? "the connection ended early" : null;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            log.Warning($"The server has less of {file.Name} than was already downloaded. Starting the download over.");
            transfer.Restart();

            return null;
        }
        catch (AgentTokenRejectedException)
        {
            // The heartbeat renews the token within a beat. A second refusal, or no new token, means the machine lost
            // its authorization.
            if (transfer.WaitedForToken
                || await tokens.WaitForOtherThanAsync(token, tokenWait, timeProvider, cancellationToken).ConfigureAwait(false) is null)
            {
                throw;
            }

            transfer.WaitedForToken = true;

            return null;
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            throw new DeploymentStepException(ServerCallRules.Reason(exception, $"the download of {file.Name}"), exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return exception is OperationCanceledException
                ? $"nothing arrived for {StallTimeout.TotalSeconds:0} s"
                : exception.Message;
        }
    }

    private async Task WaitToResumeAsync(ContentFile file, Transfer transfer, string interruption, CancellationToken cancellationToken)
    {
        if (timeProvider.GetElapsedTime(transfer.Progressed) >= GiveUpAfter)
        {
            throw new DeploymentStepException(
                $"The download of {file.Name} made no progress for {GiveUpAfter.TotalMinutes:0} minutes (last: {interruption}).");
        }

        transfer.Failures++;
        TimeSpan delay = AgentLimits.RetryDelay(transfer.Failures);
        log.Warning(
            $"The download of {file.Name} was interrupted at {ByteSize.Format(transfer.Sink.Length)} ({interruption}). " +
            $"Resuming in {delay.TotalSeconds:0} s.");
        await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    // One request, appending what arrives to the sink, which may be less than the rest when the connection ends early.
    private async Task ReceiveAsync(ContentFile file, string token, Transfer transfer, CancellationToken cancellationToken)
    {
        using StallGuard stall = new(timeProvider, cancellationToken);

        long offset = transfer.Sink.Length;
        stall.Arm();
        AgentImageStream content = await open(token, file.Sha256, offset, stall.Token).ConfigureAwait(false);

        await using (content.ConfigureAwait(false))
        {
            offset = CheckStart(file, content, offset, transfer);
            byte[] buffer = new byte[BufferSize];

            while (offset < file.SizeBytes)
            {
                stall.Arm();
                int read;

                try
                {
                    read = await content.Content.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
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

                int usable = (int)Math.Min(read, file.SizeBytes - offset);

                // A raw disk image's zstd can turn a few kilobytes of zeros into gigabytes to write.
                stall.Disarm();
                await transfer.Sink.WriteAsync(buffer.AsMemory(0, usable), cancellationToken).ConfigureAwait(false);
                transfer.Hash.AppendData(buffer, 0, usable);
                offset += usable;
                transfer.Progress.Report(offset);
            }
        }
    }

    // The offset the content starts at, after checking it is the part asked for.
    private long CheckStart(ContentFile file, AgentImageStream content, long offset, Transfer transfer)
    {
        // A server may ignore the range and send everything, which RFC 9110 allows.
        if (content.Offset == 0 && offset > 0)
        {
            log.Warning($"The server sent all of {file.Name} instead of the missing part. Starting the download over.");
            transfer.Restart();
            offset = 0;
            transfer.Progress.Report(0);
        }

        if (content.Offset != offset)
        {
            throw new DeploymentStepException($"The server sent {file.Name} from byte {content.Offset} when byte {offset} was asked for.");
        }

        if (content.TotalLength >= 0 && content.TotalLength != file.SizeBytes)
        {
            throw new DeploymentStepException(
                $"The server's file for {file.Name} holds {content.TotalLength} bytes, but {file.SizeBytes} were announced. Upload {file.Name} again.");
        }

        return offset;
    }

    // One download's sink, the hash of what it holds, and how its attempts fared since it last made progress.
    private sealed class Transfer(IDownloadSink sink, IncrementalHash hash, ByteProgress progress, long progressed)
    {
        public IDownloadSink Sink => sink;

        public IncrementalHash Hash => hash;

        public ByteProgress Progress => progress;

        public int Failures { get; set; }

        public bool WaitedForToken { get; set; }

        public long Progressed { get; set; } = progressed;

        public void Restart()
        {
            sink.Restart();
            hash.GetHashAndReset();
        }
    }

    // Cancels a read that waits longer than StallTimeout for the server. Writing to the sink does not count.
    private sealed class StallGuard : IDisposable
    {
        private readonly CancellationTokenSource _stall;
        private readonly CancellationTokenSource _reading;

        public StallGuard(TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            _stall = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
            _reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stall.Token);
        }

        public CancellationToken Token => _reading.Token;

        public void Arm() => _stall.CancelAfter(StallTimeout);

        public void Disarm() => _stall.CancelAfter(Timeout.InfiniteTimeSpan);

        public void Dispose()
        {
            _reading.Dispose();
            _stall.Dispose();
        }
    }
}
