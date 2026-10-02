// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Server;

// What a new server still needs before its first deployment, each true once the server sees it done: the first
// password changed, an interface answering netboot, a boot image, a Windows image, a task sequence, a machine.
public sealed record SetupChecklist(bool Password, bool Netboot, bool BootImage, bool Image, bool Sequence, bool Machine);
