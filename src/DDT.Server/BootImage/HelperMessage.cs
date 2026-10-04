// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// One line of the helper's answer: a line of output, or the end with an exit code and, when it failed, why.
public sealed record HelperMessage(string? Line = null, int? ExitCode = null, string? Problem = null);
