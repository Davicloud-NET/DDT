// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Xml;
using static DDT.Core.Unattend.UnattendXml;

namespace DDT.Core.Unattend;

internal static class OobeSystemPass
{
    // Setup reads a LocalAccount password with PlainText false as Base64 of UTF-16LE text ending in this suffix.
    private const string LocalAccountPasswordSuffix = "Password";

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
            WriteElement(writer, "Value", EncodeLocalAccountPassword(administrator.Password));
            WriteElement(writer, "PlainText", Flag(false));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
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

    private static string EncodeLocalAccountPassword(string password) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(password + LocalAccountPasswordSuffix));
}
