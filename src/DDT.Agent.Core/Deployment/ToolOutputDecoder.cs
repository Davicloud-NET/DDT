// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Reads a line of a tool's output that isn't UTF-8, in the encoding the system's older programs write.
public delegate string ToolOutputDecoder(ReadOnlySpan<byte> bytes);
