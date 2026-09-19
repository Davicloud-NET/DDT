namespace DDT.Agent.Deployment;

public interface IDiskPartitioner
{
    // The disks DDT could install on. Every disk probed is logged, with the reason when it is left out.
    Task<IReadOnlyList<LocalDisk>> ListDisksAsync(CancellationToken cancellationToken);

    // Erases the disk and creates the system, Windows and recovery partitions.
    Task<TargetVolumes> PartitionAsync(LocalDisk disk, CancellationToken cancellationToken);
}
