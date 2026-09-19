using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent.Deployment;

// Finds disks with IOCTLs and partitions one with diskpart. workDirectory holds the script (X:\DDT in Windows PE).
public sealed class DiskpartPartitioner(ToolRunner tools, AgentLog log, TimeProvider timeProvider, string workDirectory) : IDiskPartitioner
{
    // Disk numbers can have gaps, so a missing number does not end the search.
    private const int MaxDisks = 32;

    private const int StoragePropertyQueryLength = 12;
    private const int LengthInformationLength = 8;
    private const int MaxLayoutLength = 1024 * 1024;

    private static readonly TimeSpan s_volumeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_volumePollInterval = TimeSpan.FromMilliseconds(500);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        List<LocalDisk> disks = [];

        for (int number = 0; number < MaxDisks; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Probe(number) is { } disk)
            {
                disks.Add(disk);
            }
        }

        return Task.FromResult<IReadOnlyList<LocalDisk>>(disks);
    }

    public async Task<TargetVolumes> PartitionAsync(LocalDisk disk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        (char system, char windows, char recovery) = DriveLetters.Choose(DiskNativeMethods.GetLogicalDrives());
        string script = DiskpartScript.Build(disk.Number, system, windows, recovery);
        string path = Path.Combine(workDirectory, "partition.txt");

        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(path, script, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

        log.Information($"Partitioning disk {disk.Number} with this diskpart script:");

        foreach (string line in script.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            log.Information($"  {line}");
        }

        await tools.RunAsync(Path.Combine(Environment.SystemDirectory, "diskpart.exe"), ["/s", path], cancellationToken).ConfigureAwait(false);

        TargetVolumes volumes = new($"{system}:\\", $"{windows}:\\", $"{recovery}:\\");

        foreach (string root in new[] { volumes.System, volumes.Windows, volumes.Recovery })
        {
            await WaitForVolumeAsync(root, cancellationToken).ConfigureAwait(false);
        }

        return volumes;
    }

    // diskpart assigns letters before it exits, but the volume can still take a moment to mount.
    private async Task WaitForVolumeAsync(string root, CancellationToken cancellationToken)
    {
        long started = timeProvider.GetTimestamp();

        while (true)
        {
            if (FileSystemOf(root) is { } fileSystem)
            {
                log.Information($"Volume {root} is ready ({fileSystem}).");

                return;
            }

            if (timeProvider.GetElapsedTime(started) >= s_volumeTimeout)
            {
                throw new DeploymentStepException(
                    $"The new volume {root} did not appear within {s_volumeTimeout.TotalSeconds:0} seconds after diskpart. Check the diskpart output in the machine log.");
            }

            await Task.Delay(s_volumePollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static unsafe string? FileSystemOf(string root)
    {
        char* name = stackalloc char[64];

        return DiskNativeMethods.GetVolumeInformation(root, null, 0, out _, out _, out _, name, 64)
            ? new string(name)
            : null;
    }

    private LocalDisk? Probe(int number)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $@"\\.\PhysicalDrive{number}");

        using SafeFileHandle handle = DiskNativeMethods.CreateFile(
            path,
            DiskNativeMethods.GenericRead,
            DiskNativeMethods.FileShareRead | DiskNativeMethods.FileShareWrite,
            0,
            DiskNativeMethods.OpenExisting,
            0,
            0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            if (error is not (DiskNativeMethods.ErrorFileNotFound or DiskNativeMethods.ErrorPathNotFound))
            {
                log.Warning(error == DiskNativeMethods.ErrorAccessDenied
                    ? $"Disk {number} cannot be opened: access is denied. The agent has to run as an administrator."
                    : $"Disk {number} cannot be opened (Windows error {error}). It is left out.");
            }

            return null;
        }

        if (ReadDescriptor(handle) is not { } device)
        {
            log.Warning($"Disk {number} does not describe itself (Windows error {Marshal.GetLastPInvokeError()}). It is left out.");

            return null;
        }

        if (ReadLength(handle) is not { } size)
        {
            log.Warning($"The size of disk {number} cannot be read (Windows error {Marshal.GetLastPInvokeError()}). It is left out.");

            return null;
        }

        int partitions = ReadPartitionCount(handle, number);
        string? reason = DiskEligibility.ExclusionReason(device.RemovableMedia, device.BusType, size);
        LocalDisk disk = new(number, device.Model, size, device.BusType, partitions);

        log.Information(
            $"{disk.Describe()}, removable {(device.RemovableMedia ? "yes" : "no")}: " +
            (reason is null ? "DDT can install on it." : $"left out because {reason}."));

        return reason is null ? disk : null;
    }

    private static unsafe StorageDeviceInfo? ReadDescriptor(SafeFileHandle handle)
    {
        byte* query = stackalloc byte[StoragePropertyQueryLength];
        new Span<byte>(query, StoragePropertyQueryLength).Clear();

        byte* header = stackalloc byte[StorageDescriptorReader.HeaderLength];

        if (!DiskNativeMethods.DeviceIoControl(
            handle,
            DiskNativeMethods.IoctlStorageQueryProperty,
            query,
            StoragePropertyQueryLength,
            header,
            StorageDescriptorReader.HeaderLength,
            out _,
            0))
        {
            return null;
        }

        int size = Math.Max(StorageDescriptorReader.ReadSize(new ReadOnlySpan<byte>(header, StorageDescriptorReader.HeaderLength)), StorageDescriptorReader.MinimumLength);
        byte[] descriptor = new byte[size];

        fixed (byte* output = descriptor)
        {
            if (!DiskNativeMethods.DeviceIoControl(
                handle,
                DiskNativeMethods.IoctlStorageQueryProperty,
                query,
                StoragePropertyQueryLength,
                output,
                (uint)size,
                out uint returned,
                0)
                || returned < StorageDescriptorReader.MinimumLength)
            {
                return null;
            }

            return StorageDescriptorReader.Read(descriptor.AsSpan(0, (int)returned));
        }
    }

    private static unsafe long? ReadLength(SafeFileHandle handle)
    {
        byte* length = stackalloc byte[LengthInformationLength];

        return DiskNativeMethods.DeviceIoControl(
            handle,
            DiskNativeMethods.IoctlDiskGetLengthInfo,
            null,
            0,
            length,
            LengthInformationLength,
            out uint returned,
            0)
            && returned >= LengthInformationLength
            ? BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(length, LengthInformationLength))
            : null;
    }

    // Only shown to the technician, so a layout that cannot be read counts as none rather than hiding the disk.
    private unsafe int ReadPartitionCount(SafeFileHandle handle, int number)
    {
        int size = DriveLayoutReader.HeaderLength + (16 * DriveLayoutReader.EntryLength);

        while (size <= MaxLayoutLength)
        {
            byte[] layout = new byte[size];

            fixed (byte* output = layout)
            {
                if (DiskNativeMethods.DeviceIoControl(
                    handle,
                    DiskNativeMethods.IoctlDiskGetDriveLayoutEx,
                    null,
                    0,
                    output,
                    (uint)size,
                    out uint returned,
                    0))
                {
                    try
                    {
                        return DriveLayoutReader.CountUsedPartitions(layout.AsSpan(0, (int)returned));
                    }
                    catch (ArgumentException exception)
                    {
                        log.Warning($"The partitions of disk {number} cannot be read ({exception.Message})");

                        return 0;
                    }
                }
            }

            int error = Marshal.GetLastPInvokeError();

            if (error is not (DiskNativeMethods.ErrorInsufficientBuffer or DiskNativeMethods.ErrorMoreData))
            {
                log.Warning($"The partitions of disk {number} cannot be read (Windows error {error}).");

                return 0;
            }

            size *= 2;
        }

        return 0;
    }
}
