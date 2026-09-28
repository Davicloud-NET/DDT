// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole;

internal static class ConsoleBuild
{
    public static string Version => typeof(ConsoleBuild).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
