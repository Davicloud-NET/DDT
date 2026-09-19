using System.Net;
using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// Downloads an image into a part file and resumes it after any interruption: the bytes already there are hashed
// again and the rest is asked for with a Range request. The file only gets its final name once its length and
// SHA-256 match what the server announced.
public sealed class ImageDownloader(IAgentServer server, DeploymentTokens tokens, AgentLog log, TimeProvider timeProvider, TimeSpan tokenWait)
{
    // A connection that drops without a reset, as a VPN can, would otherwise wait for ever.
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    // As long as a server outage may last. While the progress reports get through, the tokens stay valid, so a
    // failure of the image transfer alone would otherwise be retried for ever.
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);

    private const int BufferSize = 1024 * 1024;

    public async Task DownloadAsync(
        Guid machineId,
        string sha256,
        long sizeBytes,
        string partPath,
        string finalPath,
        IProgress<int> percent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        ArgumentNullException.ThrowIfNull(percent);

        FileStream file = new(partPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 0, useAsync: true);
        IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bool matches;

        try
        {
            if (file.Length > sizeBytes)
            {
                file.SetLength(0);
            }

            // The file's length is the one record of progress: every byte in it has been hashed, whatever
            // interrupted the transfer.
            await HashExistingAsync(file, hash, cancellationToken).ConfigureAwait(false);

            if (file.Length > 0)
            {
                log.Information($"Resuming the download at {ByteSize.Format(file.Length)} of {ByteSize.Format(sizeBytes)}.");
            }

            ByteProgress progress = new(percent, sizeBytes);
            progress.Report(file.Length);

            int failures = 0;
            bool waitedForToken = false;
            long progressed = timeProvider.GetTimestamp();

            while (file.Length < sizeBytes)
            {
                string token = tokens.Token;
                long before = file.Length;
                string? interruption = null;

                try
                {
                    await ReceiveAsync(machineId, token, sha256, sizeBytes, file, hash, progress, cancellationToken).ConfigureAwait(false);
                    waitedForToken = false;

                    if (file.Length < sizeBytes)
                    {
                        interruption = "the connection ended early";
                    }
                }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    log.Warning("The server has less of this image than was already downloaded. Starting the download over.");
                    Restart(file, hash);
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
                    throw new DeploymentStepException(ServerCallRules.Reason(exception, "the image download"), exception);
                }
                catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                    || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    interruption = exception is OperationCanceledException
                        ? $"nothing arrived for {StallTimeout.TotalSeconds:0} s"
                        : exception.Message;
                }

                // Only a run of interruptions without progress backs off further and counts towards giving up.
                if (file.Length > before)
                {
                    failures = 0;
                    progressed = timeProvider.GetTimestamp();
                }

                if (interruption is not null)
                {
                    if (timeProvider.GetElapsedTime(progressed) >= GiveUpAfter)
                    {
                        throw new DeploymentStepException(
                            $"The image download made no progress for {GiveUpAfter.TotalMinutes:0} minutes (last: {interruption}).");
                    }

                    failures++;
                    TimeSpan delay = AgentLimits.RetryDelay(failures);
                    log.Warning($"The download was interrupted at {ByteSize.Format(file.Length)} ({interruption}). Resuming in {delay.TotalSeconds:0} s.");
                    await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }

            matches = file.Length == sizeBytes
                && string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            hash.Dispose();
            await file.DisposeAsync().ConfigureAwait(false);
        }

        if (!matches)
        {
            File.Delete(partPath);

            throw new DeploymentStepException("The downloaded image does not match the SHA-256 the server announced. Start the deployment again; if it fails again, upload the image again.");
        }

        File.Move(partPath, finalPath, overwrite: true);
    }

    private static async Task HashExistingAsync(FileStream file, IncrementalHash hash, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        file.Position = 0;
        int read;

        while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    private static void Restart(FileStream file, IncrementalHash hash)
    {
        file.SetLength(0);
        hash.GetHashAndReset();
    }

    // One request, appending what arrives to the file, which may be less than the rest when the connection ends
    // early.
    private async Task ReceiveAsync(
        Guid machineId,
        string token,
        string sha256,
        long sizeBytes,
        FileStream file,
        IncrementalHash hash,
        ByteProgress progress,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource stall = new(Timeout.InfiniteTimeSpan, timeProvider);
        using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);

        long offset = file.Length;
        stall.CancelAfter(StallTimeout);
        AgentImageStream image = await server.OpenImageAsync(machineId, token, sha256, offset, reading.Token).ConfigureAwait(false);

        await using (image.ConfigureAwait(false))
        {
            // A server may ignore the range and send everything, which RFC 9110 allows.
            if (image.Offset == 0 && offset > 0)
            {
                log.Warning("The server sent the whole image instead of the missing part. Starting the download over.");
                Restart(file, hash);
                offset = 0;
                progress.Report(0);
            }

            if (image.Offset != offset)
            {
                throw new DeploymentStepException($"The server sent the image from byte {image.Offset} when byte {offset} was asked for.");
            }

            if (image.TotalLength >= 0 && image.TotalLength != sizeBytes)
            {
                throw new DeploymentStepException(
                    $"The server's image file holds {image.TotalLength} bytes, but the deployment expects {sizeBytes}. Upload the image again.");
            }

            byte[] buffer = new byte[BufferSize];
            file.Position = offset;

            while (offset < sizeBytes)
            {
                stall.CancelAfter(StallTimeout);
                int read;

                try
                {
                    read = await image.Content.ReadAsync(buffer, reading.Token).ConfigureAwait(false);
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
                await file.WriteAsync(buffer.AsMemory(0, usable), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, usable);
                offset += usable;
                progress.Report(offset);
            }
        }
    }
}
