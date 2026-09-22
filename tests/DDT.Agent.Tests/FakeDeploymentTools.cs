// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Stands in for the disk, wimlib, bcdboot, the firmware boot order and the restart, and records each call in one
// journal so a test can check their order. FailAt names the call that throws Failure: list, prepare, partition,
// apply, bcd, firmware or reboot. The volumes are directories in a temporary folder, created by the partitioning,
// that Dispose removes.
internal sealed class FakeDeploymentTools : IDiskPartitioner, IImageApplier, IBcdWriter, IRebooter, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];

    public FakeDeploymentTools(params LocalDisk[] disks)
    {
        Disks = disks.Length > 0 ? [.. disks] : [Disk(0)];
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ddt-agent-deployment-{Guid.NewGuid():N}");

    public List<LocalDisk> Disks { get; }

    public string? FailAt { get; set; }

    public Exception Failure { get; set; } = new DeploymentStepException("The scripted step failed.");

    // When set, the apply reports 50 percent, completes ApplyStarted and then waits for it.
    public TaskCompletionSource? ApplyGate { get; set; }

    public TaskCompletionSource ApplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Runs on the applied Windows volume once the apply is done.
    public Action<string>? Applied { get; set; }

    // Runs when Windows is put first in the boot order, before FailAt is checked.
    public Action? PuttingWindowsFirst { get; set; }

    // When set, putting Windows first completes FirmwareStarted once it is recorded and then waits for it.
    public TaskCompletionSource? FirmwareGate { get; set; }

    public TaskCompletionSource FirmwareStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool ImageWasThereToApply { get; private set; }

    // The sizes of the last partitioning, in MB.
    public (int System, int Recovery)? PartitionSizes { get; private set; }

    // What the partitioning creates.
    public TargetVolumes Volumes => new(Path.Combine(Root, "S"), Path.Combine(Root, "W"), Path.Combine(Root, "R"), [ErasedSystemPartitionId])
    {
        SystemPartitionId = SystemPartitionId,
        WindowsPartitionId = WindowsPartitionId,
        RecoveryPartitionId = RecoveryPartitionId,
    };

    public static Guid SystemPartitionId { get; } = Guid.Parse("5a5a0000-0000-4000-8000-000000000001");

    public static Guid WindowsPartitionId { get; } = Guid.Parse("5a5a0000-0000-4000-8000-000000000002");

    public static Guid RecoveryPartitionId { get; } = Guid.Parse("5a5a0000-0000-4000-8000-000000000003");

    public static Guid ErasedSystemPartitionId { get; } = Guid.Parse("5a5a0000-0000-4000-8000-0000000000e0");

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public static LocalDisk Disk(int number, long sizeBytes = 256L * 1024 * 1024 * 1024, int partitions = 0) =>
        new(number, $"Test disk {number}", sizeBytes, StorageBusType.Nvme, partitions);

    public Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken)
    {
        Record("list");

        return Task.FromResult<IReadOnlyList<LocalDisk>>([.. Disks]);
    }

    public Task<TargetVolumes> PartitionAsync(
        LocalDisk disk,
        int systemPartitionMegabytes,
        int recoveryPartitionMegabytes,
        CancellationToken cancellationToken)
    {
        PartitionSizes = (systemPartitionMegabytes, recoveryPartitionMegabytes);
        Record("partition", $" {disk.Number}");

        TargetVolumes volumes = Volumes;
        Directory.CreateDirectory(volumes.System);
        Directory.CreateDirectory(volumes.Windows);
        Directory.CreateDirectory(volumes.Recovery);

        return Task.FromResult(volumes);
    }

    public void Prepare() => Record("prepare");

    public async Task ApplyAsync(string wimPath, int index, string targetRoot, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ImageWasThereToApply = File.Exists(wimPath);
        percent.Report(50);
        ApplyStarted.TrySetResult();

        if (ApplyGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        Record("apply", $" {index}");
        Directory.CreateDirectory(Path.Combine(targetRoot, "Windows", "System32"));
        Applied?.Invoke(targetRoot);
    }

    public Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        Record("bcd");

        return Task.CompletedTask;
    }

    // The journal says whether the answer file was there at that moment.
    public async Task PutWindowsFirstAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        PuttingWindowsFirst?.Invoke();
        Record("firmware", File.Exists(UnattendFile.PathIn(volumes.Windows)) ? " after the answer file" : " without the answer file");
        FirmwareStarted.TrySetResult();

        if (FirmwareGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }
    }

    public Task RestoreBootOrderAsync(CancellationToken cancellationToken)
    {
        Record("restore");

        return Task.CompletedTask;
    }

    public Task RebootAsync(CancellationToken cancellationToken)
    {
        Record("reboot");

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private void Record(string call, string detail = "")
    {
        lock (_lock)
        {
            _calls.Add(call + detail);
        }

        if (FailAt == call)
        {
            throw Failure;
        }
    }
}
