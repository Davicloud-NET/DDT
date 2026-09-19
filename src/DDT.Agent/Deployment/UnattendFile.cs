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

        // An image may bring its own SetupComplete.cmd, which keeps its lines.
        string existing = File.Exists(setupComplete) ? await File.ReadAllTextAsync(setupComplete, cancellationToken).ConfigureAwait(false) : "";
        string separator = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\r\n";
        await File.AppendAllTextAsync(setupComplete, $"{separator}{CleanupLine}\r\n", Encoding.ASCII, cancellationToken).ConfigureAwait(false);
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
