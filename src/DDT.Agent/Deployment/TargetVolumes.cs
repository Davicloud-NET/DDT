namespace DDT.Agent.Deployment;

// Root directories of the new partitions, such as S:\, W:\ and R:\, and the unique GUIDs of the EFI system partitions
// the partitioning erased, which old firmware boot entries may still name.
public sealed record TargetVolumes(string System, string Windows, string Recovery, IReadOnlyList<Guid> ErasedSystemPartitionIds);
