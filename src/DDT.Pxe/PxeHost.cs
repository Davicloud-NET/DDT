// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Messages;
using DDT.Protocols.Pxe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace DDT.Pxe;

// One hosted service owns all three listeners, because AddHostedService dedupes on the implementation type and would
// drop the second ProxyDHCP listener. Each new settings version rebuilds them. A setup that doesn't bind rolls back to
// the previous one and is reported, unless PxeDesiredSetup.StopHostOnFailure asks to stop the host at startup.
public sealed class PxeHost : IHostedService, IDisposable
{
    private readonly PxeHostSource _source;
    private readonly PxeListenerBinding _binding;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Func<Task>> _stops = [];
    private readonly CancellationTokenSource _stopping = new();
    private PxeSetup? _applied;
    private long _version = -1;
    private IDisposable? _subscription;

    public PxeHost(PxeHostSource source, TimeProvider timeProvider, ILoggerFactory loggerFactory, PxeListenerBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _source = source;
        _binding = binding ?? PxeListenerBinding.Standard;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PxeHost>();
    }

    // The setup whose listeners are running. The HTTP boot gate reads it on every request. Null while nothing is
    // served.
    public PxeSetup? Applied => Volatile.Read(ref _applied);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        PxeDesiredSetup desired = _source.Desired();
        PxeApplyResult result = await ApplyAsync(desired, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded && desired.StopHostOnFailure && desired.Options is not null)
        {
            throw new InvalidOperationException(result.Message);
        }

        _subscription = ChangeToken.OnChange(_source.Changed, () => _ = ApplyChangedAsync());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StopListenersAsync().ConfigureAwait(false);
            Volatile.Write(ref _applied, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _gate.Dispose();
        _stopping.Dispose();
    }

    // Interfaces are enumerated again on every apply, so an address that changed since the last one is picked up by
    // applying the same version again.
    public async Task<PxeApplyResult> ApplyAsync(PxeDesiredSetup desired, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(desired);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        PxeApplyResult result;

        try
        {
            if (desired.Options is null)
            {
                await StopListenersAsync().ConfigureAwait(false);
                Volatile.Write(ref _applied, null);
                PxeLog.Stopped(_logger, desired.Refusal?.Text ?? "nothing is configured");
                result = new PxeApplyResult(desired.Version, desired.Refusal is null, desired.Refusal?.Text, null, desired.Refusal);
            }
            else
            {
                result = await ApplyAsync(desired, desired.Options).ConfigureAwait(false);
            }

            Volatile.Write(ref _version, desired.Version);
        }
        finally
        {
            _gate.Release();
        }

        await _source.Applied(result).ConfigureAwait(false);

        return result;
    }

    private async Task<PxeApplyResult> ApplyAsync(PxeDesiredSetup desired, PxeOptions options)
    {
        PxeSetup? previous = Volatile.Read(ref _applied);
        PxeSetup next;

        try
        {
            next = PxeSetup.Create(options, _binding.Interfaces(options.Interfaces));
        }
        catch (InvalidOperationException exception)
        {
            // The source already checked the values, so this comes from something that changed on the host since. The
            // running setup stays.
            PxeLog.ApplyFailed(_logger, exception.Message);

            return new PxeApplyResult(desired.Version, false, exception.Message, previous?.Interfaces);
        }

        await StopListenersAsync().ConfigureAwait(false);
        Volatile.Write(ref _applied, null);

        try
        {
            Start(next);
            Volatile.Write(ref _applied, next);

            return new PxeApplyResult(desired.Version, true, null, next.Interfaces);
        }
        catch (InvalidOperationException failure)
        {
            await StopListenersAsync().ConfigureAwait(false);
            PxeLog.ApplyFailed(_logger, failure.Message);

            if (previous is not null)
            {
                try
                {
                    Start(previous);
                    Volatile.Write(ref _applied, previous);
                    PxeLog.RolledBack(_logger);
                }
                catch (InvalidOperationException again)
                {
                    await StopListenersAsync().ConfigureAwait(false);
                    PxeLog.ApplyFailed(_logger, again.Message);
                }
            }

            return new PxeApplyResult(desired.Version, false, failure.Message, next.Interfaces, (failure as PxeBindException)?.Reason);
        }
    }

