using System.Text;
using System.Xml;

namespace DDT.Core.Unattend;

public static class UnattendWriter
{
    private const string UnattendNamespace = "urn:schemas-microsoft-com:unattend";
    private const string WcmNamespace = "http://schemas.microsoft.com/WMIConfig/2002/State";
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string PublicKeyToken = "31bf3856ad364e35";

    // Setup reads a LocalAccount password with PlainText false as Base64 of UTF-16LE text ending in this suffix.
    private const string LocalAccountPasswordSuffix = "Password";

    // A password written by LocalAccounts expires after 42 days on clients, and the change Windows then forces
    // gives every PC its own password.
    private const string LiftPasswordAgeCommand = "net accounts /maxpwage:unlimited";

    public static string Write(UnattendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ProcessorArchitecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ComputerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.UiLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Keyboard);

        if (settings.LocalAdministrator is { } administrator)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(administrator.Name);
            ArgumentException.ThrowIfNullOrEmpty(administrator.Password);
        }

        if (settings.DomainJoin is { } join)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(join.Domain);
            ArgumentException.ThrowIfNullOrWhiteSpace(join.UserName);
            ArgumentException.ThrowIfNullOrEmpty(join.Password);
        }

        XmlWriterSettings xmlSettings = new()
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\r\n",
            // The default would rewrite a line break inside a password and change the value Setup reads.
            NewLineHandling = NewLineHandling.Entitize,
        };

        using MemoryStream stream = new();
        using XmlWriter writer = XmlWriter.Create(stream, xmlSettings);

        writer.WriteStartDocument();
        writer.WriteStartElement("unattend", UnattendNamespace);
        WriteSpecialize(writer, settings);
        WriteOobeSystem(writer, settings);
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSpecialize(XmlWriter writer, UnattendSettings settings)
    {
        StartPass(writer, "specialize");

        StartComponent(writer, "Microsoft-Windows-Shell-Setup", settings.ProcessorArchitecture);
        WriteElement(writer, "ComputerName", settings.ComputerName);

        // Without TimeZone, Windows picks the zone that matches the locale.
        if (!string.IsNullOrWhiteSpace(settings.TimeZone))
        {
            WriteElement(writer, "TimeZone", settings.TimeZone);
        }

        writer.WriteEndElement();

        if (settings.LocalAdministrator is not null)
        {
            StartComponent(writer, "Microsoft-Windows-Deployment", settings.ProcessorArchitecture);
            StartElement(writer, "RunSynchronous");
            StartElement(writer, "RunSynchronousCommand");
            writer.WriteAttributeString("action", WcmNamespace, "add");
            WriteElement(writer, "Order", "1");
            WriteElement(writer, "Path", LiftPasswordAgeCommand);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        if (settings.DomainJoin is { } join)
        {
            StartComponent(writer, "Microsoft-Windows-UnattendedJoin", settings.ProcessorArchitecture);
            StartElement(writer, "Identification");
            StartElement(writer, "Credentials");

            // Setup takes a UPN or DOMAIN\user here only when Credentials has no Domain element.
            WriteElement(writer, "Username", join.UserName);
            WriteElement(writer, "Password", join.Password);
            writer.WriteEndElement();
            WriteElement(writer, "JoinDomain", join.Domain);

            if (!string.IsNullOrWhiteSpace(join.OrganizationalUnit))
            {
                WriteElement(writer, "MachineObjectOU", join.OrganizationalUnit);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteOobeSystem(XmlWriter writer, UnattendSettings settings)
    {
        StartPass(writer, "oobeSystem");

        StartComponent(writer, "Microsoft-Windows-International-Core", settings.ProcessorArchitecture);
        WriteElement(writer, "InputLocale", settings.Keyboard);
        WriteElement(writer, "SystemLocale", settings.Locale);
        WriteElement(writer, "UILanguage", settings.UiLanguage);
        WriteElement(writer, "UserLocale", settings.Locale);
        writer.WriteEndElement();

        StartComponent(writer, "Microsoft-Windows-Shell-Setup", settings.ProcessorArchitecture);
        StartElement(writer, "OOBE");
        WriteElement(writer, "HideEULAPage", "true");
        WriteElement(writer, "HideOEMRegistrationScreen", "true");
        WriteElement(writer, "HideOnlineAccountScreens", "true");
        WriteElement(writer, "HideWirelessSetupInOOBE", "true");
        WriteElement(writer, "ProtectYourPC", "3");
        writer.WriteEndElement();

        if (settings.LocalAdministrator is { } administrator)
        {
            StartElement(writer, "UserAccounts");
            StartElement(writer, "LocalAccounts");
            StartElement(writer, "LocalAccount");
            writer.WriteAttributeString("action", WcmNamespace, "add");
            WriteElement(writer, "Name", administrator.Name);

            // Setup maps the English group name to the localized group of any image language.
            WriteElement(writer, "Group", "Administrators");
            StartElement(writer, "Password");
            WriteElement(writer, "Value", EncodeLocalAccountPassword(administrator.Password));
            WriteElement(writer, "PlainText", "false");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static string EncodeLocalAccountPassword(string password) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(password + LocalAccountPasswordSuffix));

    private static void StartPass(XmlWriter writer, string pass)
    {
        StartElement(writer, "settings");
        writer.WriteAttributeString("pass", pass);
    }

    private static void StartComponent(XmlWriter writer, string name, string processorArchitecture)
    {
        StartElement(writer, "component");
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("processorArchitecture", processorArchitecture);
        writer.WriteAttributeString("publicKeyToken", PublicKeyToken);
        writer.WriteAttributeString("language", "neutral");
        writer.WriteAttributeString("versionScope", "nonSxS");
        writer.WriteAttributeString("xmlns", "wcm", null, WcmNamespace);
        writer.WriteAttributeString("xmlns", "xsi", null, XsiNamespace);
    }

    private static void StartElement(XmlWriter writer, string name) => writer.WriteStartElement(name, UnattendNamespace);

    private static void WriteElement(XmlWriter writer, string name, string value) =>
        writer.WriteElementString(name, UnattendNamespace, value);
}
