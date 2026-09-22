// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Xml.Linq;
using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

public sealed class UnattendWriterTests
{
    private const string ShellSetup = "Microsoft-Windows-Shell-Setup";
    private const string Deployment = "Microsoft-Windows-Deployment";
    private const string InternationalCore = "Microsoft-Windows-International-Core";

    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace s_wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
    private static readonly XNamespace s_xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly LocalAdministrator s_administrator = new("Admin", "Adm1n-Secret");

    private static UnattendSettings Settings(LocalAdministrator? administrator = null, string? timeZone = "W. Europe Standard Time") =>
        new("amd64", "PC-0042", timeZone, "en-US", "de-DE", "0407:00000407", administrator);

    private static XDocument Render(UnattendSettings settings) => XDocument.Parse(UnattendWriter.Write(settings));

    private static XElement Pass(XDocument document, string pass) =>
        Assert.Single(document.Root!.Elements(s_unattend + "settings"), s => (string?)s.Attribute("pass") == pass);

    private static XElement Component(XDocument document, string pass, string name) =>
        Assert.Single(Pass(document, pass).Elements(s_unattend + "component"), c => (string?)c.Attribute("name") == name);

    private static string[] ComponentNames(XDocument document, string pass) =>
        [.. Pass(document, pass).Elements(s_unattend + "component").Select(c => (string)c.Attribute("name")!)];

    private static string Text(XElement parent, params string[] path)
    {
        XElement current = parent;

        foreach (string name in path)
        {
            current = Assert.Single(current.Elements(s_unattend + name));
        }

        Assert.False(current.HasElements);

        return current.Value;
    }

