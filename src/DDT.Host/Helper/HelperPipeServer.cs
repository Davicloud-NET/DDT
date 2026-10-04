// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// Answers the web server on a named pipe that only its account may open, and nobody over the network. One request a
// connection and one connection at a time: a line of JSON in, lines of JSON out, the last with the exit code.
[SupportedOSPlatform("windows")]
public sealed partial class HelperPipeServer(
    string pipeName,
    SecurityIdentifier client,
    Func<HelperRequest, Action<string>, CancellationToken, Task<string?>> run,
    ILogger<HelperPipeServer> logger) : BackgroundService
{
    // A build request with a few hundred drivers stays far below this.
    private const int MaxRequestBytes = 256 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, Security());

            await using (pipe.ConfigureAwait(false))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                    await AnswerAsync(pipe, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // A broken request or connection must not stop the service. The next one gets a fresh pipe.
                    LogFailed(exception);
                }
            }
        }
    }

    private PipeSecurity Security()
    {
        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(client, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return security;
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using SemaphoreSlim writing = new(1, 1);
        string? problem;

        // Output comes from two streams at once, so each line is written under the lock
        void Line(string text) => Send(pipe, writing, new HelperMessage(Line: text));

        try
        {
            HelperRequest? request = JsonSerializer.Deserialize(await ReadLineAsync(pipe, cancellationToken).ConfigureAwait(false), HelperJsonContext.Default.HelperRequest);
            problem = request is null ? "The request is empty." : await run(request, Line, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Told to the web server, which shows it where the job was started.
            LogFailed(exception);
            problem = exception.Message;
        }

        Send(pipe, writing, new HelperMessage(ExitCode: problem is null ? 0 : 1, Problem: problem));

        try
        {
            pipe.WaitForPipeDrain();
        }
        catch (IOException)
        {
            // Nobody reads the end of it
        }
    }

    private static void Send(NamedPipeServerStream pipe, SemaphoreSlim writing, HelperMessage message)
    {
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, HelperJsonContext.Default.HelperMessage) + "\n");
        writing.Wait();

        try
        {
            // A web server that went away mid-build loses the output, and the build goes on
            if (pipe.IsConnected)
            {
                pipe.Write(line);
                pipe.Flush();
            }
        }
        catch (IOException)
        {
            // The same: nobody is reading any more
        }
        finally
        {
            writing.Release();
        }
    }

    private static async Task<string> ReadLineAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using MemoryStream line = new();
        byte[] buffer = new byte[4096];

        while (line.Length <= MaxRequestBytes)
        {
            int read = await pipe.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            int end = Array.IndexOf(buffer, (byte)'\n', 0, read);
            line.Write(buffer, 0, end < 0 ? read : end);

            if (end >= 0)
            {
                return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
            }
        }

        throw new InvalidDataException("The request is not one line of JSON.");
    }

    [LoggerMessage(EventId = 990, Level = LogLevel.Warning, Message = "A request to the DDT Helper failed")]
    private partial void LogFailed(Exception exception);
}
