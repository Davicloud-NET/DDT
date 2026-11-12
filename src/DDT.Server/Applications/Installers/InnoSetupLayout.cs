// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Applications.Installers;

// How a version of Inno Setup lays out the start of setup-0.bin. Unicode stores the header's strings as UTF-16, else in
// the ANSI code page. 6.5 added an encryption header before the first block, 6.7 a 64-bit block size. CreateKeyIndex
// is the header string CreateUninstallRegKey; Uninstallable follows it.
internal sealed record InnoSetupLayout(bool Unicode, bool EncryptionHeader, bool LongBlockSize, int CreateKeyIndex);
