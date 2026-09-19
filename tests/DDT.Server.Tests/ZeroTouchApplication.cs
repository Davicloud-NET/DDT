namespace DDT.Server.Tests;

// Machines that netboot from 10.200.0.0/16 or fd00:200::/64 keep an approval a web assignment gave them.
// TestRemoteAddress hands out addresses in 10.0.0.0/16, outside both.
public sealed class ZeroTouchApplication() : SettingsApplication(("DDT:Machines:ZeroTouchNetworks", "10.200.0.0/16, fd00:200::/64"));