    // Only a new version is applied. The source fires when any setting changes.
    private async Task ApplyChangedAsync()
    {
        try
        {
            PxeDesiredSetup desired = _source.Desired();

            if (desired.Version != Volatile.Read(ref _version))
            {
                await ApplyAsync(desired, _stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PxeLog.ApplyCrashed(_logger, exception);
        }
    }

    private void Start(PxeSetup setup)
    {
        if (!LogInterfaces(setup.Interfaces))
        {
            return;
        }

        LogBootTargets(setup);

        if (setup.Options.EnableProxyDhcp)
        {
            StartProxyDhcp(setup);
        }

        if (setup.Options.EnableTftp)
        {
            StartTftp(setup);
        }

        PxeLog.HttpBootListening(_logger, setup.Options.HttpBootPort, setup.Files.Root);
    }

    // False when no interface is served, which leaves nothing to start.
    private bool LogInterfaces(NetworkInterfaceMap interfaces)
    {
        foreach (string name in interfaces.Unmatched)
        {
            PxeLog.InterfaceNotFound(_logger, name);
        }

        if (interfaces.Served.Count == 0)
        {
            PxeLog.NoInterfaces(
                _logger,
                string.Join(", ", interfaces.Candidates.Select(candidate => $"{candidate.Name} ({candidate.Address})")));

            return false;
        }

        foreach (ServedInterface served in interfaces.Served)
        {
            PxeLog.ServingInterface(_logger, served.Name, served.Address, served.Index);
        }

        return true;
    }

    private void StartProxyDhcp(PxeSetup setup)
    {
        ProxyDhcpHandler handler = new(setup.ProxyDhcp, setup.Interfaces);

        ProxyDhcpListener dhcp = new(
            ProxyDhcpListenPort.Dhcp,
            new IPEndPoint(_binding.Address, _binding.DhcpPort),
            handler,
            setup.Interfaces,
            _loggerFactory.CreateLogger<ProxyDhcpListener>());
        Bind(dhcp, "ProxyDHCP", "proxyDhcp", _binding.DhcpPort);

        ProxyDhcpListener bootServer = new(
            ProxyDhcpListenPort.PxeBootServer,
            new IPEndPoint(_binding.Address, _binding.BootServerPort),
            handler,
            setup.Interfaces,
            _loggerFactory.CreateLogger<ProxyDhcpListener>());
        Bind(bootServer, "PXE boot server", "bootServer", _binding.BootServerPort);
    }

    private void StartTftp(PxeSetup setup)
    {
        PxeOptions options = setup.Options;
        TftpListener tftp = new(
            new IPEndPoint(_binding.Address, _binding.TftpPort),
            setup.Interfaces,
            setup.Files,
            new TftpServing(setup.TftpLimits, options.MaxConcurrentTftpTransfers, options.TftpSinglePort),
            _timeProvider,
            _loggerFactory.CreateLogger<TftpListener>());
        Bind(
            tftp,
            options.TftpSinglePort ? "TFTP (single port)" : "TFTP",
            options.TftpSinglePort ? "tftpSinglePort" : "tftp",
            _binding.TftpPort);
    }

    // Ends every TFTP transfer in progress. The machines then start them again.
    private async Task StopListenersAsync()
    {
        foreach (Func<Task> stop in _stops)
        {
            await stop().ConfigureAwait(false);
        }

        _stops.Clear();
    }

    // protocol names the listener in the log, and kind names it in the message. The message also gives advice for the
    // socket error.
    private void Bind(IPxeListener listener, string protocol, string kind, int port)
    {
        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            throw new PxeBindException(
                ServerMessages.SettingsApplyPxeBindFailed.With("port", port, "protocol", kind, "error", exception.SocketErrorCode),
                exception);
        }

        _stops.Add(listener.StopAsync);
        PxeLog.Listening(_logger, protocol, port);
    }

    private void LogBootTargets(PxeSetup setup)
    {
        // A site DHCP server that names DDT in options 66 and 67 needs no boot targets at all.
        if (setup.ProxyDhcp.BootTargets.Count == 0 && setup.Options.EnableProxyDhcp)
        {
            PxeLog.NoBootTargets(_logger);
        }

        foreach (BootTarget target in setup.ProxyDhcp.BootTargets.Values)
        {
            PxeLog.BootTarget(_logger, target.Architecture, target.Method, target.BootFile);

            // A missing boot manager is the commonest cause of "DHCP works, nothing boots".
            if (target.Method == BootMethod.Tftp && target.ServerAddress is null && !setup.Files.TryResolve(target.BootFile, out _))
            {
                PxeLog.BootFileMissing(_logger, target.Architecture, target.BootFile, setup.Files.Root);
            }
        }
    }
}
