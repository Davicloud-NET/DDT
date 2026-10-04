// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace DDT.Server.BootImage;

// Talks to the helper over its named pipe, which only the DDT service's account may open. One request a connection:
// a line of JSON there, lines of JSON back.
public sealed class PipeBootImageHelper(string pipeName) : IBootImageHelper
{
    public const string PipeName = "DDT.Helper";

    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(5);

    public PipeBootImageHelper()
        : this(PipeName)
    {
    }

    // Listing the pipes does not connect to one, which File.Exists would.
    public bool Available
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                return Directory.EnumerateFiles(@"\\.\pipe\")
                    .Any(pipe => string.Equals(Path.GetFileName(pipe), pipeName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public async IAsyncEnumerable<HelperMessage> RunAsync(HelperRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Whoever holds the pipe may learn who connects, never act as the DDT service
        NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

        await using (pipe.ConfigureAwait(false))
        {
            await pipe.ConnectAsync(s_connectTimeout, cancellationToken).ConfigureAwait(false);

            byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, HelperJsonContext.Default.HelperRequest) + "\n");
            await pipe.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            using StreamReader reader = new(pipe, Encoding.UTF8);

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } answer)
            {
                if (JsonSerializer.Deserialize(answer, HelperJsonContext.Default.HelperMessage) is not { } message)
                {
                    continue;
                }

                yield return message;

                if (message.ExitCode is not null)
                {
                    yield break;
                }
            }
        }
    }
}
