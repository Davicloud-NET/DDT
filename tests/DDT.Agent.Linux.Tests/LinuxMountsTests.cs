// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// What a script left mounted from the disk the run wrote, and its unmounting before the machine restarts.
public sealed class LinuxMountsTests : IDisposable
{
    // The list of a boot environment in which a script mounted the image's root and its EFI system partition.
    private const string Mounts =
        "rootfs / rootfs rw 0 0\n" +
        "proc /proc proc rw,nosuid,nodev,noexec,relatime 0 0\n" +
        "devtmpfs /dev devtmpfs rw,nosuid,relatime,size=1002128k,nr_inodes=250532,mode=755 0 0\n" +
        "tmpfs /run tmpfs rw,nosuid,nodev,relatime,mode=755 0 0\n" +
        "efivarfs /sys/firmware/efi/efivars efivarfs rw,nosuid,nodev,noexec,relatime 0 0\n" +
        "/dev/nvme0n1p1 /mnt ext4 rw,relatime 0 0\n" +
        "/dev/nvme0n1p15 /mnt/boot/efi vfat rw,relatime,fmask=0022 0 0\n" +
        "/dev/sda1 /mnt/usb\\040stick vfat rw,relatime 0 0\n" +
        "/dev/nvme1n1 /srv/whole\\011disk xfs rw 0 0\n";

    private readonly LinuxTree _tree = new();
    private readonly StringWriter _console = new();
    private readonly List<string> _unmounted = [];
    private readonly Dictionary<string, int> _errors = new(StringComparer.Ordinal);

    public LinuxMountsTests()
    {
        _tree.Write(Mounts, "proc", "self", "mounts");

        foreach (string partition in new[] { "nvme0n1p1", "nvme0n1p14", "nvme0n1p15" })
        {
            _tree.Write("1\n", "sys", "block", "nvme0n1", partition, "partition");
        }

        _tree.Write("1\n", "sys", "block", "sda", "sda1", "partition");
    }

    public void Dispose() => _tree.Dispose();

    private LinuxMounts MountsOf() => new(
        _tree.Root,
        new AgentLog(new ImmediateTimeProvider(), _console),
        target =>
        {
            _unmounted.Add(target);

            return _errors.GetValueOrDefault(target);
        });

    // The partition inside another goes first, or the outer one would be in use.
    [Fact]
    public void UnmountsWhatIsMountedFromTheDiskDeepestFirstAndNothingElse()
    {
        MountsOf().UnmountFrom([_tree.PathOf("dev", "nvme0n1")]);

        Assert.Equal(["/mnt/boot/efi", "/mnt"], _unmounted);
        Assert.Contains("Unmounted /mnt/boot/efi, which was still mounted from /dev/nvme0n1p15, before the restart.", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unmounted /mnt, which was still mounted from /dev/nvme0n1p1, before the restart.", _console.ToString(), StringComparison.Ordinal);
    }

    // The look for a handed-over run opens every disk, so every one of them is let go of, also one no run wrote to.
    [Fact]
    public void UnmountsWhatIsMountedFromEveryDiskThatWasOpened()
    {
        MountsOf().UnmountFrom([_tree.PathOf("dev", "nvme0n1"), "/dev/nvme1n1", "/dev/sda"]);

        Assert.Equal(["/srv/whole\tdisk", "/mnt/usb stick", "/mnt/boot/efi", "/mnt"], _unmounted);
    }

    // The hand-over lets go of the disk before it writes the seed. No restart comes then, so none is named.
    [Fact]
    public void SaysWhatTheUnmountingComesBeforeWhenThatIsNotTheRestart()
    {
        _errors["/mnt"] = 16;

        MountsOf().UnmountFrom([_tree.PathOf("dev", "nvme0n1")], LinuxMounts.TheHandOver);

        Assert.Contains(
            "INFO  Unmounted /mnt/boot/efi, which was still mounted from /dev/nvme0n1p15, before the hand-over writes the cloud-init seed.",
            _console.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "WARN  /mnt is still mounted from /dev/nvme0n1p1 and could not be unmounted (error 16), so its file system is not closed before " +
            "the hand-over writes the cloud-init seed.",
            _console.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("restart", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAPlaceWithASpaceOrATabAndADiskMountedWhole()
    {
        MountsOf().UnmountFrom(["/dev/sda", "/dev/nvme1n1"]);

        Assert.Equal(["/srv/whole\tdisk", "/mnt/usb stick"], _unmounted);
    }

    [Fact]
    public void SaysWhatCouldNotBeUnmountedAndGoesOnWithTheRest()
    {
        _errors["/mnt/boot/efi"] = 16;

        MountsOf().UnmountFrom(["/dev/nvme0n1"]);

        Assert.Equal(["/mnt/boot/efi", "/mnt"], _unmounted);
        Assert.Contains(
            "/mnt/boot/efi is still mounted from /dev/nvme0n1p15 and could not be unmounted (error 16), so its file system is not closed before the restart.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnmountsNothingWhereNoDiskWasOpenedOrTheListCannotBeRead()
    {
        MountsOf().UnmountFrom([]);
        new LinuxMounts(_tree.PathOf("nowhere"), new AgentLog(new ImmediateTimeProvider(), _console), _ => 0).UnmountFrom(["/dev/nvme0n1"]);

        Assert.Empty(_unmounted);
        Assert.Contains("The kernel's list of what is mounted cannot be read", _console.ToString(), StringComparison.Ordinal);
    }

    // The disks whose mounts go are those the agent opened, by the device it opened each by.
    [Fact]
    public void TheDisksThatWereOpenedAreRemembered()
    {
        string device = _tree.Write(new byte[1024 * 1024], "dev", "nvme0n1");
        _tree.AddDisk("nvme0n1", 1024 * 1024);
        LinuxRawDisks disks = new(_tree.Root, new FileBlockDevices(), _ => { });

        Assert.Empty(disks.Opened);
        disks.Open(new LocalDisk(0, null, 1024 * 1024, StorageBusType.Nvme, 3) { Device = device }).Dispose();

        Assert.Equal([device], disks.Opened);
        MountsOf().UnmountFrom(disks.Opened);
        Assert.Equal(["/mnt/boot/efi", "/mnt"], _unmounted);
    }
}
