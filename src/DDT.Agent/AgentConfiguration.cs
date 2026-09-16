namespace DDT.Agent;

// agent.json, written into the boot image beside the agent by Build-BootImage.ps1.
public sealed record AgentConfiguration(string? ServerUrl, string? EnrollmentToken, string? RootCertificate);
