// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDT.Pxe;

// Answers a boot file request for TFTP and the HTTP boot port alike: the Linux boot environment's files and the two
// files generated for it by their exact names, and everything else from the boot directory. A name a client sends
// reaches the file system only through a BootFileResolver, or as a name the environment's description lists.
public sealed class BootFiles
{
    public const string LinuxFolder = "linux";
    public const string Shim = "shimx64.efi";
    public const string Grub = "grubx64.efi";
    public const string SiteArchiveName = "site.cpio";
    public const string GrubConfigName = "grub/grub.cfg";

    // What the log adds to the name, so a start that loads slower says why.
    public const string TftpAloneNote = " for TFTP alone, since an HTTP read of this machine did not end well";
    public const string ConnectionLeftNote = " for TFTP alone, since this server's system still holds a connection of the machine's start before";

    private const int MaxRequestLength = 512;

    private const string LinuxBootFile = LinuxFolder + "/" + Shim;

    private readonly ILinuxBootSite? _linux;
    private readonly int _httpBootPort;
    private readonly BootConnections _connections;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private int _failing;

    public BootFiles(
        BootFileResolver boot,
        ILinuxBootSite? linux,
        int httpBootPort,
        BootConnections connections,
        TimeProvider timeProvider,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(boot);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Boot = boot;
        _linux = linux;
        _httpBootPort = httpBootPort;
        _connections = connections;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    // The boot directory, where a Windows PE build lies.
    public BootFileResolver Boot { get; }

    // The Linux boot environment as the server has it ready, or null while none can be started.
    public LinuxBootBundle? Linux => FromLinux(bundle => bundle);

    // Whether the Linux boot environment can be started now.
    public bool LinuxAvailable => Linux is not null;

    public bool TryFind(string requested, BootRequest request, [NotNullWhen(true)] out BootContent? content)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? name = Normalized(requested);

        if (name is not null && (content = FromLinux(bundle => OfLinux(name, bundle, request))) is not null)
        {
            return true;
        }

        if (Boot.TryResolve(requested, out FileInfo? file))
        {
            content = new FileBootContent(file);

            return true;
        }

        // A DHCP server that names the boot file only in option 67 makes shim ask for GRUB at the TFTP root.
        content = name == Grub ? FromLinux(bundle => FromBundle(Grub, bundle)) : null;

        return content is not null;
    }

    // A TFTP read of shim or of grub.cfg begins a machine's start, so what its start before left open is reset.
    // Returns how many connections that were.
    public int Starting(string requested, IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return Normalized(requested) is LinuxBootFile or GrubConfigName ? _connections.NetbootStarts(client) : 0;
    }

    // Forward slashes and no leading one, as the resolver reads a request. Null for what no file can be named by.
    public static string? Normalized(string? requested) =>
        string.IsNullOrEmpty(requested) || requested.Length > MaxRequestLength ? null : requested.Replace('\\', '/').TrimStart('/');

    // Whether the name is one of the Linux boot environment's: what GRUB loads, and nothing Windows PE asks for.
    public static bool IsLinuxName(string? requested) =>
        Normalized(requested) is { } name && (name == GrubConfigName || name.StartsWith(LinuxFolder + "/", StringComparison.Ordinal));

    // Asks the environment the server has ready. Whatever fails behind it, a machine must still get the answer it
    // would get without the environment, so a failure counts as none. The log says so once until it works again.
    private T? FromLinux<T>(Func<LinuxBootBundle, T?> ask)
    {
        try
        {
            T? answer = _linux?.Current is { } bundle ? ask(bundle) : default;

            if (Interlocked.Exchange(ref _failing, 0) == 1)
            {
                PxeLog.LinuxBootAnswersAgain(_logger);
            }

            return answer;
        }
        catch (Exception exception)
        {
            // A boundary: every PXE request and every boot file read passes here, and silence starts no machine.
            if (Interlocked.Exchange(ref _failing, 1) == 0)
            {
                PxeLog.LinuxBootFailed(_logger, exception);
            }

            return default;
        }
    }

    private BootContent? OfLinux(string name, LinuxBootBundle bundle, BootRequest request)
    {
        if (name == GrubConfigName)
        {
            return request.Server.AddressFamily == AddressFamily.InterNetwork ? GrubConfigFor(bundle, request) : null;
        }

        if (!name.StartsWith(LinuxFolder + "/", StringComparison.Ordinal))
        {
            return null;
        }

        string file = name[(LinuxFolder.Length + 1)..];

        if (file == SiteArchiveName)
        {
            return new MemoryBootContent(name, SiteArchive.Write(bundle, _timeProvider.GetUtcNow()));
        }

        return FromBundle(file, bundle);
    }

    // GRUB hangs in an HTTP read that meets a connection its start before left on the server. TFTP has none.
    private MemoryBootContent GrubConfigFor(LinuxBootBundle bundle, BootRequest request)
    {
        HttpBootRisk risk = _connections.HttpRisk(request.Client, _httpBootPort);
        string text = GrubConfig.Write(new GrubConfigValues(request.Server, risk == HttpBootRisk.None ? _httpBootPort : null, _timeProvider.GetUtcNow())
        {
            Keymap = bundle.Keymap,
            HostName = bundle.ServerUrl.Host,
            KernelArguments = bundle.KernelArguments,
        });
        string note = risk switch
        {
            HttpBootRisk.ReadEndedBadly => TftpAloneNote,
            HttpBootRisk.ConnectionLeft => ConnectionLeftNote,
            _ => string.Empty,
        };

        return new MemoryBootContent(GrubConfigName + note, Encoding.ASCII.GetBytes(text));
    }

    // Only what the environment's description lists, by its exact name and from its folder itself, so nothing else in
    // the folder is ever served and no marker in it leads elsewhere. The file is looked at when it is opened.
    private static FileBootContent? FromBundle(string file, LinuxBootBundle bundle) =>
        bundle.Files.TryGetValue(file, out BootFileStamp stamp)
            ? new FileBootContent(new FileInfo(Path.Combine(bundle.Folder, file))) { Checked = stamp, Changed = () => bundle.Changed?.Invoke(file) }
            : null;
}
