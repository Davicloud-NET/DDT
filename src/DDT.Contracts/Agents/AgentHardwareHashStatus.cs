// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Contracts.Agents;

// How far the upload of the machine's latest hardware hash is. Message says why it failed.
public sealed record AgentHardwareHashStatus(HardwareHashUploadState State, string? Message);
