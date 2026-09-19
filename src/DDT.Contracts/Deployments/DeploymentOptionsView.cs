namespace DDT.Contracts.Deployments;

// ZeroTouchEnabled: an assignment to a waiting machine carries over to its next netboot from a listed network.
public sealed record DeploymentOptionsView(bool DomainConfigured, bool RequireWebApproval = false, bool ZeroTouchEnabled = false);
