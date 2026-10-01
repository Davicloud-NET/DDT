// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Xml;
using System.Xml.Linq;

namespace DDT.Host.Startup;

// An IIS site of DDT's own that URL Rewrite and ARR forward to Kestrel. Browsers get IIS's name and certificate; agents
// keep talking to Kestrel. ARR keeps the Host header, so DDT sees the name the browser used.
public sealed class IisSite(Func<string, IReadOnlyList<string>, CommandResult> run, string folder, bool installed)
{
    public const string Name = "DDT";

    // IIS's own application ID for certificate bindings
    private const string IisAppId = "{4dc3e181-e14b-4a21-b022-59fc669b0914}";
    private const string ForwardedProto = "HTTP_X_FORWARDED_PROTO";
    private const string HttpsPrefix = "https/*:443:";

    private static readonly string s_appCmd = Path.Combine(Environment.SystemDirectory, "inetsrv", "appcmd.exe");
    private static readonly string s_netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");

    public static IisSite ForThisServer() => new(CommandResult.Run, Path.Combine(AppContext.BaseDirectory, "iis"), File.Exists(s_appCmd));

    // Returns null, or what stopped it
    public string? Publish(string host, string thumbprint, int port, TextWriter output)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentException.ThrowIfNullOrEmpty(thumbprint);
        ArgumentNullException.ThrowIfNull(output);

        if (!installed)
        {
            return "IIS isn't installed.";
        }

        string modules = AppCmd("list", "config", "-section:system.webServer/globalModules").Output;
        string information = $"*:443:{host}";

        if (Blocker(host, information, modules) is { } blocker)
        {
            return blocker;
        }

