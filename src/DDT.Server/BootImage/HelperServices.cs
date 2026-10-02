// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// The helper's answer about the DHCP server and WDS of this computer. Windows hides the DHCP server's state from the
// web server's account, and the helper, as SYSTEM, sees it.
public sealed record HelperServices(HelperServiceState Dhcp, HelperServiceState Wds);
