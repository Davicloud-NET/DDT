// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.BootImage;

// KeyboardLayout is an input locale and layout such as 0407:00000407, or null for the server's own first layout.
public sealed record BuildBootImageRequest(string? KeyboardLayout, bool SkipPowerShell = false, int? TftpWindowSize = null);
