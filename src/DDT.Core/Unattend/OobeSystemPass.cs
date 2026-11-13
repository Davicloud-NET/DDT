// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text;
using System.Xml;
using static DDT.Core.Unattend.UnattendXml;

namespace DDT.Core.Unattend;

internal static class OobeSystemPass
{
    // Setup reads a LocalAccount or AutoLogon password with PlainText false as Base64 of UTF-16LE text ending in this
    // suffix.
    private const string PasswordSuffix = "Password";

    // Setup adds 1 to a LogonCount above 0, so one sign-in needs this first command. See Microsoft's LogonCount page.
    public const string OneLogonCommand = @"reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"" /v AutoLogonCount /t REG_DWORD /d 0 /f";

    public static void Write(XmlWriter writer, UnattendSettings settings)
    {
        StartPass(writer, "oobeSystem");

        StartComponent(writer, "Microsoft-Windows-International-Core", settings.ProcessorArchitecture);
        WriteElement(writer, "InputLocale", settings.Keyboard);
        WriteElement(writer, "SystemLocale", settings.Locale);
        WriteElement(writer, "UILanguage", settings.UiLanguage);
        WriteElement(writer, "UserLocale", settings.Locale);
        writer.WriteEndElement();

        StartComponent(writer, "Microsoft-Windows-Shell-Setup", settings.ProcessorArchitecture);
        WriteAutoLogon(writer, settings.AutoLogon);
        WriteFirstLogonCommands(writer, settings.AutoLogon is { Count: 1 } ? [OneLogonCommand, .. settings.FirstLogonCommands] : settings.FirstLogonCommands);
        WriteOobe(writer, settings.Oobe);

        if (settings.LocalAdministrator is { } administrator)
        {
            StartElement(writer, "UserAccounts");
            StartElement(writer, "LocalAccounts");
            StartAdded(writer, "LocalAccount");
            WriteElement(writer, "Name", administrator.Name);

            // Setup maps the English group name to the localized group of any image language.
            WriteElement(writer, "Group", "Administrators");
            StartElement(writer, "Password");
            WriteElement(writer, "Value", EncodePassword(administrator.Password));
            WriteElement(writer, "PlainText", Flag(false));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    // In the order Windows System Image Manager writes them. The domain is left out for a local account.
    private static void WriteAutoLogon(XmlWriter writer, AutoLogonAccount? account)
    {
        if (account is null)
        {
            return;
        }

        StartElement(writer, "AutoLogon");
        StartElement(writer, "Password");
        WriteElement(writer, "Value", EncodePassword(account.Password));
        WriteElement(writer, "PlainText", Flag(false));
        writer.WriteEndElement();
        WriteOptional(writer, "Domain", account.Domain);
        WriteElement(writer, "Enabled", Flag(true));
        WriteElement(writer, "LogonCount", (account.Count > 1 ? account.Count - 1 : 1).ToString(CultureInfo.InvariantCulture));
        WriteElement(writer, "Username", account.UserName);
        writer.WriteEndElement();
    }

    private static void WriteFirstLogonCommands(XmlWriter writer, IReadOnlyList<string> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        StartElement(writer, "FirstLogonCommands");

        for (int index = 0; index < commands.Count; index++)
        {
            StartAdded(writer, "SynchronousCommand");
            WriteElement(writer, "CommandLine", commands[index]);
            WriteElement(writer, "Order", (index + 1).ToString(CultureInfo.InvariantCulture));
            WriteElement(writer, "RequiresUserInput", Flag(false));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    // A page left shown is left out, so Setup shows it as it would without an answer file.
    private static void WriteOobe(XmlWriter writer, OobePages pages)
    {
        StartElement(writer, "OOBE");
        WriteHidden(writer, "HideEULAPage", pages.Eula);
        WriteHidden(writer, "HideOEMRegistrationScreen", pages.OemRegistration);
        WriteHidden(writer, "HideOnlineAccountScreens", pages.OnlineAccounts);
        WriteHidden(writer, "HideWirelessSetupInOOBE", pages.WirelessSetup);
        WriteElement(writer, "ProtectYourPC", "3");
        writer.WriteEndElement();
    }

    private static void WriteHidden(XmlWriter writer, string name, bool hidden)
    {
        if (hidden)
        {
            WriteElement(writer, name, Flag(true));
        }
    }

    private static string EncodePassword(string password) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(password + PasswordSuffix));
}
