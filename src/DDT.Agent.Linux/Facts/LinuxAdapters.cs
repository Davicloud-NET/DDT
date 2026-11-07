// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// The network adapters Linux shows under /sys/class/net. A machine can start from the network with the firmware's own
// driver and have none here, and a USB adapter or a dock may appear after the agent started.
public sealed class LinuxAdapters(string root)
{
    public const string NoneMessage =
        "Linux shows no network adapter on this machine, so the agent cannot reach the server. The firmware started the machine " +
        "from the network with a driver of its own, but the Linux boot environment has no driver or no firmware for this network " +
        "card. Connect a USB network adapter or a dock with one: the agent goes on as soon as an adapter appears.";

    // ARPHRD_ETHER of linux/if_arp.h, which leaves out the loopback and tunnels.
    private const string Ethernet = "1";

    private const string NoAddress = "00:00:00:00:00:00";

    private static readonly TimeSpan s_lookAgainAfter = TimeSpan.FromSeconds(2);

    // The names of the adapters that have an address of their own, in the kernel's order of names.
    public IReadOnlyList<string> Names()
    {
        try
        {
            return
            [
                .. Directory.EnumerateDirectories(Path.Combine(root, "sys", "class", "net"))
                    .Where(adapter => SysfsText.Read(Path.Combine(adapter, "type")) == Ethernet
                        && SysfsText.Read(Path.Combine(adapter, "address")) is { } address && address != NoAddress)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Says once what is wrong and what to do, then looks again until an adapter is there. False once stopped.
    public async Task<bool> WaitAsync(AgentLog log, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (Names().Count > 0)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        log.Warning(NoneMessage);

        while (Names().Count == 0)
        {
            try
            {
                await Task.Delay(s_lookAgainAfter, timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        log.Information($"Linux shows a network adapter now, {Names()[0]}. The agent goes on.");

        return true;
    }
}
