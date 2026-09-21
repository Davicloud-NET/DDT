// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Host.Logging;

internal static partial class HostLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Active roles: {Roles}")]
    public static partial void ActiveRoles(ILogger logger, string roles);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Generated a self signed TLS certificate at {Path}. Replace it with your own, or distribute it as a trusted root.")]
    public static partial void GeneratedCertificate(ILogger logger, string path);
}
