// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Netboot;
using DDT.Server.BootImage;

namespace DDT.Server.Netboot;

// Asks the DDT Helper service for what needs an administrator on this computer's DHCP server and WDS. Each request is
// short, so its answer is collected here and not followed line by line.
public sealed class NetbootHelper(IBootImageHelper helper)
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(5);

    public bool Available => helper.Available;

    // What the helper wrote, and null or what stopped it.
    public async Task<(IReadOnlyList<string> Lines, string? Problem)> RunAsync(HelperRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> lines = [];
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(s_timeout);

        try
        {
            await foreach (HelperMessage message in helper.RunAsync(request, limit.Token).ConfigureAwait(false))
            {
                if (message.Line is not null)
                {
                    lines.Add(message.Line);
                }

                if (message.ExitCode is { } exitCode)
                {
                    return (lines, exitCode == 0 ? null : message.Problem ?? $"The helper ended with exit code {exitCode}.");
                }
            }

            return (lines, "The DDT Helper service closed the connection before it was done.");
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (lines, "The DDT Helper service does not answer.");
        }
    }

    // The scopes of this computer's DHCP server, or why the helper could not list them.
    public async Task<(IReadOnlyList<DhcpScope> Scopes, string? Problem)> ScopesAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<string> lines, string? problem) = await RunAsync(new HelperRequest { Kind = HelperRequest.DhcpScopes }, cancellationToken).ConfigureAwait(false);

        if (problem is not null)
        {
            return ([], problem);
        }

        try
        {
            // The script writes one line of JSON
            return (JsonSerializer.Deserialize(lines.LastOrDefault(line => line.StartsWith('[')) ?? "[]", DdtJsonContext.Default.IReadOnlyListDhcpScope) ?? [], null);
        }
        catch (JsonException)
        {
            return ([], "The DDT Helper service answered with something that is no list of scopes.");
        }
    }
}
