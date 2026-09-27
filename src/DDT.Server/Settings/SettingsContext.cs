// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using DDT.Server.Security;

namespace DDT.Server.Settings;

// What a section's rules may need of other sections and of configuration alone. Saving names the section a save
// changes, for the rules that are checked on a save of either of two sections but belong to one of them at load.
public sealed class SettingsContext
{
    public required MachineOptions Machines { get; init; }

    public required DdtForwardedHeadersOptions Proxies { get; init; }

    public required int HttpBootPort { get; init; }

    public string? Saving { get; init; }
}
