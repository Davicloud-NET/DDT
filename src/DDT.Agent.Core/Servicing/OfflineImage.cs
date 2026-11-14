// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Servicing;

// The applied Windows DISM changes, its scratch directory on the target disk and the log of one call.
public sealed record OfflineImage(string Windows, string Scratch, string Log);
