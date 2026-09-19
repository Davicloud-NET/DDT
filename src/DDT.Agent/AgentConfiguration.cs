namespace DDT.Agent;

// agent.json, written into the boot image beside the agent by Build-BootImage.ps1. KeyboardLayout is the name
// of the layout the image is set to, shown to whoever signs in.
public sealed record AgentConfiguration(string? ServerUrl, string? RootCertificate, string? KeyboardLayout);
