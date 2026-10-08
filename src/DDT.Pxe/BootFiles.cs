// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text;

namespace DDT.Pxe;

// Answers a boot file request for TFTP and the HTTP boot port alike: the Linux boot environment's files and the two
// files generated for it by their exact names, and everything else from the boot directory. A name a client sends
// reaches the file system only through a BootFileResolver.
public sealed class BootFiles
{
    public const string LinuxFolder = "linux";
    public const string Shim = "shimx64.efi";
    public const string Grub = "grubx64.efi";
    public const string SiteArchiveName = "site.cpio";
    public const string GrubConfigName = "grub/grub.cfg";

    private const int MaxRequestLength = 512;

    private readonly ILinuxBootSite? _linux;
    private readonly int _httpBootPort;
    private readonly TimeProvider _timeProvider;
    private BootFileResolver? _bundle;

    public BootFiles(BootFileResolver boot, ILinuxBootSite? linux, int httpBootPort, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(boot);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Boot = boot;
        _linux = linux;
        _httpBootPort = httpBootPort;
        _timeProvider = timeProvider;
    }

    // The boot directory, where a Windows PE build lies.
    public BootFileResolver Boot { get; }

    // Whether the Linux boot environment can be started now.
    public bool LinuxAvailable => _linux?.Current is not null;

    public bool TryFind(string requested, BootRequest request, [NotNullWhen(true)] out BootContent? content)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? name = Normalized(requested);
        LinuxBootBundle? bundle = name is null ? null : _linux?.Current;

        if (name is not null && bundle is not null && (content = OfLinux(name, bundle, request)) is not null)
        {
            return true;
        }

        if (Boot.TryResolve(requested, out FileInfo? file))
        {
            content = new FileBootContent(file);

            return true;
        }

        // A DHCP server that names the boot file only in option 67 makes shim ask for GRUB at the TFTP root.
        content = name == Grub && bundle is not null ? FromBundle(Grub, bundle) : null;

        return content is not null;
    }

    // Forward slashes and no leading one, as the resolver reads a request. Null for what no file can be named by.
    public static string? Normalized(string? requested) =>
        string.IsNullOrEmpty(requested) || requested.Length > MaxRequestLength ? null : requested.Replace('\\', '/').TrimStart('/');

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

        // Only what the environment's description lists, so nothing else in its folder is ever served.
        return bundle.Files.Contains(file) ? FromBundle(file, bundle) : null;
    }

    private MemoryBootContent GrubConfigFor(LinuxBootBundle bundle, BootRequest request)
    {
        string text = GrubConfig.Write(new GrubConfigValues(request.Server, _httpBootPort, _timeProvider.GetUtcNow())
        {
            Keymap = bundle.Keymap,
            HostName = bundle.ServerUrl.Host,
            KernelArguments = bundle.KernelArguments,
        });

        return new MemoryBootContent(GrubConfigName, Encoding.ASCII.GetBytes(text));
    }

    // The same rules as for the boot directory: a real entry of the folder, and no link.
    private FileBootContent? FromBundle(string file, LinuxBootBundle bundle)
    {
        BootFileResolver? resolver = Volatile.Read(ref _bundle);

        if (resolver is null || !string.Equals(resolver.Root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundle.Folder)), StringComparison.Ordinal))
        {
            resolver = new BootFileResolver(bundle.Folder);
            Volatile.Write(ref _bundle, resolver);
        }

        return resolver.TryResolve(file, out FileInfo? found) ? new FileBootContent(found) : null;
    }
}
