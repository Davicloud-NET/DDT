namespace DDT.Contracts.Deployments;

public sealed record DeploymentSummary(
    Guid Id,
    Guid? ImageId,
    string ImageName,
    DeploymentState State,
    DeploymentStep? Step,
    int Percent,
    DeploymentSource Source,
    string? RequestedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    string? Error);
