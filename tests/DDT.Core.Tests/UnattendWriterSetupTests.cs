// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

// The product key, organization, owner, out-of-box pages and static network, checked as the text Setup reads.
public sealed class UnattendWriterSetupTests
{
    private const string Key = "VK7JG-NPHTM-C97JM-9MPGT-3V66T";

    private const string Identity =
        "processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" "
        + "xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    private static readonly UnattendSettings s_plain = new("amd64", "PC-0042", "UTC", "de-DE", "de-DE", "de-DE", null);

    private static string Lines(string text) => text.ReplaceLineEndings("\r\n");

    [Fact]
    public void WritesTheKeyOrganizationAndOwnerInSpecializeBeforeTheTimeZone()
    {
        string xml = UnattendWriter.Write(s_plain with
        {
            ProductKey = new ProductKey(Key),
            RegisteredOrganization = "Davicloud",
            RegisteredOwner = "IT",
        });

        Assert.Contains(
            Lines($"""
                  <settings pass="specialize">
                    <component name="Microsoft-Windows-Shell-Setup" {Identity}>
                      <ComputerName>PC-0042</ComputerName>
                      <ProductKey>{Key}</ProductKey>
                      <RegisteredOrganization>Davicloud</RegisteredOrganization>
                      <RegisteredOwner>IT</RegisteredOwner>
                      <TimeZone>UTC</TimeZone>
                    </component>
                  </settings>
                """),
            xml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutAnEmptyOrganizationAndOwner()
    {
        string xml = UnattendWriter.Write(s_plain with { RegisteredOrganization = " ", RegisteredOwner = "" });

        Assert.DoesNotContain("Registered", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductKey", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutTheOobePagesItShows()
    {
        string xml = UnattendWriter.Write(s_plain with { Oobe = new OobePages(Eula: true, OemRegistration: false, OnlineAccounts: false, WirelessSetup: true) });

        Assert.Contains(
            Lines("""
                      <OOBE>
                        <HideEULAPage>true</HideEULAPage>
                        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                        <ProtectYourPC>3</ProtectYourPC>
                      </OOBE>
                """),
            xml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SetsAStaticNetworkOnTheAdapterByItsMacAddress()
    {
        StaticNetwork network = new("00-15-5D-01-02-03", ["10.0.4.51/24", "10.0.5.51/24"], "10.0.4.1", ["10.0.4.2", "10.0.4.3"]);

        string xml = UnattendWriter.Write(s_plain with { Network = network });

        Assert.Contains(
            Lines($"""
                    </component>
                    <component name="Microsoft-Windows-TCPIP" {Identity}>
                      <Interfaces>
                        <Interface wcm:action="add">
                          <Ipv4Settings>
                            <DhcpEnabled>false</DhcpEnabled>
                          </Ipv4Settings>
                          <Identifier>00-15-5D-01-02-03</Identifier>
                          <UnicastIpAddresses>
                            <IpAddress wcm:action="add" wcm:keyValue="1">10.0.4.51/24</IpAddress>
                            <IpAddress wcm:action="add" wcm:keyValue="2">10.0.5.51/24</IpAddress>
                          </UnicastIpAddresses>
                          <Routes>
                            <Route wcm:action="add">
                              <Identifier>1</Identifier>
                              <NextHopAddress>10.0.4.1</NextHopAddress>
                              <Prefix>0.0.0.0/0</Prefix>
                            </Route>
                          </Routes>
                        </Interface>
                      </Interfaces>
                    </component>
                    <component name="Microsoft-Windows-DNS-Client" {Identity}>
                      <Interfaces>
                        <Interface wcm:action="add">
                          <Identifier>00-15-5D-01-02-03</Identifier>
                          <DNSServerSearchOrder>
                            <IpAddress wcm:action="add" wcm:keyValue="1">10.0.4.2</IpAddress>
                            <IpAddress wcm:action="add" wcm:keyValue="2">10.0.4.3</IpAddress>
                          </DNSServerSearchOrder>
                        </Interface>
                      </Interfaces>
                    </component>
                  </settings>
                  <settings pass="oobeSystem">
                """),
            xml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WritesNoRouteWithoutAGatewayAndNoDnsWithoutServers()
    {
        string xml = UnattendWriter.Write(s_plain with { Network = new StaticNetwork("00-15-5D-01-02-03", ["10.0.4.51/24"], null, []) });

        Assert.Contains("Microsoft-Windows-TCPIP", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Routes>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft-Windows-DNS-Client", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAProductKeySetupCannotReadWithoutNamingIt()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => UnattendWriter.Write(s_plain with { ProductKey = new ProductKey("SECRET-KEY") }));

        Assert.DoesNotContain("SECRET", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheProductKeyOutOfTheSettingsText()
    {
        string text = (s_plain with { ProductKey = new ProductKey(Key) }).ToString();

        Assert.Contains("ProductKey", text, StringComparison.Ordinal);
        Assert.DoesNotContain("VK7JG", text, StringComparison.Ordinal);
    }
}
