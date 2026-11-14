// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Records;

public static class RecordLimits
{
    public const int MaxRequestBytes = 32 * 1024;

    // Windows reports about 4,000 characters.
    public const int MaxHashLength = 8000;

    // As long as the machine's own serial number may be.
    public const int MaxSerialNumberLength = 128;

    public const int MaxGroupTagLength = 128;

    public const int VolumeLength = 2;

    public const int PasswordLength = 55;

    // Autopilot's own words for a failed import.
    public const int MaxUploadMessageLength = 1024;

    public const int MaxImportIdLength = 64;
}