        Remove(output);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "web.config"), WebConfig(port));

        string? problem = Step(s_appCmd, "set", "config", "-section:system.webServer/proxy", "/enabled:true", "/preserveHostHeader:true", "/commit:apphost")
            ?? AllowForwardedProto()
            ?? Step(s_appCmd, "add", "apppool", $"/name:{Name}", "/managedRuntimeVersion:")
            ?? Step(s_appCmd, "add", "site", $"/name:{Name}", $"/physicalPath:{folder}", $"/bindings:https/{information}")
            ?? Step(
                s_appCmd, "set", "site", $"/site.name:{Name}",
                $"/bindings.[protocol='https',bindingInformation='{information}'].sslFlags:1",
                $"/applicationDefaults.applicationPool:{Name}")
            ?? Step(s_netsh, "http", "add", "sslcert", $"hostnameport={host}:443", $"certhash={thumbprint}", $"appid={IisAppId}", "certstorename=MY");

        if (problem is not null)
        {
            return problem;
        }

        output.WriteLine($"IIS serves DDT at https://{host}/ now and forwards to port {port}.");

        if (!modules.Contains("\"WebSocketModule\"", StringComparison.Ordinal))
        {
            output.WriteLine("IIS has no WebSocket Protocol feature, so live updates fall back to slower transports. Adding it helps.");
        }

        return null;
    }

    // What stops it before anything changes
    private string? Blocker(string host, string information, string modules)
    {
        if (!modules.Contains("\"RewriteModule\"", StringComparison.Ordinal))
        {
            return "IIS has no URL Rewrite module. Install URL Rewrite and ARR first.";
        }

        if (!modules.Contains("\"ApplicationRequestRouting\"", StringComparison.Ordinal))
        {
            return "IIS has no Application Request Routing module. Install ARR first.";
        }

        if (Sites().FirstOrDefault(site => site.Name != Name && site.Bindings.Contains($"https/{information}", StringComparer.OrdinalIgnoreCase)) is { Name: not null } taken)
        {
            return $"The IIS site {taken.Name} already serves https://{host}/. Pick another name.";
        }

        // preserveHostHeader is server-wide, IIS allows it nowhere else. So only where ARR proxied nothing yet.
        XElement? proxy = XmlRows(AppCmd("list", "config", "-section:system.webServer/proxy").Output, "proxy").FirstOrDefault();

        return (bool?)proxy?.Attribute("enabled") == true && (bool?)proxy?.Attribute("preserveHostHeader") != true
            ? "ARR already proxies on this server without keeping the host name, which DDT needs to see. " +
                "If the other proxied sites allow it, run " +
                $"{s_appCmd} set config -section:system.webServer/proxy /preserveHostHeader:true /commit:apphost " +
                "and then DDT.Host setup iis again."
            : null;
    }

    // Only a site in DDT's folder. An administrator's own site named DDT stays.
    public void Remove(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!installed)
        {
            return;
        }

        if (Sites().FirstOrDefault(site => site.Name == Name) is { Name: not null } site)
        {
            string path = XmlRows(AppCmd("list", "vdir", $"/app.name:{Name}/", "/xml").Output, "VDIR")
                .Select(vdir => (string?)vdir.Attribute("physicalPath"))
                .FirstOrDefault() ?? string.Empty;

            if (!SameFolder(Environment.ExpandEnvironmentVariables(path), folder))
            {
                output.WriteLine($"The IIS site {Name} serves {path}, not DDT's folder, so it stays.");

                return;
            }

            foreach (string binding in site.Bindings.Where(binding => binding.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                run(s_netsh, ["http", "delete", "sslcert", $"hostnameport={binding[HttpsPrefix.Length..]}:443"]);
            }

            AppCmd("delete", "site", Name);
            output.WriteLine($"Removed the IIS site {Name}.");
        }

        if (XmlRows(AppCmd("list", "apppool", $"/name:{Name}", "/xml").Output, "APPPOOL").Any())
        {
            AppCmd("delete", "apppool", Name);
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // UNENCODED_URL keeps escapes like %2F and holds the query string.
    public static string WebConfig(int port)
    {
        XElement configuration = new("configuration",
            new XElement("system.webServer",
                new XElement("rewrite",
                    new XElement("rules",
                        new XElement("rule",
                            new XAttribute("name", Name),
                            new XAttribute("stopProcessing", "true"),
                            new XElement("match", new XAttribute("url", ".*")),
                            new XElement("serverVariables",
                                new XElement("set", new XAttribute("name", ForwardedProto), new XAttribute("value", "https"))),
                            new XElement("action",
                                new XAttribute("type", "Rewrite"),
                                new XAttribute("url", $"https://localhost:{port}{{UNENCODED_URL}}"),
                                new XAttribute("appendQueryString", "false")))))));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + configuration + Environment.NewLine;
    }

    private string? AllowForwardedProto() =>
        AppCmd("list", "config", "-section:system.webServer/rewrite/allowedServerVariables").Output.Contains(ForwardedProto, StringComparison.OrdinalIgnoreCase)
            ? null
            : Step(s_appCmd, "set", "config", "-section:system.webServer/rewrite/allowedServerVariables", $"/+[name='{ForwardedProto}']", "/commit:apphost");

    private IEnumerable<(string Name, string[] Bindings)> Sites() =>
        XmlRows(AppCmd("list", "site", "/xml").Output, "SITE")
            .Select(site => (
                (string?)site.Attribute("SITE.NAME") ?? string.Empty,
                ((string?)site.Attribute("bindings") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)));

    private static IEnumerable<XElement> XmlRows(string xml, string row)
    {
        try
        {
            return XDocument.Parse(xml).Descendants(row);
        }
        catch (XmlException)
        {
            return [];
        }
    }

    private static bool SameFolder(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);

    private CommandResult AppCmd(params string[] arguments) => run(s_appCmd, arguments);

    // Returns null, or the command with what it said
    private string? Step(string program, params string[] arguments) =>
        run(program, arguments) is { ExitCode: not 0 } failed
            ? $"{Path.GetFileNameWithoutExtension(program)} {string.Join(' ', arguments)} failed: {failed.Output.Trim()}"
            : null;
}
