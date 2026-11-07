// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Boot;

namespace DDT.Pxe;

// The third initrd, made for each request: /ddt/agent.json with this server's address and root certificate.
public static class SiteArchive
{
    public const string AgentFolder = "ddt";

    public static byte[] Write(LinuxBootBundle bundle, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        byte[] agent = AgentConfigurationFile.Write(bundle.ServerUrl, bundle.RootCertificatePem, bundle.KeyboardLayout);

        return CpioWriter.Write(
            [CpioEntry.Folder(AgentFolder), CpioEntry.File(AgentFolder + "/" + AgentConfigurationFile.Name, agent)],
            now);
    }
}
