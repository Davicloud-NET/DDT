// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

public enum AuthenticodeStatus
{
    // A signature that matches the file chains to one of the trusted certificates.
    Trusted,

    // A signature matches the file, but chains to none of the trusted certificates.
    SignedByOthers,

    // No signature, or none that matches the file.
    NotSigned,

    // The file or its signatures cannot be read.
    Unreadable,
}
