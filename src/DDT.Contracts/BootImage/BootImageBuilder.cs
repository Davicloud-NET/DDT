// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// Whether this server can build the boot image itself: a Windows server whose helper service answers. ServerUrl is the
// address a build puts into the image for the agent. Adk is null where the server cannot tell, as on Linux.
public sealed record BootImageBuilder(bool Available, string ServerUrl, BootImageAdk? Adk, bool Package = false);
