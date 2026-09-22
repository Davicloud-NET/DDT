// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Images;

// What a completed upload becomes: images from a WIM, or a package from a zip of drivers or of files.
public enum UploadKind
{
    Image,
    Drivers,
    Files,
}
