namespace DDT.Contracts.Deployments;

// What the web UI needs to say what an assignment does before it is sent. ZeroTouchEnabled: an assignment to a machine
// that is not waiting carries over to its next netboot from a listed network, which needs web approval to be off.
public sealed record DeploymentOptionsView(bool DomainConfigured, bool RequireWebApproval, bool ZeroTouchEnabled);
