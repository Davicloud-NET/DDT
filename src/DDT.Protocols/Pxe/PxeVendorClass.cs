// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Pxe;

// PXE 2.1 section 2.5.1.1: a redirection service must only respond to messages carrying option 60
// with the value PXEClient. Clients send the long "PXEClient:Arch:xxxxx:UNDI:yyyzzz" form, so this
// is a case sensitive prefix match, and the reply echoes the short form.
public static class PxeVendorClass
{
    public const string Pxe = "PXEClient";
    public const string Http = "HTTPClient";

    public static bool TryMatch(string? vendorClassIdentifier, out string family)
    {
        family = string.Empty;

        if (string.IsNullOrEmpty(vendorClassIdentifier))
        {
            return false;
        }

        if (vendorClassIdentifier.StartsWith(Http, StringComparison.Ordinal))
        {
            family = Http;
            return true;
        }

        if (vendorClassIdentifier.StartsWith(Pxe, StringComparison.Ordinal))
        {
            family = Pxe;
            return true;
        }

        return false;
    }
}
