// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Autopilot;

// Windows Autopilot's device import. A refusal throws AutopilotDirectoryException with the service's words; a network
// failure throws HttpRequestException and may pass on its own.
public interface IAutopilotDirectory
{
    Task<AutopilotImport> ImportAsync(AutopilotOptions account, AutopilotDevice device, CancellationToken cancellationToken);

    Task<AutopilotImport> ReadAsync(AutopilotOptions account, string importId, CancellationToken cancellationToken);
}
