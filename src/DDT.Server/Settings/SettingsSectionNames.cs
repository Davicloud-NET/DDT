// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// The rows of ddt."SettingsSections". Certificate is reserved for the server names of the certificate section, and
// keyring holds the value that tells whether this process reads the key ring the stored secrets were encrypted with.
public static class SettingsSectionNames
{
    public const string Deployment = "deployment";
    public const string Machines = "machines";
    public const string Ldap = "ldap";
    public const string Oidc = "oidc";
    public const string Proxies = "proxies";
    public const string Pxe = "pxe";
    public const string Logging = "logging";
    public const string Certificate = "certificate";
    public const string KeyRing = "keyring";
}
