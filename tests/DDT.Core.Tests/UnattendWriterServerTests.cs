// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Xml.Linq;
using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

// Server's Setup stops for the built-in Administrator's password unless the answer file sets it.
public sealed class UnattendWriterServerTests
{
    private const string Secret = "Test-Only-7";

    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    private static XElement UserAccounts(string name, bool server) =>
        XDocument.Parse(UnattendWriter.Write(new UnattendSettings("amd64", "*", null, "en-US", "en-US", "en-US", new LocalAdministrator(name, Secret))
        {
            ServerEdition = server,
        })).Descendants(s_unattend + "UserAccounts").Single();

    // Microsoft's AdministratorPassword page: the hidden value is the password followed by "AdministratorPassword".
    [Fact]
    public void OnServerSetsTheBuiltInAdministratorsPasswordAndAddsTheAccount()
    {
        XElement accounts = UserAccounts("Admin", server: true);

        XElement password = accounts.Element(s_unattend + "AdministratorPassword") ?? throw new InvalidOperationException("No AdministratorPassword.");
        Assert.Equal(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(Secret + "AdministratorPassword")),
            password.Element(s_unattend + "Value")?.Value);
        Assert.Equal("false", password.Element(s_unattend + "PlainText")?.Value);
        Assert.Equal("Admin", accounts.Descendants(s_unattend + "Name").Single().Value);
        Assert.Equal(["AdministratorPassword", "LocalAccounts"], accounts.Elements().Select(element => element.Name.LocalName));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("administrator")]
    public void OnServerAnAccountNamedAdministratorIsTheBuiltInOne(string name)
    {
        XElement accounts = UserAccounts(name, server: true);

        Assert.Equal(["AdministratorPassword"], accounts.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void OnAClientAnAccountNamedAdministratorIsStillAdded()
    {
        XElement accounts = UserAccounts("Administrator", server: false);

        Assert.Equal(["LocalAccounts"], accounts.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void WithoutTheLocalAdministratorNothingIsSetOnServer()
    {
        string xml = UnattendWriter.Write(new UnattendSettings("amd64", "*", null, "en-US", "en-US", "en-US", null) { ServerEdition = true });

        Assert.DoesNotContain("UserAccounts", xml, StringComparison.Ordinal);
    }
}
