// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using WixToolset.Dtf.WindowsInstaller;

namespace DDT.Installer.CustomActions;

// 1 Domain, 2 Private, 4 Public
public static class FirewallProfiles
{
    private const string Property = "FIREWALLPROFILES";
    private const int Public = 4;

    // ServerDlg's checkbox, FIREWALLPUBLIC=1
    [CustomAction]
    public static ActionResult AllowPublicNetworks(Session session)
    {
        session[Property] = (Allowed(session) | Public).ToString(CultureInfo.InvariantCulture);

        return ActionResult.Success;
    }

    public static int Allowed(Session session) =>
        int.TryParse(session[Property], NumberStyles.None, CultureInfo.InvariantCulture, out int profiles) ? profiles : 3;

    // Interface index to profile. A card without a profile is missing, so it never shows up as blocked
    public static Dictionary<int, int> OfCards()
    {
        Dictionary<int, int> profiles = [];

        try
        {
            using ManagementObjectSearcher searcher = new(@"root\StandardCimv2", "SELECT InterfaceIndex, NetworkCategory FROM MSFT_NetConnectionProfile");

            foreach (ManagementBaseObject profile in searcher.Get())
            {
                using (profile)
                {
                    int index = Convert.ToInt32(profile["InterfaceIndex"], CultureInfo.InvariantCulture);

                    // NetworkCategory: 0 Public, 1 Private, 2 DomainAuthenticated
                    profiles[index] = Convert.ToInt32(profile["NetworkCategory"], CultureInfo.InvariantCulture) switch
                    {
                        1 => 2,
                        2 => 1,
                        _ => Public,
                    };
                }
            }
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException)
        {
            // No warning then
        }

        return profiles;
    }

    public static string Name(int profile) => profile switch
    {
        1 => "domain",
        2 => "private",
        _ => "public",
    };
}
