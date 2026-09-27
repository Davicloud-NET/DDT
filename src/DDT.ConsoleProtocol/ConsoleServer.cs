// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Address is the server's URL as the boot image names it. Problem is why the last request failed, in the words the agent
// logs, such as "the server at ddt.example:8443 did not accept a connection within 10 s (tried 10.0.0.5, name lookup
// 0.0 s)", until a request gets through. FailedStage is how far that request got, where the agent can tell, and
// Failures how many requests in a row failed. The agent tries again by itself.
public sealed record ConsoleServer(string Address, string? Problem = null, ConnectionStage? FailedStage = null, int Failures = 0);
