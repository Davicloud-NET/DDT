// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// What a machine without a run starts. Auto is Windows PE once a boot image is built, and Linux until then.
public enum DefaultBootEnvironment
{
    Auto,
    WindowsPE,
    Linux,
}
