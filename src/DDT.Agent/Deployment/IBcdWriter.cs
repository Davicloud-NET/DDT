namespace DDT.Agent.Deployment;

public interface IBcdWriter
{
    // Makes the applied Windows bootable and registers its recovery environment.
    Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken);
}
