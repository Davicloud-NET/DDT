// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Installed when the file exists and, with a minimum version, its file version is at least that. The path may start
// with an environment variable such as %ProgramFiles%.
public sealed record FileDetection(string Path, string? MinimumVersion = null) : ApplicationDetection;
