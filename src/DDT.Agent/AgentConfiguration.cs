namespace DDT.Agent;

// agent.json, written into the boot image beside the agent by Build-BootImage.ps1. KeyboardLayout is the name
// of the layout startnet.cmd sets, shown to whoever signs in.
public sealed record AgentConfiguration(string? ServerUrl, string? EnrollmentToken, string? RootCertificate, string? KeyboardLayout);
