// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

// A pair waiting to be confirmed by DeadlineUtc. Thumbprint is matched against the thumbprint the confirming
// connection was served.
public sealed record ProvisionalCertificate(string Thumbprint, DateTimeOffset DeadlineUtc);
