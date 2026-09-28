// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Connects a step's shares as \\host\share, as Windows connects shares, not folders; temporary and without a drive letter,
// nothing outlives the step. Windows allows one account per server in a logon session, so another account's connection
// to the server gives way, once, unless the step made it.
public sealed class ShareConnector(INetworkConnections network, AgentLog log) : IShareConnector
{
    // ERROR_SESSION_CREDENTIAL_CONFLICT.
    public const int SessionCredentialConflict = 1219;

    public async Task<IAsyncDisposable> ConnectAsync(
        IReadOnlyList<AgentShareConnection> shares,
        IAccountSession? account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shares);

        Connections connections = new(network, account, log);

        try
        {
            foreach (AgentShareConnection share in shares)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await connections.AddAsync(share).ConfigureAwait(false);
            }

            return connections;
        }
        catch
        {
            await connections.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    // \\host\share of a path such as \\host\share\folder.
    public static string RemoteName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string trimmed = path.Trim();
        string[] parts = trimmed.StartsWith(@"\\", StringComparison.Ordinal) ? trimmed[2..].Split('\\') : [];

        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0 || parts[0] is "?" or ".")
        {
            throw new DeploymentStepException($"{path} is not the path of a share, such as \\\\files.example.com\\drivers.");
        }

        return $@"\\{parts[0]}\{parts[1]}";
    }

    public static string Host(string remoteName)
    {
        ArgumentNullException.ThrowIfNull(remoteName);

        string name = remoteName.TrimStart('\\');
        int end = name.IndexOf('\\', StringComparison.Ordinal);

        return end < 0 ? name : name[..end];
    }

    private static bool OnHost(string remoteName, string host) => string.Equals(Host(remoteName), host, StringComparison.OrdinalIgnoreCase);

    private static string Describe(NetworkError error) =>
        $"(error {error.Code}): {error.Message.Trim()}";

    private sealed record Connection(string RemoteName, string UserName);

    private sealed class Connections(INetworkConnections network, IAccountSession? account, AgentLog log) : IAsyncDisposable
    {
        private readonly List<Connection> _connected = [];
        private bool _disposed;

        private string Where => account is null ? string.Empty : $", for {account.UserName}";

        public async Task AddAsync(AgentShareConnection share)
        {
            string remote = RemoteName(share.Path);

            if (_connected.Find(connection => string.Equals(connection.RemoteName, remote, StringComparison.OrdinalIgnoreCase)) is { } same)
            {
                if (string.Equals(same.UserName, share.UserName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                throw Conflict(remote, share.UserName, same);
            }

            NetworkError? error = await RunAsync(() => network.Add(remote, share.UserName, share.Password)).ConfigureAwait(false);

            if (error?.Code == SessionCredentialConflict)
            {
                string host = Host(remote);

                if (_connected.Find(connection => OnHost(connection.RemoteName, host)) is { } ours)
                {
                    throw Conflict(remote, share.UserName, ours);
                }

                await CancelOthersAsync(host, share.UserName).ConfigureAwait(false);
                error = await RunAsync(() => network.Add(remote, share.UserName, share.Password)).ConfigureAwait(false);
            }

            if (error is not null)
            {
                throw new DeploymentStepException($"{remote} could not be connected as {share.UserName} {Describe(error)}");
            }

            _connected.Add(new Connection(remote, share.UserName));
            log.Information($"Connected {remote} as {share.UserName}{Where}.");
        }

        // In the reverse order, and a share that stays connected only warns: the step's result is already decided.
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            for (int index = _connected.Count - 1; index >= 0; index--)
            {
                string remote = _connected[index].RemoteName;

                try
                {
                    NetworkError? error = await RunAsync(() => network.Cancel(remote)).ConfigureAwait(false);

                    if (error is null)
                    {
                        log.Information($"Disconnected {remote}.");
                    }
                    else
                    {
                        log.Warning($"{remote} could not be disconnected {Describe(error)}");
                    }
                }
                catch (Exception exception)
                {
                    log.Warning($"{remote} could not be disconnected: {exception.Message}");
                }
            }

            _connected.Clear();
        }

        private async Task CancelOthersAsync(string host, string userName)
        {
            IReadOnlyList<string> connected = await RunAsync(network.Connected).ConfigureAwait(false);

            foreach (string other in connected.Where(name => OnHost(name, host)))
            {
                log.Warning($"{other} is connected with another account, which Windows does not allow beside {userName} on {host}, so it is disconnected.");
                NetworkError? error = await RunAsync(() => network.Cancel(other)).ConfigureAwait(false);

                if (error is not null)
                {
                    log.Warning($"{other} could not be disconnected {Describe(error)}");
                }
            }
        }

        private DeploymentStepException Conflict(string remote, string userName, Connection ours) =>
            new($"{remote} cannot be connected as {userName} while this step has {ours.RemoteName} connected as {ours.UserName}: " +
                $"Windows connects to a server with one account at a time. Use one account for every share on {Host(remote)}.");

        private Task<T> RunAsync<T>(Func<T> call) =>
            account is null ? DedicatedThread.RunAsync(call, "DDT share connection") : account.ImpersonateAsync(call);
    }
}
