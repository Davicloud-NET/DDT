// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DDT.Agent.Deployment;

// Puts the server's answer file where Setup looks for it in an applied image, and has Setup delete it once Windows
// is installed: it holds the local administrator's and the domain join account's passwords.
public static class UnattendFile
{
    public const string CleanupLine = "del /q /f \"%WINDIR%\\Panther\\unattend.xml\"";

    private static readonly XNamespace s_unattend = "urn:schemas-microsoft-com:unattend";

    // Where Setup looks for it when Windows was applied offline to windowsRoot.
    public static string PathIn(string windowsRoot) => Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");

    public static async Task WriteAsync(string windowsRoot, string unattend, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowsRoot);
        ArgumentNullException.ThrowIfNull(unattend);

        string path = PathIn(windowsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, unattend, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);

        string scripts = Path.Combine(windowsRoot, "Windows", "Setup", "Scripts");
        string setupComplete = Path.Combine(scripts, "SetupComplete.cmd");
        Directory.CreateDirectory(scripts);

        // An image may bring its own SetupComplete.cmd, which keeps its bytes. The cleanup goes first, so an exit in
        // the image's lines cannot skip it.
        byte[] existing = File.Exists(setupComplete) ? await File.ReadAllBytesAsync(setupComplete, cancellationToken).ConfigureAwait(false) : [];
        await File.WriteAllBytesAsync(setupComplete, [.. Encoding.ASCII.GetBytes($"{CleanupLine}\r\n"), .. existing], cancellationToken)
            .ConfigureAwait(false);
    }

    // Has Setup sign in as userName once after its last restart, which nothing outside Setup can time. False without an
    // answer file or its oobeSystem Shell-Setup component. The password is encoded as the answer file encodes every
    // password, which only hides it from a glance; the file goes when Setup has read it.
    public static async Task<bool> AddAutoLogonAsync(string windowsRoot, string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        string path = PathIn(windowsRoot);

        if (!File.Exists(path))
        {
            return false;
        }

        XDocument document;

        await using (FileStream file = File.OpenRead(path))
        {
            using XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Async = true });
            document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }

        XElement? shell = document.Root?
            .Elements(s_unattend + "settings")
            .Where(settings => (string?)settings.Attribute("pass") == "oobeSystem")
            .Elements(s_unattend + "component")
            .FirstOrDefault(component => (string?)component.Attribute("name") == "Microsoft-Windows-Shell-Setup");

        if (shell is null)
        {
            return false;
        }

        shell.Element(s_unattend + "AutoLogon")?.Remove();

        // First, as Windows System Image Manager orders the settings; the suffix is the one every account password gets.
        shell.AddFirst(new XElement(
            s_unattend + "AutoLogon",
            new XElement(
                s_unattend + "Password",
                new XElement(s_unattend + "Value", Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "Password"))),
                new XElement(s_unattend + "PlainText", "false")),
            new XElement(s_unattend + "Enabled", "true"),
            new XElement(s_unattend + "LogonCount", "1"),
            new XElement(s_unattend + "Username", userName)));

        XmlWriterSettings settings = new() { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Indent = true, Async = true };

        await using (FileStream file = File.Create(path))
        await using (XmlWriter writer = XmlWriter.Create(file, settings))
        {
            await document.SaveAsync(writer, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    // What the answer file sets, without any secret, for the log.
    public static string Summarize(string unattend)
    {
        XDocument document;

        try
        {
            using StringReader text = new(unattend);
            using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new DeploymentStepException($"The unattend file from the server is not valid XML ({exception.Message}).", exception);
        }

        string? Value(string name) => document.Descendants(s_unattend + name).Select(element => element.Value.Trim()).FirstOrDefault();

        bool localAdministrator = document.Descendants(s_unattend + "LocalAccount").Any();
        bool domain = document.Descendants(s_unattend + "JoinDomain").Any();

        string? computerName = Value("ComputerName");

        return $"computer name {(computerName is null or "*" ? "chosen by Windows" : computerName)}, " +
            $"time zone {Value("TimeZone") ?? "the default for the locale"}, " +
            $"UI language {Value("UILanguage") ?? "the image's"}, " +
            $"locale {Value("UserLocale") ?? "the image's"}, " +
            $"keyboard {Value("InputLocale") ?? "the image's"}, " +
            $"local administrator {(localAdministrator ? "yes" : "no")}, " +
            $"domain join {(domain ? "yes" : "no")}";
    }
}