    private static string Encoded(string password) => Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "Password"));

    [Fact]
    public void WritesAWellFormedUtf8DocumentInTheUnattendNamespace()
    {
        string xml = UnattendWriter.Write(Settings(s_administrator));
        XDocument document = XDocument.Parse(xml);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml, StringComparison.Ordinal);
        Assert.Equal(s_unattend + "unattend", document.Root!.Name);
        Assert.All(document.Descendants(), e => Assert.Equal(s_unattend, e.Name.Namespace));
        Assert.Equal(
            ["specialize", "oobeSystem"],
            document.Root.Elements().Select(e => (string?)e.Attribute("pass")));
    }

    [Fact]
    public void GivesEveryComponentTheIdentityWindowsSetupMatches()
    {
        XDocument document = Render(Settings(s_administrator) with { ProcessorArchitecture = "arm64" });
        XElement[] components = [.. document.Descendants(s_unattend + "component")];

        Assert.Equal(4, components.Length);
        Assert.All(components, component =>
        {
            Assert.Equal("arm64", (string?)component.Attribute("processorArchitecture"));
            Assert.Equal("31bf3856ad364e35", (string?)component.Attribute("publicKeyToken"));
            Assert.Equal("neutral", (string?)component.Attribute("language"));
            Assert.Equal("nonSxS", (string?)component.Attribute("versionScope"));
            Assert.Equal(s_wcm.NamespaceName, (string?)component.Attribute(XNamespace.Xmlns + "wcm"));
            Assert.Equal(s_xsi.NamespaceName, (string?)component.Attribute(XNamespace.Xmlns + "xsi"));
        });
    }

    [Theory]
    [InlineData(false, ShellSetup)]
    [InlineData(true, ShellSetup + "," + Deployment)]
    public void WritesTheComponentsOfEachCombination(bool withAdministrator, string specialize)
    {
        XDocument document = Render(Settings(withAdministrator ? s_administrator : null));

        Assert.Equal(specialize.Split(','), ComponentNames(document, "specialize"));
        Assert.Equal([InternationalCore, ShellSetup], ComponentNames(document, "oobeSystem"));
        Assert.Equal(withAdministrator, Component(document, "oobeSystem", ShellSetup).Element(s_unattend + "UserAccounts") is not null);
    }

    // The machine joins its domain in Windows, with credentials fetched while that step runs, so the join account's
    // password never lands in Panther\unattend.xml.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeverJoinsADomain(bool withAdministrator)
    {
        string xml = UnattendWriter.Write(Settings(withAdministrator ? s_administrator : null));
        string[] names = [.. XDocument.Parse(xml).Descendants().Select(e => e.Name.LocalName)];

        Assert.DoesNotContain("Microsoft-Windows-UnattendedJoin", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("JoinDomain", names);
        Assert.DoesNotContain("Credentials", names);
    }

    [Fact]
    public void WritesTheComputerNameAndTimeZoneInSpecialize()
    {
        XElement shell = Component(Render(Settings()), "specialize", ShellSetup);

        Assert.Equal(["ComputerName", "TimeZone"], shell.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("PC-0042", Text(shell, "ComputerName"));
        Assert.Equal("W. Europe Standard Time", Text(shell, "TimeZone"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void LeavesTheTimeZoneToWindowsWhenNoneIsSet(string? timeZone)
    {
        XDocument document = Render(Settings(s_administrator, timeZone));

        Assert.Empty(document.Descendants(s_unattend + "TimeZone"));
        Assert.Equal("PC-0042", Text(Component(document, "specialize", ShellSetup), "ComputerName"));
    }

    [Fact]
    public void WritesTheInternationalSettingsInOobeSystem()
    {
        XElement international = Component(Render(Settings()), "oobeSystem", InternationalCore);

        Assert.Equal(
            ["InputLocale", "SystemLocale", "UILanguage", "UserLocale"],
            international.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("0407:00000407", Text(international, "InputLocale"));
        Assert.Equal("de-DE", Text(international, "SystemLocale"));
        Assert.Equal("en-US", Text(international, "UILanguage"));
        Assert.Equal("de-DE", Text(international, "UserLocale"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HidesTheOobePagesThatNeedNoAnswer(bool withAdministrator)
    {
        XDocument document = Render(Settings(withAdministrator ? s_administrator : null));
        XElement oobe = Assert.Single(Component(document, "oobeSystem", ShellSetup).Elements(s_unattend + "OOBE"));

        Assert.Equal(
            [
                ("HideEULAPage", "true"),
                ("HideOEMRegistrationScreen", "true"),
                ("HideOnlineAccountScreens", "true"),
                ("HideWirelessSetupInOOBE", "true"),
                ("ProtectYourPC", "3"),
            ],
            oobe.Elements().Select(e => (e.Name.LocalName, e.Value)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeverSkipsOobeOrSignsInAutomatically(bool withAdministrator)
    {
        XDocument document = Render(Settings(withAdministrator ? s_administrator : null));
        string[] names = [.. document.Descendants().Select(e => e.Name.LocalName)];

        Assert.DoesNotContain("AutoLogon", names);
        Assert.DoesNotContain("SkipMachineOOBE", names);
        Assert.DoesNotContain("SkipUserOOBE", names);
        Assert.DoesNotContain("HideLocalAccountScreen", names);
        Assert.DoesNotContain("AdministratorPassword", names);
    }

    [Fact]
    public void CreatesTheLocalAdministratorWithAnEncodedPassword()
    {
        XElement shell = Component(Render(Settings(s_administrator)), "oobeSystem", ShellSetup);
        XElement account = Assert.Single(shell.Elements(s_unattend + "UserAccounts").Elements(s_unattend + "LocalAccounts").Elements());

        Assert.Equal(s_unattend + "LocalAccount", account.Name);
        Assert.Equal("add", (string?)account.Attribute(s_wcm + "action"));
        Assert.Equal("Admin", Text(account, "Name"));
        Assert.Equal("Administrators", Text(account, "Group"));
        Assert.Equal(Encoded("Adm1n-Secret"), Text(account, "Password", "Value"));
        Assert.Equal("false", Text(account, "Password", "PlainText"));
    }

    [Fact]
    public void EncodesTheLocalAdministratorPasswordLikeMicrosoftsSample()
    {
        // Microsoft's LocalAccount documentation hides "Password12346" as this value.
        XElement shell = Component(Render(Settings(new LocalAdministrator("Admin", "Password12346"))), "oobeSystem", ShellSetup);

        Assert.Equal(
            "UABhAHMAcwB3AG8AcgBkADEAMgAzADQANgBQAGEAcwBzAHcAbwByAGQA",
            Text(shell, "UserAccounts", "LocalAccounts", "LocalAccount", "Password", "Value"));
    }

    [Fact]
    public void LiftsThePasswordAgeLimitWithALocalAdministrator()
    {
        XElement deployment = Component(Render(Settings(s_administrator)), "specialize", Deployment);
        XElement command = Assert.Single(deployment.Elements(s_unattend + "RunSynchronous").Elements());

        Assert.Equal(s_unattend + "RunSynchronousCommand", command.Name);
        Assert.Equal("add", (string?)command.Attribute(s_wcm + "action"));
        Assert.Equal(["Order", "Path"], command.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("1", Text(command, "Order"));
        Assert.Equal("net accounts /maxpwage:unlimited", Text(command, "Path"));
    }

    [Fact]
    public void RunsNoCommandWithoutALocalAdministrator()
    {
        string xml = UnattendWriter.Write(Settings());

        Assert.DoesNotContain("maxpwage", xml, StringComparison.Ordinal);
        Assert.Empty(XDocument.Parse(xml).Descendants(s_unattend + "RunSynchronous"));
    }

    [Fact]
    public void WritesTheSecretOnlyIntoItsPasswordElement()
    {
        string xml = UnattendWriter.Write(Settings(s_administrator));
        XDocument document = XDocument.Parse(xml);
        XText[] texts = [.. document.DescendantNodes().OfType<XText>()];

        Assert.DoesNotContain("Adm1n", xml, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Descendants().Attributes(), a => a.Value.Contains("Secret", StringComparison.Ordinal));

        XText administrator = Assert.Single(texts, t => t.Value == Encoded("Adm1n-Secret"));
        Assert.Equal(s_unattend + "Value", administrator.Parent!.Name);
        Assert.Equal(s_unattend + "Password", administrator.Parent.Parent!.Name);
        Assert.Equal(s_unattend + "LocalAccount", administrator.Parent.Parent.Parent!.Name);
    }

    [Fact]
    public void EscapesHostileValuesSoTheyCannotChangeTheStructure()
    {
        static string Hostile(string field) => $"{field}&amp;<\"'></ComputerName><AutoLogon>]]><!--";

        UnattendSettings benign = Settings(s_administrator);
        UnattendSettings hostile = new(
            Hostile("arch"),
            Hostile("name"),
            Hostile("zone"),
            Hostile("ui"),
            Hostile("locale"),
            Hostile("keyboard"),
            new LocalAdministrator(Hostile("admin"), Hostile("adminpw")));

        XDocument document = Render(hostile);

        Assert.Equal(
            Render(benign).Descendants().Select(e => e.Name),
            document.Descendants().Select(e => e.Name));
        Assert.Empty(document.DescendantNodes().OfType<XComment>());
        Assert.All(
            document.Descendants(s_unattend + "component"),
            c => Assert.Equal(Hostile("arch"), (string?)c.Attribute("processorArchitecture")));

        XElement specializeShell = Component(document, "specialize", ShellSetup);
        Assert.Equal(Hostile("name"), Text(specializeShell, "ComputerName"));
        Assert.Equal(Hostile("zone"), Text(specializeShell, "TimeZone"));

        XElement international = Component(document, "oobeSystem", InternationalCore);
        Assert.Equal(Hostile("keyboard"), Text(international, "InputLocale"));
        Assert.Equal(Hostile("locale"), Text(international, "SystemLocale"));
        Assert.Equal(Hostile("ui"), Text(international, "UILanguage"));
        Assert.Equal(Hostile("locale"), Text(international, "UserLocale"));

        XElement account = Assert.Single(Component(document, "oobeSystem", ShellSetup).Descendants(s_unattend + "LocalAccount"));
        Assert.Equal(Hostile("admin"), Text(account, "Name"));
        Assert.Equal(Encoded(Hostile("adminpw")), Text(account, "Password", "Value"));
    }

    [Fact]
    public void KeepsLineBreaksAndTabsInsideValues()
    {
        const string password = "first\r\nsecond\nthird\rfourth\tfifth";

        XDocument document = Render(Settings(new LocalAdministrator("Admin", password)));
        string encoded = Text(Assert.Single(document.Descendants(s_unattend + "LocalAccount")), "Password", "Value");

        Assert.Equal(password + "Password", Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
        Assert.Equal("first\r\nsecond", Render(Settings() with { ComputerName = "first\r\nsecond" }).Descendants(s_unattend + "ComputerName").Single().Value);
    }

    [Fact]
    public void RefusesCharactersXmlCannotCarry()
    {
        Assert.Throws<ArgumentException>(() => UnattendWriter.Write(Settings() with { ComputerName = "PC" }));
    }

    [Fact]
    public void RefusesMissingRequiredValues()
    {
        Assert.Throws<ArgumentNullException>(() => UnattendWriter.Write(null!));
        Assert.Throws<ArgumentException>(() => UnattendWriter.Write(Settings() with { ComputerName = "" }));
        Assert.Throws<ArgumentException>(() => UnattendWriter.Write(Settings() with { ProcessorArchitecture = " " }));
        Assert.Throws<ArgumentException>(() => UnattendWriter.Write(Settings(new LocalAdministrator("Admin", ""))));
        Assert.Throws<ArgumentException>(() => UnattendWriter.Write(Settings(new LocalAdministrator(" ", "Adm1n-Secret"))));
    }

    [Fact]
    public void KeepsPasswordsOutOfTheSettingsText()
    {
        string text = Settings(s_administrator).ToString();

        Assert.Contains("Admin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
    }
}
