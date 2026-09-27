// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// Stands in for the disk, wimlib, bcdboot, the firmware boot order and the restart, and in the installed Windows for
// setup, the domain join, the due restart, the agent's removal and what Windows deletes when it next starts, and records
// each call in one journal so a test can check their order. FailAt names the call that throws Failure: list, prepare,
// partition, apply, bcd, firmware, reboot, find, join, remove, or one passed to Note. The volumes are directories in a
// temporary folder, created by the partitioning, that Dispose removes.
internal sealed class FakeDeploymentTools
    : IDiskPartitioner, IImageApplier, IBcdWriter, IRebooter, IWindowsSetupProbe, IDomainJoiner, IRestartMarker, IAgentRemoval, IRestartDeleter, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly Queue<string> _setupPending = new();
    private readonly Queue<int> _joinAnswers = new();
    private readonly List<string> _deletedAtRestart = [];
    private AgentJoinDomainCredentials? _joinedWith;

    public FakeDeploymentTools(params LocalDisk[] disks)
    {
        Disks = disks.Length > 0 ? [.. disks] : [Disk(0)];
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ddt-agent-deployment-{Guid.NewGuid():N}");

    public List<LocalDisk> Disks { get; }

    public string? FailAt { get; set; }

    public Exception Failure { get; set; } = new DeploymentStepException("The scripted step failed.");

    // When set, the partitioning waits for it once it is recorded.
    public TaskCompletionSource? PartitionGate { get; set; }

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

    public async Task<TargetVolumes> PartitionAsync(
        LocalDisk disk,
        int systemPartitionMegabytes,
        int recoveryPartitionMegabytes,
        CancellationToken cancellationToken)
    {
        PartitionSizes = (systemPartitionMegabytes, recoveryPartitionMegabytes);
        Record("partition", $" {disk.Number}");

        if (PartitionGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        TargetVolumes volumes = Volumes;
        Directory.CreateDirectory(volumes.System);
        Directory.CreateDirectory(volumes.Windows);
        Directory.CreateDirectory(volumes.Recovery);

        return volumes;
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

    // Into Windows it is a plain reboot, as before task sequences.
    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        Record("reboot", into == RestartInto.WindowsPE ? " into Windows PE" : string.Empty);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> CleanAsync(LocalDisk disk, CancellationToken cancellationToken)
    {
        Record("clean", $" {disk.Number}");

        return Task.FromResult<IReadOnlyList<Guid>>([ErasedSystemPartitionId]);
    }

    // The journal names the loader and the partition it is on.
    public Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken)
    {
        Record("firmware", $" {description} {loaderPath} on partition {esp.PartitionNumber}");

        return Task.CompletedTask;
    }

    public Task<TargetVolumes> FindAsync(RunDiskIds ids, string windowsRoot, CancellationToken cancellationToken)
    {
        Record("find", $" {ids.Windows}");

        return Task.FromResult(new TargetVolumes(Path.Combine(Root, "S"), windowsRoot, Path.Combine(Root, "R"), ids.ErasedSystemPartitionIds)
        {
            SystemPartitionId = ids.System,
            WindowsPartitionId = ids.Windows,
            RecoveryPartitionId = ids.Recovery,
        });
    }

    // What setup is still doing at each of the next looks, after which it has finished.
    public void SetupRuns(params string[] pending)
    {
        lock (_lock)
        {
            foreach (string step in pending)
            {
                _setupPending.Enqueue(step);
            }
        }
    }

    // Each look goes into the journal as setup, with what setup was still doing.
    public string? Pending()
    {
        string? pending;

        lock (_lock)
        {
            _setupPending.TryDequeue(out pending);
        }

        Record("setup", pending is null ? " finished" : $" {pending}");

        return pending;
    }

    // The account of the last join, which the journal never names.
    public AgentJoinDomainCredentials? JoinedWith
    {
        get
        {
            lock (_lock)
            {
                return _joinedWith;
            }
        }
    }

    // What NetJoinDomain answers to each of the next joins, after which a join works.
    public void JoinAnswers(params int[] codes)
    {
        lock (_lock)
        {
            foreach (int code in codes)
            {
                _joinAnswers.Enqueue(code);
            }
        }
    }

    public Task<int> JoinAsync(AgentJoinDomainCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        int code;

        lock (_lock)
        {
            _joinedWith = credentials;
            code = _joinAnswers.TryDequeue(out int answer) ? answer : 0;
        }

        Record("join", $" {credentials.Domain}");

        return Task.FromResult(code);
    }

    // Set until the test says Windows restarted, as the restart itself would.
    public bool RestartDue { get; set; }

    bool IRestartMarker.IsSet => RestartDue;

    void IRestartMarker.Set()
    {
        RestartDue = true;
        Record("restart due");
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        Record("remove");

        return Task.CompletedTask;
    }

    // In the journal relative to the Windows volume.
    public void DeleteAtRestart(string path)
    {
        lock (_lock)
        {
            _deletedAtRestart.Add(path);
        }

        Record("delete at restart", $" {Path.GetRelativePath(Volumes.Windows, path)}");
    }

    // Deletes what was marked, in order, as Windows does when it starts: a directory only once it is empty.
    public void DeleteMarkedAsWindowsStarts()
    {
        List<string> marked;

        lock (_lock)
        {
            marked = [.. _deletedAtRestart];
            _deletedAtRestart.Clear();
        }

        foreach (string path in marked)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
    }

    // Another fake's call, such as a tool run, in the same journal, so a test can check the order of both.
    public void Note(string call) => Record(call);

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
