// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Packages;

// Drivers go to the machines whose model a target names. Files are unpacked for a Run script step that names them.
public enum PackageKind
{
    Drivers,
    Files,
}
