// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The agent netbooting machines switch to. Null values when there is none. An upload answers with it as well.
public sealed record AgentBinaryView(string? Sha256, long? Size, DateTimeOffset? UploadedUtc, string? UploadedBy, AgentBinarySource Source);
