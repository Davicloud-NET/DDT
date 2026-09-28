// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Address is the server's URL as the boot image names it. Problem says why the last request failed, in the words the
// agent logs, until one gets through. Failures counts the failures in a row. The agent retries by itself.
public sealed record ConsoleServer(string Address, string? Problem = null, ConnectionStage? FailedStage = null, int Failures = 0);
