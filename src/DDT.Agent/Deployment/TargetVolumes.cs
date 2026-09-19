namespace DDT.Agent.Deployment;

// Root directories of the new partitions, such as S:\, W:\ and R:\.
public sealed record TargetVolumes(string System, string Windows, string Recovery);
