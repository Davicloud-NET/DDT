// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml.Linq;
using DDT.Host.Startup;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class IisSiteTests : IDisposable
{
    private const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ddt-iis-" + Guid.NewGuid().ToString("N"));
    private readonly FakeIis _iis = new();

    [Fact]
    public void TheWebConfigForwardsEverythingToKestrelOverHttps()
    {
        XElement rule = XDocument.Parse(IisSite.WebConfig(9443)).Descendants("rule").Single();
        XElement action = rule.Element("action")!;

        Assert.Equal(".*", (string?)rule.Element("match")!.Attribute("url"));
        Assert.Equal("https://localhost:9443{UNENCODED_URL}", (string?)action.Attribute("url"));
        Assert.Equal("false", (string?)action.Attribute("appendQueryString"));
        Assert.Equal("HTTP_X_FORWARDED_PROTO", (string?)rule.Descendants("set").Single().Attribute("name"));
    }

    [Fact]
    public void WithoutUrlRewriteNothingChanges()
    {
        _iis.Modules = "<add name=\"ApplicationRequestRouting\" />";
        using StringWriter output = new();

        string? problem = Site().Publish("ddt.contoso.com", Thumbprint, 8443, output);

        Assert.Contains("URL Rewrite", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(_iis.Calls, call => call.StartsWith("appcmd add", StringComparison.Ordinal) || call.StartsWith("appcmd set", StringComparison.Ordinal));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void AnotherSiteWithTheNameKeepsIt()
    {
        _iis.Sites = "<appcmd><SITE SITE.NAME=\"Portal\" bindings=\"http/*:80:,https/*:443:ddt.contoso.com\" /></appcmd>";
        using StringWriter output = new();

        string? problem = Site().Publish("ddt.contoso.com", Thumbprint, 8443, output);

        Assert.Contains("Portal", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(_iis.Calls, call => call.StartsWith("netsh", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishingMakesTheSiteAndBindsTheCertificateByName()
    {
        using StringWriter output = new();

        Assert.Null(Site().Publish("ddt.contoso.com", Thumbprint, 8443, output));

        Assert.Contains("appcmd set config -section:system.webServer/proxy /enabled:true /preserveHostHeader:true /commit:apphost", _iis.Calls);
        Assert.Contains("appcmd set config -section:system.webServer/rewrite/allowedServerVariables /+[name='HTTP_X_FORWARDED_PROTO'] /commit:apphost", _iis.Calls);
        Assert.Contains($"appcmd add site /name:DDT /physicalPath:{_folder} /bindings:https/*:443:ddt.contoso.com", _iis.Calls);
        Assert.Contains("appcmd set site /site.name:DDT /bindings.[protocol='https',bindingInformation='*:443:ddt.contoso.com'].sslFlags:1 /applicationDefaults.applicationPool:DDT", _iis.Calls);
        Assert.Contains(
            $"netsh http add sslcert hostnameport=ddt.contoso.com:443 certhash={Thumbprint} appid={{4dc3e181-e14b-4a21-b022-59fc669b0914}} certstorename=MY",
            _iis.Calls);
        Assert.Contains("https://localhost:8443{UNENCODED_URL}", File.ReadAllText(Path.Combine(_folder, "web.config")), StringComparison.Ordinal);
        Assert.StartsWith("IIS serves DDT at https://ddt.contoso.com/", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WhereArrAlreadyProxiesWithoutTheHostNameNothingChanges()
    {
        _iis.Proxy = "<system.webServer><proxy enabled=\"true\"><cache /></proxy></system.webServer>";
        using StringWriter output = new();

        string? problem = Site().Publish("ddt.contoso.com", Thumbprint, 8443, output);

        Assert.Contains("/preserveHostHeader:true", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(_iis.Calls, call => call.StartsWith("appcmd add", StringComparison.Ordinal) || call.StartsWith("appcmd set", StringComparison.Ordinal));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void WhereArrAlreadyKeepsTheHostNameItPublishes()
    {
        _iis.Proxy = "<system.webServer><proxy enabled=\"true\" preserveHostHeader=\"true\"><cache /></proxy></system.webServer>";
        using StringWriter output = new();

        Assert.Null(Site().Publish("ddt.contoso.com", Thumbprint, 8443, output));
    }

    [Fact]
    public void AFailedStepNamesItsCommand()
    {
        _iis.FailOn = "appcmd add site";
        using StringWriter output = new();

        string? problem = Site().Publish("ddt.contoso.com", Thumbprint, 8443, output);

        Assert.StartsWith("appcmd add site /name:DDT", problem, StringComparison.Ordinal);
        Assert.EndsWith("failed: no", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAllowedForwardedProtoIsNotAddedTwice()
    {
        _iis.AllowedVariables = "<allowedServerVariables><add name=\"HTTP_X_FORWARDED_PROTO\" /></allowedServerVariables>";
        using StringWriter output = new();

        Assert.Null(Site().Publish("ddt.contoso.com", Thumbprint, 8443, output));

        Assert.DoesNotContain(_iis.Calls, call => call.Contains("/+[name=", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingTakesDdtsSiteBindingPoolAndFolder()
    {
        Directory.CreateDirectory(_folder);
        _iis.Sites = "<appcmd><SITE SITE.NAME=\"DDT\" bindings=\"https/*:443:ddt.contoso.com\" /></appcmd>";
        _iis.Vdirs = $"<appcmd><VDIR physicalPath=\"{_folder}\" /></appcmd>";
        _iis.Pools = "<appcmd><APPPOOL APPPOOL.NAME=\"DDT\" /></appcmd>";
        using StringWriter output = new();

        Site().Remove(output);

        Assert.Contains("netsh http delete sslcert hostnameport=ddt.contoso.com:443", _iis.Calls);
        Assert.Contains("appcmd delete site DDT", _iis.Calls);
        Assert.Contains("appcmd delete apppool DDT", _iis.Calls);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void AnAdministratorsOwnSiteNamedDdtStays()
    {
        _iis.Sites = "<appcmd><SITE SITE.NAME=\"DDT\" bindings=\"https/*:443:ddt.contoso.com\" /></appcmd>";
        _iis.Vdirs = "<appcmd><VDIR physicalPath=\"%SystemDrive%\\inetpub\\ddt\" /></appcmd>";
        using StringWriter output = new();

        Site().Remove(output);

        Assert.DoesNotContain(_iis.Calls, call => call.Contains("delete", StringComparison.Ordinal));
        Assert.Contains("so it stays", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ddt contoso", Thumbprint, "isn't a host name")]
    [InlineData("ddt.contoso.com", "1234", "isn't a certificate thumbprint")]
    public void SetupIisChecksItsArguments(string host, string thumbprint, string problem)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The setup verbs run on Windows only.");
        using StringWriter output = new();

        Assert.Equal(1, SetupConsole.Run(["setup", "iis", host, thumbprint], output, Configuration("https://*:8443"), Site()));
        Assert.Contains(problem, output.ToString(), StringComparison.Ordinal);
        Assert.Empty(_iis.Calls);
    }

    [Fact]
    public void SetupIisNeedsDdtOffPort443()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The setup verbs run on Windows only.");
        using StringWriter output = new();

        Assert.Equal(1, SetupConsole.Run(["setup", "iis", "ddt.contoso.com", Thumbprint], output, Configuration("https://*:443"), Site()));
        Assert.Contains("listens on 443 itself", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private IisSite Site() => new(_iis.Run, _folder, installed: true);

    private static IConfiguration Configuration(string url) =>
        new ConfigurationBuilder().AddInMemoryCollection([new("Kestrel:Endpoints:Https:Url", url)]).Build();

    // Answers appcmd's list calls and records every call as "program arguments"
    private sealed class FakeIis
    {
        public List<string> Calls { get; } = [];

        public string Modules { get; set; } =
            "<add name=\"RewriteModule\" /><add name=\"ApplicationRequestRouting\" /><add name=\"WebSocketModule\" />";

        public string Sites { get; set; } = "<appcmd />";

        public string Vdirs { get; set; } = "<appcmd />";

        public string Pools { get; set; } = "<appcmd />";

        public string AllowedVariables { get; set; } = "<allowedServerVariables />";

        public string Proxy { get; set; } = "<system.webServer><proxy enabled=\"false\"><cache /></proxy></system.webServer>";

        // A call that starts with this exits 1 and says "no"
        public string? FailOn { get; set; }

        public CommandResult Run(string program, IReadOnlyList<string> arguments)
        {
            string call = $"{Path.GetFileNameWithoutExtension(program)} {string.Join(' ', arguments)}";
            Calls.Add(call);

            if (FailOn is not null && call.StartsWith(FailOn, StringComparison.Ordinal))
            {
                return new CommandResult(1, "no");
            }

            return new CommandResult(0, call switch
            {
                _ when call.Contains("globalModules", StringComparison.Ordinal) => Modules,
                _ when call.StartsWith("appcmd list config -section:system.webServer/proxy", StringComparison.Ordinal) => Proxy,
                _ when call.StartsWith("appcmd list site", StringComparison.Ordinal) => Sites,
                _ when call.StartsWith("appcmd list vdir", StringComparison.Ordinal) => Vdirs,
                _ when call.StartsWith("appcmd list apppool", StringComparison.Ordinal) => Pools,
                _ when call.StartsWith("appcmd list config -section:system.webServer/rewrite", StringComparison.Ordinal) => AllowedVariables,
                _ => string.Empty,
            });
        }
    }
}
