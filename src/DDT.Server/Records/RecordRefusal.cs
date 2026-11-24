// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Records;

// Why the server did not take a record, as the agent's problem title says it. Never names a password.
public sealed record RecordRefusal(int StatusCode, string Title);
