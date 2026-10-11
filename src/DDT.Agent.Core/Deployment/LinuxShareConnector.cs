// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Connects a step's shares in the installed Linux with the kernel's own mount call, under directory, and tells the
// step where. A helper program is not on every image and loses a password at its first comma. The mounts are the
// step's alone and go when it ends, with files still open.
public sealed class LinuxShareConnector(IMounts mounts, string directory, AgentLog log) : IShareConnector
{
    public const string FileSystem = "cifs";

    // Looks a host's name up. The kernel resolves none without a helper, so it gets the address.
    public Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; init; } = Dns.GetHostAddressesAsync;

    // The running kernel's release, for the message of one without CIFS.
    public Func<string> Kernel { get; init; } = () => Environment.OSVersion.Version.ToString();

    public async Task<IConnectedShares> ConnectAsync(
        IReadOnlyList<AgentShareConnection> shares,
        IAccountSession? account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shares);

        LinuxConnectedShares connected = new(mounts, log);
        string step = Path.Combine(directory, log.StepId?.ToString("D") ?? "step");

        try
        {
            for (int index = 0; index < shares.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(step, (index + 1).ToString(CultureInfo.InvariantCulture));
                await ConnectAsync(shares[index], target, connected, cancellationToken).ConfigureAwait(false);
            }

            return connected;
        }
        catch
        {
            await connected.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    // What an agent that was killed left mounted, since its steps can no longer disconnect it.
    public void DisconnectLeftovers()
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string target in Directory.GetDirectories(directory).SelectMany(Directory.GetDirectories))
        {
            if (mounts.Unmount(target, detach: true) == 0)
            {
                log.Warning($"Disconnected a share an earlier start of the agent left at {target}.");
            }

            LinuxConnectedShares.RemoveEmpty(target);
        }
    }

    private async Task ConnectAsync(AgentShareConnection share, string target, LinuxConnectedShares connected, CancellationToken cancellationToken)
    {
        SharePath path = SharePath.Parse(share.Path);
        IPAddress address = await AddressAsync(path, cancellationToken).ConfigureAwait(false);
        string options = CifsOptions.For(address, share.UserName, share.Password)
            ?? throw new DeploymentStepException($"The account for {path.Unc} is too long for the kernel's mount call, or holds a character it cannot carry.");

        LinuxConnectedShares.Create(target);

        // A mount an agent that was killed left here would lie under the new one.
        mounts.Unmount(target, detach: true);

        // The call waits for the server, for seconds when it does not answer.
        int error = await Task.Run(() => mounts.Mount(path.Source, target, FileSystem, options), CancellationToken.None).ConfigureAwait(false);

        if (error != 0)
        {
            LinuxConnectedShares.RemoveEmpty(target);

            throw new DeploymentStepException(Failure(error, path, share.UserName, address));
        }

        connected.Add(path.Unc, target);
        log.Information($"Connected {path.Unc} as {share.UserName} at {target}.");
    }

    // An address the kernel can reach by IPv4 comes first.
    private async Task<IPAddress> AddressAsync(SharePath path, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(path.Host, out IPAddress? literal))
        {
            return literal;
        }

        try
        {
            IPAddress[] found = await Resolve(path.Host, cancellationToken).ConfigureAwait(false);

            return found.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                ?? found.FirstOrDefault()
                ?? throw new DeploymentStepException($"The name {path.Host} of {path.Unc} has no address, so the share cannot be connected.");
        }
        catch (SocketException exception)
        {
            throw new DeploymentStepException($"The name {path.Host} of {path.Unc} could not be looked up ({exception.Message}), so the share cannot be connected.", exception);
        }
    }

    // By the kernel's error number, as two cloud images answered a Samba server.
    private string Failure(int error, SharePath path, string userName, IPAddress address) => error switch
    {
        19 => $"This Linux's kernel {Kernel()} has no CIFS file system, so {path.Unc} cannot be connected for this step. The kernel " +
            "modules package of the image lacks it. Add it to the image or install it in an earlier step, or leave the share out.",
        13 => $"{path.Unc} could not be connected as {userName}: the server did not take the account's name or password (error 13).",
        2 => $"{path.Unc} could not be connected: {path.Host} has no such share, or the share no such folder (error 2).",
        113 => $"{path.Unc} could not be connected: {path.Host} ({address}) cannot be reached from this machine (error 113).",
        111 => $"{path.Unc} could not be connected: {path.Host} ({address}) takes no SMB connection on port 445 (error 111).",
        _ => $"{path.Unc} could not be connected as {userName} (error {error}). The kernel's log says why: dmesg shows it on the machine.",
    };
}
