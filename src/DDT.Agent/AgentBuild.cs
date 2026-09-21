// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

public static class AgentBuild
{
    // Only dotnet publish defines DDT_PUBLISHED. RuntimeFeature cannot tell: PublishAot switches dynamic code off
    // in the JIT build as well.
#if DDT_PUBLISHED
    public static bool IsPublished => true;
#else
    public static bool IsPublished => false;
#endif
}
