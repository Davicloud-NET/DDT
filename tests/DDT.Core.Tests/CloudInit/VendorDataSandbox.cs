// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Core.Tests.CloudInit;

// A folder that stands in for the installed Linux, where sh runs the vendor-data script as it is but for its paths,
// which lead into the folder. systemctl, blkid, mount, umount and sync are stand-ins: a seed is a folder, mounting it
// puts a link to it in the mount point's place, and the test computer's own disks are not written out.
[SupportedOSPlatform("linux")]
internal sealed partial class VendorDataSandbox : IDisposable
{
    // The places the script names outside its unit's text: systemd's, the mount point, the link, and where it installs.
    private const int PathsInTheScript = 11;

    private const string Mount =
        """
        #!/bin/sh
        echo "mount $*" >> "$DDT_SANDBOX/calls"
        device=$5
        case "$device" in */by-label/*) device=$(readlink "$device") ;; esac
        volume="$DDT_SANDBOX/volumes/$(basename "$device")"
        [ -d "$volume" ] || exit 32
        rmdir "$6" && ln -s "$volume" "$6"
        """;

    // As the real one, it fails where nothing is mounted.
    private const string Unmount =
        """
        #!/bin/sh
        echo "umount $*" >> "$DDT_SANDBOX/calls"
        [ -L "$1" ] || exit 32
        rm "$1" && mkdir "$1"
        """;

    private const string Blkid =
        """
        #!/bin/sh
        [ -s "$DDT_SANDBOX/blkid" ] || exit 2
        cat "$DDT_SANDBOX/blkid"
        """;

    private const string Systemctl =
        """
        #!/bin/sh
        echo "systemctl $*" >> "$DDT_SANDBOX/calls"
        """;

    private const string Sync =
        """
        #!/bin/sh
        echo "sync" >> "$DDT_SANDBOX/calls"
        """;

    private readonly string _script;

    public VendorDataSandbox()
    {
        Root = Directory.CreateTempSubdirectory("ddt-vendor-").FullName;
        _script = Path.Combine(Root, "part-001");

        foreach (string folder in (string[])["bin", "volumes", "dev/disk/by-label", "run/systemd/system", "etc/systemd/system", "usr/local/lib", "var/lib"])
        {
            Directory.CreateDirectory(In(folder));
        }

        File.WriteAllText(_script, Sandboxed(VendorDataScript.Text));
        Tool("mount", Mount);
        Tool("umount", Unmount);
        Tool("blkid", Blkid);
        Tool("systemctl", Systemctl);
        Tool("sync", Sync);
    }

    public string Root { get; }

    public string SeedMount => In("run/ddt-seed");

    // What the stand-ins were called with, in order.
    public string[] Calls => File.Exists(In("calls")) ? File.ReadAllLines(In("calls")) : [];

    public string In(string path) => Path.Combine(Root, path);

    // A CIDATA volume the kernel knows, with files beside cloud-init's own. byLabel is the one udev's link names.
    // Returns its device, as blkid prints it.
    public string AddSeed(string name, bool byLabel, params (string Path, string Content)[] files)
    {
        string device = In($"dev/{name}");
        string volume = In($"volumes/{name}");
        MakeDevice(device);
        Directory.CreateDirectory(volume);
        File.WriteAllText(Path.Combine(volume, CloudInitSeed.UserData), "#cloud-config\n");

        foreach ((string path, string content) in files)
        {
            string file = Path.Combine(volume, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? volume);
            File.WriteAllText(file, content);
        }

        File.AppendAllText(In("blkid"), device + "\n");

        if (byLabel)
        {
            File.CreateSymbolicLink(In($"dev/disk/by-label/{CloudInitSeed.Label}"), device);
        }

        return device;
    }

    public string Volume(string name) => In($"volumes/{name}");

    // A stand-in of the test's own, such as a dd that fails.
    public void Tool(string name, string script)
    {
        string path = In($"bin/{name}");
        File.WriteAllText(path, script.ReplaceLineEndings("\n") + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // Runs the script as cloud-init does, as a program of its own, with the stand-ins before the system's tools.
    public (int ExitCode, string[] Output, string[] Errors) Run()
    {
        ProcessStartInfo start = new("sh", [_script]) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PATH"] = $"{In("bin")}:{Environment.GetEnvironmentVariable("PATH")}";
        start.Environment["DDT_SANDBOX"] = Root;

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("sh did not start.");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, Lines(output), Lines(errors.GetAwaiter().GetResult()));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // Every path of the script leads into the folder. The unit's text stays, since it is only written to a file.
    private string Sandboxed(string script)
    {
        int unit = script.IndexOf("<<'UNIT'\n", StringComparison.Ordinal);
        int after = script.IndexOf("\nUNIT\n", StringComparison.Ordinal);
        int paths = 0;

        string Move(string part) => Places().Replace(part, match =>
        {
            paths++;

            return Root + match.Value;
        });

        string moved = Move(script[..unit]) + script[unit..after] + Move(script[after..]);
        Assert.Equal(PathsInTheScript, paths);

        return moved;
    }

    // The script asks whether the device is a block device. Root makes one that nothing ever opens. Anyone else gets
    // a link to a disk of this computer, which the stand-ins never open either.
    private static void MakeDevice(string path)
    {
        using Process? made = Process.Start(new ProcessStartInfo("mknod", [path, "b", "7", "250"]) { RedirectStandardError = true });
        made?.WaitForExit();

        if (made is { ExitCode: 0 })
        {
            return;
        }

        string? disk = Directory.Exists("/sys/block")
            ? Directory.EnumerateDirectories("/sys/block").Select(block => $"/dev/{Path.GetFileName(block)}").FirstOrDefault(File.Exists)
            : null;
        Assert.SkipWhen(disk is null, "This system has no block device, and the test may not make one.");
        File.CreateSymbolicLink(path, disk ?? "");
    }

    [GeneratedRegex(@"(?<=[ =])/(?=run/|usr/local/lib/ddt|var/lib/ddt|etc/systemd/system/|dev/disk/)")]
    private static partial Regex Places();
}
