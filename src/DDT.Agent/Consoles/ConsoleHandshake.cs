// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The hello both sides say first, which only a console of this agent's version of the protocol gets back.
internal static class ConsoleHandshake
{
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    // The console's program once both sides said hello; otherwise why it is refused, and whether it was told so.
    public static async Task<(string? Program, string? Refusal, bool Told)> GreetAsync(
        ConsoleChannel channel,
        string agentVersion,
        CancellationToken stop)
    {
        HelloMessage hello;

        using (CancellationTokenSource greeting = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            greeting.CancelAfter(HelloTimeout);

            try
            {
                if (await channel.ReceiveAsync(greeting.Token).ConfigureAwait(false) is not HelloMessage received)
                {
                    return (null, "did not begin with a hello", false);
                }

                hello = received;
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                return (null, $"did not say hello within {Durations.Describe(HelloTimeout)}", false);
            }
        }

        if (hello.Version != HelloMessage.CurrentVersion)
        {
            RefusedMessage refusal = new(
                HelloMessage.CurrentVersion,
                $"This agent speaks version {HelloMessage.CurrentVersion} of the console protocol, not {hello.Version}.");
            bool told = await ConsoleSender.SendAsync(channel, refusal, stop).ConfigureAwait(false) is null;

            return (null, $"speaks version {hello.Version} of the console protocol, and this agent version {HelloMessage.CurrentVersion}", told);
        }

        HelloMessage accepted = new(HelloMessage.CurrentVersion, $"DDT agent {agentVersion}");

        return await ConsoleSender.SendAsync(channel, accepted, stop).ConfigureAwait(false) is { } unsent
            ? (null, unsent, false)
            : (hello.Program, null, false);
    }
}
