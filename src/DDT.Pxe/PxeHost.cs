// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Pxe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace DDT.Pxe;

// One hosted service owns all three listeners. Registering them separately with AddHostedService
// would silently drop the second ProxyDHCP listener, because it dedupes on implementation type.
//
// The settings can change while the server runs, so the listeners are rebuilt whenever the source reports a new version:
// stopped, and started again with the new setup. A setup whose listeners do not bind is not kept: the previous one is
// started again, and the failure is reported to the source instead of stopping the host. Only at startup, and only when
// the source says configuration decided what is served, a bind failure still stops the host before it reports itself
// started, as it always did.
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

    // The setup whose listeners run, which the HTTP boot gate reads on every request. Null while nothing is served.
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
                PxeLog.Stopped(_logger, desired.Refusal ?? "nothing is configured");
                result = new PxeApplyResult(desired.Version, desired.Refusal is null, desired.Refusal, null);
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
            // The source checked the values; this is what changed on the host since, and the running setup stays.
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

            return new PxeApplyResult(desired.Version, false, failure.Message, next.Interfaces);
        }
    }

    // Only a new version is applied; the source fires for a change of any setting.
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
        NetworkInterfaceMap interfaces = setup.Interfaces;

        foreach (string name in interfaces.Unmatched)
        {
            PxeLog.InterfaceNotFound(_logger, name);
        }

        if (interfaces.Served.Count == 0)
        {
            PxeLog.NoInterfaces(
                _logger,
                string.Join(", ", interfaces.Candidates.Select(candidate => $"{candidate.Name} ({candidate.Address})")));

            return;
        }

        foreach (ServedInterface served in interfaces.Served)
        {
            PxeLog.ServingInterface(_logger, served.Name, served.Address, served.Index);
        }

        LogBootTargets(setup);

        PxeOptions options = setup.Options;

        if (options.EnableProxyDhcp)
        {
            ProxyDhcpHandler handler = new(setup.ProxyDhcp, interfaces);

            ProxyDhcpListener dhcp = new(
                ProxyDhcpListenPort.Dhcp,
                new IPEndPoint(_binding.Address, _binding.DhcpPort),
                handler,
                interfaces,
                _loggerFactory.CreateLogger<ProxyDhcpListener>());
            Bind(dhcp.Start, dhcp.StopAsync, "ProxyDHCP", _binding.DhcpPort);

            ProxyDhcpListener bootServer = new(
                ProxyDhcpListenPort.PxeBootServer,
                new IPEndPoint(_binding.Address, _binding.BootServerPort),
                handler,
                interfaces,
                _loggerFactory.CreateLogger<ProxyDhcpListener>());
            Bind(bootServer.Start, bootServer.StopAsync, "PXE boot server", _binding.BootServerPort);
        }

        if (options.EnableTftp)
        {
            TftpListener tftp = new(
                new IPEndPoint(_binding.Address, _binding.TftpPort),
                interfaces,
                setup.Files,
                setup.TftpLimits,
                options.MaxConcurrentTftpTransfers,
                options.TftpSinglePort,
                _timeProvider,
                _loggerFactory.CreateLogger<TftpListener>());
            Bind(tftp.Start, tftp.StopAsync, options.TftpSinglePort ? "TFTP (single port)" : "TFTP", _binding.TftpPort);
        }

        PxeLog.HttpBootListening(_logger, options.HttpBootPort, setup.Files.Root);
    }

    // Ends every TFTP transfer in progress, which a machine then starts again.
    private async Task StopListenersAsync()
    {
        foreach (Func<Task> stop in _stops)
        {
            await stop().ConfigureAwait(false);
        }

        _stops.Clear();
    }

    private void Bind(Action start, Func<Task> stop, string protocol, int port)
    {
        try
        {
            start();
        }
        catch (SocketException exception)
        {
            string hint = exception.SocketErrorCode switch
            {
                SocketError.AccessDenied =>
                    "The process may not bind a privileged port. Grant NET_BIND_SERVICE, as build/compose.yaml does.",
                SocketError.AddressAlreadyInUse =>
                    "Another DHCP, PXE or TFTP service already holds this port on this host. Stop it, or run DDT without the pxe role here.",
                _ => "Check that no other service holds the port.",
            };

            throw new InvalidOperationException(
                $"DDT could not bind UDP {port} for {protocol} ({exception.SocketErrorCode}). {hint}",
                exception);
        }

        _stops.Add(stop);
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

// What the host should serve: the options, or null to serve nothing with Refusal saying why, and the version of the
// settings they came from. StopHostOnFailure: configuration decided what is served, so a bind failure at startup stops
// the host rather than being reported.
public sealed record PxeDesiredSetup(long Version, PxeOptions? Options, string? Refusal, bool StopHostOnFailure);

// Interfaces is what the host found when it applied, null when it built no setup.
public sealed record PxeApplyResult(long Version, bool Succeeded, string? Message, NetworkInterfaceMap? Interfaces);

// How the host learns what to serve, when that changes, and where each result goes. DDT.Pxe knows nothing of the
// settings store; the host wires these to it.
public sealed record PxeHostSource(Func<PxeDesiredSetup> Desired, Func<IChangeToken> Changed, Func<PxeApplyResult, Task> Applied);

// Where the listeners bind and how the host finds its interfaces. Standard is the real thing: the wildcard address, which
// leaves Kestrel alone, the PXE ports, and the host's interfaces as they are at each apply.
public sealed record PxeListenerBinding(IPAddress Address, int DhcpPort, int BootServerPort, int TftpPort, Func<string, NetworkInterfaceMap> Interfaces)
{
    public static PxeListenerBinding Standard { get; } = new(IPAddress.Any, 67, 4011, 69, NetworkInterfaceMap.FromHost);
}
