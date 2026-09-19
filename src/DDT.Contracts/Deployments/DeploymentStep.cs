namespace DDT.Contracts.Deployments;

public enum DeploymentStep
{
    Partition,
    Download,
    Apply,
    Boot,
    Unattend,
    Reboot,
}
