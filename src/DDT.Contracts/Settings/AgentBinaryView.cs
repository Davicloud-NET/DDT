// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The agent that netbooting machines switch to. The values are null when there's none. An upload returns it too.
// Version is the file version, where the file has one. NewerBundledVersion is set while an upload is offered and the
// server came with a newer one, which removing the upload brings back.
public sealed record AgentBinaryView(
    string? Sha256,
    long? Size,
    DateTimeOffset? UploadedUtc,
    string? UploadedBy,
    AgentBinarySource Source,
    string? Version = null,
    string? NewerBundledVersion = null);
