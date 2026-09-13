using System.Net;
using System.Net.Sockets;
using DDT.Protocols.Pxe;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Pxe;

// One hosted service owns all three listeners. Registering them separately with AddHostedService
// would silently drop the second ProxyDHCP listener, because it dedupes on implementation type.
public sealed class PxeHost : IHostedService
{
    private const int DhcpPort = 67;
    private const int PxeBootServerPort = 4011;
    private const int TftpPort = 69;

    private readonly PxeSetup _setup;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly List<Func<Task>> _stops = [];

    public PxeHost(PxeSetup setup, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _setup = setup;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PxeHost>();
    }

    // Binding here rather than in a background loop is what makes a failed bind stop the host before
    // it reports itself started.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        NetworkInterfaceMap interfaces = _setup.Interfaces;

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

        LogBootTargets();

        PxeOptions options = _setup.Options;

        try
        {
            if (options.EnableProxyDhcp)
            {
                ProxyDhcpHandler handler = new(_setup.ProxyDhcp, interfaces);

                ProxyDhcpListener dhcp = new(
                    ProxyDhcpListenPort.Dhcp,
                    new IPEndPoint(IPAddress.Any, DhcpPort),
                    handler,
                    interfaces,
                    _loggerFactory.CreateLogger<ProxyDhcpListener>());
                Bind(dhcp.Start, dhcp.StopAsync, "ProxyDHCP", DhcpPort);

                ProxyDhcpListener bootServer = new(
                    ProxyDhcpListenPort.PxeBootServer,
                    new IPEndPoint(IPAddress.Any, PxeBootServerPort),
                    handler,
                    interfaces,
                    _loggerFactory.CreateLogger<ProxyDhcpListener>());
                Bind(bootServer.Start, bootServer.StopAsync, "PXE boot server", PxeBootServerPort);
            }

            if (options.EnableTftp)
            {
                TftpListener tftp = new(
                    new IPEndPoint(IPAddress.Any, TftpPort),
                    interfaces,
                    _setup.Files,
                    _setup.TftpLimits,
                    options.MaxConcurrentTftpTransfers,
                    options.TftpSinglePort,
                    _timeProvider,
                    _loggerFactory.CreateLogger<TftpListener>());
                Bind(tftp.Start, tftp.StopAsync, options.TftpSinglePort ? "TFTP (single port)" : "TFTP", TftpPort);
            }
        }
        catch
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        PxeLog.HttpBootListening(_logger, _setup.Options.HttpBootPort, _setup.Files.Root);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
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

    private void LogBootTargets()
    {
        // A site DHCP server that names DDT in options 66 and 67 needs no boot targets at all.
        if (_setup.ProxyDhcp.BootTargets.Count == 0 && _setup.Options.EnableProxyDhcp)
        {
            PxeLog.NoBootTargets(_logger);
        }

        foreach (BootTarget target in _setup.ProxyDhcp.BootTargets.Values)
        {
            PxeLog.BootTarget(_logger, target.Architecture, target.Method, target.BootFile);

            // A missing boot manager is the commonest cause of "DHCP works, nothing boots".
            if (target.Method == BootMethod.Tftp && target.ServerAddress is null && !_setup.Files.TryResolve(target.BootFile, out _))
            {
                PxeLog.BootFileMissing(_logger, target.Architecture, target.BootFile, _setup.Files.Root);
            }
        }
    }
}
