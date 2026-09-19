namespace DDT.Agent.Deployment;

public interface IRebooter
{
    Task RebootAsync(CancellationToken cancellationToken);
}
