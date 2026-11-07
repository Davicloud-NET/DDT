// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

// What the description says of base.img. Keymaps are the keyboard layouts ddt.keymap can name.
internal sealed class LinuxBootManifestBaseImage
{
    public List<string>? Keymaps { get; set; }
}
